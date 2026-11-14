using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WatchPointer.Core.Input;
using WatchPointer.Core.Pointer;
using WatchPointer.Core.Protocol;
using static WatchPointer.Platform.Native.NativeMethods;

namespace WatchPointer.Platform.Input;

public enum CursorApi
{
    /// <summary>Injected absolute mouse input over the virtual desktop (default; works with drag and most apps).</summary>
    SendInput,
    /// <summary>SetCursorPos: exact pixels, but not seen as mouse input by apps that read raw input.</summary>
    SetCursorPos,
}

/// <summary>
/// Moves the cursor and sends clicks, wheel and keys with SendInput.
/// <para>Limits imposed by Windows (documented in docs/TROUBLESHOOTING.md): input can't reach windows of elevated
/// (admin) apps unless WatchPointer itself runs elevated (UIPI), and secure-desktop keys such as Ctrl+Alt+Del or Win+L
/// can't be injected at all.</para>
/// </summary>
public sealed class Win32InputSink : IInputSink
{
    private readonly ILogger _log;
    private long _failures;

    public Win32InputSink(ILogger<Win32InputSink>? log = null) => _log = (ILogger?)log ?? NullLogger.Instance;

    public CursorApi Api { get; set; } = CursorApi.SendInput;
    public long Failures => Interlocked.Read(ref _failures);

    public PointI CursorPosition => GetCursorPos(out var p) ? new PointI(p.X, p.Y) : default;

    public unsafe void MoveTo(PointI position)
    {
        if (Api == CursorApi.SetCursorPos)
        {
            if (!SetCursorPos(position.X, position.Y)) Fail("SetCursorPos");
            return;
        }
        var vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        var input = Mouse(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, 0,
            AbsoluteMouseCoordinates.Normalize(position.X, vx, vw), AbsoluteMouseCoordinates.Normalize(position.Y, vy, vh));
        Send(&input, 1);
    }

    public unsafe void Button(MouseButton button, bool down)
    {
        var flags = button switch
        {
            MouseButton.Left => down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP,
            MouseButton.Right => down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP,
            _ => down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP,
        };
        var input = Mouse(flags, 0, 0, 0);
        Send(&input, 1);
    }

    public unsafe void Wheel(int delta, bool horizontal)
    {
        if (delta == 0) return;
        var input = Mouse(horizontal ? MOUSEEVENTF_HWHEEL : MOUSEEVENTF_WHEEL, unchecked((uint)delta), 0, 0);
        Send(&input, 1);
    }

    public unsafe void Keys(ReadOnlySpan<KeyStroke> strokes)
    {
        if (strokes.IsEmpty) return;
        Span<INPUT> inputs = strokes.Length <= 32 ? stackalloc INPUT[strokes.Length] : new INPUT[strokes.Length];
        for (var i = 0; i < strokes.Length; i++)
        {
            var vk = strokes[i].VirtualKey;
            var flags = strokes[i].Down ? 0u : KEYEVENTF_KEYUP;
            if (VirtualKeys.IsExtended(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
            inputs[i] = new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = (ushort)MapVirtualKeyW(vk, MAPVK_VK_TO_VSC), dwFlags = flags, dwExtraInfo = ExtraInfoTag } },
            };
        }
        fixed (INPUT* p = inputs) Send(p, inputs.Length);
    }

    public unsafe void Text(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var inputs = new INPUT[text.Length * 2];
        for (var i = 0; i < text.Length; i++)
        {
            inputs[2 * i] = Unicode(text[i], false);
            inputs[2 * i + 1] = Unicode(text[i], true);
        }
        fixed (INPUT* p = inputs) Send(p, inputs.Length);
    }

    private static INPUT Unicode(char c, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0), dwExtraInfo = ExtraInfoTag } },
    };

    private static INPUT Mouse(uint flags, uint data, int dx, int dy) => new()
    {
        type = INPUT_MOUSE,
        u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags, dwExtraInfo = ExtraInfoTag } },
    };

    private unsafe void Send(INPUT* inputs, int count)
    {
        var sent = SendInput((uint)count, inputs, sizeof(INPUT));
        if (sent != count) Fail("SendInput");
    }

    private void Fail(string api)
    {
        // Usually UIPI: the foreground window belongs to an elevated process. Log rarely; this can happen at 240 Hz.
        if (Interlocked.Increment(ref _failures) % 500 == 1)
            _log.LogWarning("{Api} was blocked (error {Error}). Is an elevated (admin) window in front?", api, Marshal.GetLastPInvokeError());
    }
}
