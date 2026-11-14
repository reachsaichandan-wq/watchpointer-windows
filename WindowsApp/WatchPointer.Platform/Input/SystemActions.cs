using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using static WatchPointer.Platform.Native.NativeMethods;

namespace WatchPointer.Platform.Input;

/// <summary>Windows actions that can't be done with injected keystrokes.</summary>
public sealed class SystemActions(ILogger<SystemActions>? log = null)
{
    private readonly ILogger _log = (ILogger?)log ?? NullLogger.Instance;

    public bool LockPc()
    {
        var ok = LockWorkStation();
        if (!ok) _log.LogWarning("LockWorkStation failed");
        return ok;
    }

    /// <summary>Opens the Windows On-Screen Keyboard (osk.exe), which works on every Windows edition.</summary>
    public bool ShowKeyboard()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "osk.exe");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            _log.LogWarning(ex, "Could not start the On-Screen Keyboard");
            return false;
        }
    }
}
