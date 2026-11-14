// Inspects an .ipa on Windows (no Mac needed): full bundle tree, Info.plist keys (binary plists decoded),
// Mach-O slices/platforms, signing presence, and the checks iOS applies before offering the watch app.
// Usage (.NET 10 SDK):  dotnet run tools/inspect-ipa.cs -- path\to\WatchPointer.ipa
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

var ipa = args[0];
var root = Path.Combine(Path.GetTempPath(), "inspect-ipa-" + Guid.NewGuid().ToString("N"));
ZipFile.ExtractToDirectory(ipa, root);
try
{
Console.WriteLine("=== Bundle hierarchy");
foreach (var e in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories).Order())
    Console.WriteLine($"  {(Directory.Exists(e) ? "d" : "f")} {Rel(e)}");
var plists = new Dictionary<string, Dictionary<string, object?>>();
string Rel(string path) => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
foreach (var plist in Directory.EnumerateFiles(root, "*.plist", SearchOption.AllDirectories).Order())
{
    Console.WriteLine($"=== {Path.GetRelativePath(root, plist)}");
    var data = File.ReadAllBytes(plist);
    if (Encoding.ASCII.GetString(data, 0, Math.Min(8, data.Length)) == "bplist00")
        { var dict = (Dictionary<string, object?>)new BPlist(data).Root()!; plists[Rel(plist)] = dict; foreach (var (k, v) in dict) Console.WriteLine($"  {k} = {Show(v)}"); }
    else Console.WriteLine(Encoding.UTF8.GetString(data));
}
foreach (var app in Directory.EnumerateDirectories(root, "*.app", SearchOption.AllDirectories).Order())
{
    var exe = Path.Combine(app, Path.GetFileNameWithoutExtension(app));
    if (!File.Exists(exe)) continue;
    Console.WriteLine($"=== Mach-O {Path.GetRelativePath(root, exe)}");
    MachO.Dump(File.ReadAllBytes(exe));
    Console.WriteLine($"  _CodeSignature: {Directory.Exists(Path.Combine(app, "_CodeSignature"))}, embedded.mobileprovision: {File.Exists(Path.Combine(app, "embedded.mobileprovision"))}");
}

// ---- Checks
var phone = plists.Keys.Single(k => k.Count(c => c == '/') == 2 && k.EndsWith(".app/Info.plist"));
var watchKey = plists.Keys.SingleOrDefault(k => k.Contains(".app/Watch/") && k.EndsWith(".app/Info.plist"));
var fail = false;
void Check(string name, bool ok) { Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {name}"); fail |= !ok; }
string? S(Dictionary<string, object?> d, string k) => d.TryGetValue(k, out var v) ? v?.ToString() : null;
Console.WriteLine("=== Checks");
var p = plists[phone];
var iosId = S(p, "CFBundleIdentifier");
Check("iOS app platform is iphoneos", S(p, "DTPlatformName") == "iphoneos");
Check("watch app embedded at <iOS app>/Watch/*.app", watchKey is not null);
if (watchKey is not null)
{
    var w = plists[watchKey];
    var wid = S(w, "CFBundleIdentifier");
    Check("watch platform is watchos", S(w, "DTPlatformName") == "watchos");
    Check("WKApplication = true (single-target watch app)", w.TryGetValue("WKApplication", out var a) && a is true);
    Check("companion app, not watch-only (no WKWatchOnly)", !w.ContainsKey("WKWatchOnly"));
    Check($"WKCompanionAppBundleIdentifier ({S(w, "WKCompanionAppBundleIdentifier")}) == iOS bundle ID ({iosId})", S(w, "WKCompanionAppBundleIdentifier") == iosId);
    Check($"watch bundle ID ({wid}) starts with iOS bundle ID + '.'", wid is not null && iosId is not null && wid.StartsWith(iosId + "."));
}
Environment.ExitCode = fail ? 1 : 0;
}
finally { Directory.Delete(root, true); }
static string Show(object? v) => v switch
{
    null => "null",
    string s => $"\"{s}\"",
    List<object?> l => "[" + string.Join(", ", l.Select(Show)) + "]",
    Dictionary<string, object?> d => "{" + string.Join(", ", d.Select(kv => $"{kv.Key}: {Show(kv.Value)}")) + "}",
    byte[] b => $"<{b.Length} bytes>",
    bool b => b ? "true" : "false",
    _ => v.ToString()!,
};

sealed class BPlist(byte[] d)
{
    int offSize, refSize; long[] offsets = [];
    public object? Root()
    {
        var t = d.AsSpan(d.Length - 32);
        offSize = t[6]; refSize = t[7];
        var count = (long)BinaryPrimitives.ReadUInt64BigEndian(t[8..]);
        var top = (long)BinaryPrimitives.ReadUInt64BigEndian(t[16..]);
        var table = (long)BinaryPrimitives.ReadUInt64BigEndian(t[24..]);
        offsets = new long[count];
        for (var i = 0; i < count; i++) offsets[i] = Int(table + i * offSize, offSize);
        return Obj(top);
    }
    long Int(long at, int size) { long v = 0; for (var i = 0; i < size; i++) v = (v << 8) | d[at + i]; return v; }
    (long len, long start) Len(long at)
    {
        var low = d[at] & 0xF;
        if (low != 0xF) return (low, at + 1);
        var sz = 1 << (d[at + 1] & 0xF);
        return (Int(at + 2, sz), at + 2 + sz);
    }
    object? Obj(long r)
    {
        var at = offsets[r]; var marker = d[at]; var hi = marker >> 4;
        switch (hi)
        {
            case 0: return marker == 9 ? true : marker == 8 ? false : null;
            case 1: return Int(at + 1, 1 << (marker & 0xF));
            case 2: return (marker & 0xF) == 3 ? BitConverter.Int64BitsToDouble(Int(at + 1, 8)) : (double)BitConverter.Int32BitsToSingle((int)Int(at + 1, 4));
            case 4: { var (n, s) = Len(at); return d[(int)s..(int)(s + n)]; }
            case 5: { var (n, s) = Len(at); return Encoding.ASCII.GetString(d, (int)s, (int)n); }
            case 6: { var (n, s) = Len(at); return Encoding.BigEndianUnicode.GetString(d, (int)s, (int)n * 2); }
            case 0xA: { var (n, s) = Len(at); var l = new List<object?>(); for (var i = 0; i < n; i++) l.Add(Obj(Int(s + i * refSize, refSize))); return l; }
            case 0xD:
            {
                var (n, s) = Len(at); var dict = new Dictionary<string, object?>();
                for (var i = 0; i < n; i++) dict[(string)Obj(Int(s + i * refSize, refSize))!] = Obj(Int(s + (n + i) * refSize, refSize));
                return dict;
            }
            default: return $"<type {hi:X}>";
        }
    }
}

static class MachO
{
    public static void Dump(byte[] b)
    {
        var magic = BinaryPrimitives.ReadUInt32BigEndian(b);
        if (magic == 0xCAFEBABE)
        {
            var n = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(4));
            for (var i = 0; i < n; i++)
            {
                var e = b.AsSpan(8 + i * 20);
                var off = (int)BinaryPrimitives.ReadUInt32BigEndian(e[8..]);
                var size = (int)BinaryPrimitives.ReadUInt32BigEndian(e[12..]);
                Slice(b.AsSpan(off, size).ToArray());
            }
        }
        else Slice(b);
    }

    static void Slice(byte[] b)
    {
        var magic = BinaryPrimitives.ReadUInt32LittleEndian(b);
        var is64 = magic == 0xFEEDFACF;
        var cpu = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(4));
        var sub = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(8)) & 0xFF;
        var arch = cpu switch { 0x0100000C => "arm64", 0x0200000C => "arm64_32", 0x01000007 => "x86_64", _ => $"cpu 0x{cpu:X}" };
        var ncmds = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(16));
        var p = is64 ? 32 : 28;
        var platforms = new Dictionary<uint, string> { [1] = "macOS", [2] = "iOS", [4] = "watchOS", [7] = "iOSSimulator", [9] = "watchOSSimulator" };
        Console.WriteLine($"  slice {arch}{(sub != 0 ? $" (subtype {sub})" : "")}");
        var hasSig = false;
        for (var i = 0; i < ncmds; i++)
        {
            var cmd = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p));
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p + 4));
            switch (cmd)
            {
                case 0x32: // LC_BUILD_VERSION
                    var plat = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p + 8));
                    var min = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p + 12));
                    var sdk = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p + 16));
                    Console.WriteLine($"    platform {platforms.GetValueOrDefault(plat, plat.ToString())} min {min >> 16}.{(min >> 8) & 0xFF} sdk {sdk >> 16}.{(sdk >> 8) & 0xFF}");
                    break;
                case 0xC: case 0x80000018: case 0x8000001F: // LC_LOAD_DYLIB, WEAK, REEXPORT
                    var nameOff = (int)BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p + 8));
                    var name = Encoding.ASCII.GetString(b, p + nameOff, size - nameOff).TrimEnd('\0');
                    if (!name.StartsWith("/usr/lib/swift/") && !name.StartsWith("/System/Library/Frameworks/")) Console.WriteLine($"    links {name}");
                    break;
                case 0x1D: hasSig = true; break; // LC_CODE_SIGNATURE
            }
            p += size;
        }
        Console.WriteLine($"    LC_CODE_SIGNATURE: {hasSig}");
    }
}
