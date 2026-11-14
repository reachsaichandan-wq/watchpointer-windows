namespace WatchPointer.Core.Protocol;

/// <summary>Hardware identifier to marketing name (from the original project's table). Unknown ids fall back.</summary>
public static class WatchModels
{
    private static readonly Dictionary<string, string> Names = Build();

    private static Dictionary<string, string> Build()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        void Range(int major, int from, int to, string name) { for (var i = from; i <= to; i++) d[$"Watch{major},{i}"] = name; }
        Range(4, 1, 4, "Apple Watch Series 4");
        Range(5, 1, 4, "Apple Watch Series 5");
        Range(5, 9, 12, "Apple Watch SE");
        Range(6, 1, 4, "Apple Watch Series 6");
        Range(6, 6, 9, "Apple Watch Series 7");
        Range(6, 10, 13, "Apple Watch SE (2nd generation)");
        Range(6, 14, 17, "Apple Watch Series 8");
        d["Watch6,18"] = "Apple Watch Ultra";
        Range(7, 1, 4, "Apple Watch Series 9");
        d["Watch7,5"] = "Apple Watch Ultra 2";
        Range(7, 8, 11, "Apple Watch Series 10");
        d["Watch7,12"] = "Apple Watch Ultra 3";
        Range(7, 13, 16, "Apple Watch SE 3");
        Range(7, 17, 20, "Apple Watch Series 11");
        return d;
    }

    public static string NameFor(string model) =>
        Names.TryGetValue(model, out var name) ? name
        : model.StartsWith("Watch", StringComparison.Ordinal) ? "Apple Watch"
        : string.IsNullOrEmpty(model) ? "Unknown device" : model;
}
