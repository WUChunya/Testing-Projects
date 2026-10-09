namespace PD.Core.Drawing;

/// <summary>Approximate text metrics for placing labels without a font engine.</summary>
public static class TextFit
{
    const double CharWidth = 0.6;   // average glyph width / size for a geometric sans, caps
    const double LineHeight = 1.2;

    public static double Width(string s, double size) => s.Length * CharWidth * size;

    /// <summary>Word-wraps to fit a box; null when it cannot fit even after wrapping.</summary>
    public static string? Wrap(string text, double size, double maxW, double maxH)
    {
        var maxChars = (int)Math.Floor(maxW / (CharWidth * size));
        var maxLines = (int)Math.Floor(maxH / (LineHeight * size));
        if (maxChars < 2 || maxLines < 1) return null;
        var lines = new List<string>();
        var cur = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Length > maxChars) return null;
            var next = cur.Length == 0 ? word : cur + " " + word;
            if (next.Length <= maxChars) { cur = next; continue; }
            lines.Add(cur);
            cur = word;
        }
        if (cur.Length > 0) lines.Add(cur);
        return lines.Count <= maxLines ? string.Join("\n", lines) : null;
    }

    public static double Height(string wrapped, double size) => wrapped.Split('\n').Length * LineHeight * size;
}
