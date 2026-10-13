using System.Text.Json;
using PD.Core.Styles;

namespace PD.Core.Entourage;

/// <summary>One silhouette: parts (compound paths) of rings, normalised to height 1, origin at the bottom centre, y up.</summary>
public sealed class LibraryFigure
{
    public string Id { get; set; } = "";
    /// <summary>Width ÷ height.</summary>
    public double Aspect { get; set; }
    public List<List<double[][]>> Parts { get; set; } = new();
}

/// <summary>
/// styles/entourage_library.json: the office people (ENTOURAGES.ai) and elevation trees (TREES.ai), extracted by
/// tools/entourage_from_ai.py.
/// </summary>
public sealed class EntourageLibrary
{
    public const string FileName = "entourage_library.json";

    public List<LibraryFigure> People { get; set; } = new();
    public List<LibraryFigure> Trees { get; set; } = new();

    static readonly Lazy<EntourageLibrary> Embedded = new(() => Load(StyleResources.ReadText(FileName)));

    public static EntourageLibrary Default => Embedded.Value;

    public static EntourageLibrary Load(string json) =>
        JsonSerializer.Deserialize<EntourageLibrary>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new EntourageLibrary();
}
