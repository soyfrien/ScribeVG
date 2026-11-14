namespace ScribeVG.Services;

/// <summary>Groups KanjiVG <c>kvg:type</c> values (e.g. "㇑a", "㇔/㇀") into base CJK stroke types.</summary>
public static class StrokeTypes
{
    private static readonly Dictionary<string, string> Names = new()
    {
        ["㇀"] = "rising flick", ["㇁"] = "curved hook", ["㇂"] = "slanted hook", ["㇃"] = "bent hook",
        ["㇄"] = "down-right turn", ["㇅"] = "turn-down-turn", ["㇆"] = "turn-hook", ["㇇"] = "turn-sweep",
        ["㇈"] = "turn-turn-hook", ["㇉"] = "down-turn-hook", ["㇊"] = "turn-rise", ["㇋"] = "turn-sweep-turn",
        ["㇌"] = "turn-sweep-hook", ["㇍"] = "turn-turn", ["㇎"] = "multi-turn", ["㇏"] = "right sweep",
        ["㇐"] = "horizontal", ["㇑"] = "vertical", ["㇒"] = "left sweep", ["㇓"] = "vertical sweep",
        ["㇔"] = "dot", ["㇕"] = "horizontal turn", ["㇖"] = "horizontal hook", ["㇗"] = "vertical turn",
        ["㇘"] = "vertical turn-left", ["㇙"] = "vertical rise", ["㇚"] = "vertical hook", ["㇛"] = "sweep-dot",
        ["㇜"] = "sweep-turn", ["㇝"] = "long sweep", ["㇞"] = "zigzag", ["㇟"] = "bent-hook turn",
        ["㇠"] = "turn-bent-hook", ["㇡"] = "zigzag hook", ["㇢"] = "slant hook", ["㇣"] = "circle"
    };

    /// <summary>"㇑a" → "㇑", "㇔/㇀" → "㇔"; null when there's no CJK stroke letter.</summary>
    public static string? Normalize(string? kvgType)
    {
        if (string.IsNullOrWhiteSpace(kvgType)) return null;
        var first = kvgType.Split('/')[0].Trim();
        foreach (var ch in first)
        {
            if (ch is >= '\u31C0' and <= '\u31EF') return ch.ToString();
        }
        return null;
    }

    public static string Name(string type) => Names.TryGetValue(type, out var name) ? name : "stroke";
}
