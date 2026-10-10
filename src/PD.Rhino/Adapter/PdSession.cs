using Rhino;
using PD.Core.Analysis;
using PD.Core.Classification;
using PD.Core.Config;
using PD.Core.Settings;

namespace PD.Rhino.Adapter;

/// <summary>Per-document state shared by the panel tabs: settings (persisted in the 3dm), extraction and analysis.</summary>
public sealed class PdSession
{
    const string Section = "ProgramDiagram";
    const string Entry = "settings";
    static readonly Dictionary<uint, PdSession> Sessions = new();

    public uint DocSerial { get; }
    public RhinoDoc? Doc => RhinoDoc.FromRuntimeSerialNumber(DocSerial);
    public PdSettings Settings { get; private set; }
    public Rules Rules { get; } = Rules.Default();
    readonly List<ProgramDef> _legend = LayerClassifier.LoadPrograms();
    LayerClassifier? _classifier;

    /// <summary>Company legend, or the project scheme with custom programs (tab 5).</summary>
    public List<ProgramSetting> Programs => ProgramCatalog.Effective(_legend, Settings.Programs);

    /// <summary>The scheme as an editable list stored in the settings (materialised from the legend on first edit).</summary>
    public List<ProgramSetting> EditablePrograms() => Settings.Programs ??= ProgramCatalog.Effective(_legend, null);

    public LayerClassifier Classifier => _classifier ??= ProgramCatalog.Classifier(Programs);
    public Extraction? Extraction { get; private set; }
    public AnalysisResult? Result { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Raised after analysis or any settings change that other tabs display.</summary>
    public event Action? Changed;

    PdSession(uint serial)
    {
        DocSerial = serial;
        Settings = PdSettings.FromJson(Doc?.Strings.GetValue(Section, Entry));
    }

    public static PdSession For(uint docSerial)
    {
        if (!Sessions.TryGetValue(docSerial, out var s)) Sessions[docSerial] = s = new PdSession(docSerial);
        return s;
    }

    public static void Forget(uint docSerial) => Sessions.Remove(docSerial);

    public void Save()
    {
        Doc?.Strings.SetString(Section, Entry, Settings.ToJson());
    }

    /// <summary>Call after adding, removing or re-aliasing programs so tab 1 and auto-fill see them.</summary>
    public void ProgramsChanged()
    {
        _classifier = null;
        NotifyChanged();
    }

    public void ReplaceSettings(PdSettings s)
    {
        Settings = s;
        _classifier = null;
        Save();
        Changed?.Invoke();
    }

    public string ProgramLabel(string key) =>
        Programs.FirstOrDefault(p => p.Key == key)?.Label ?? key;

    /// <summary>Reads the document and runs the Core pipeline with the saved user choices.</summary>
    public bool Analyze()
    {
        var doc = Doc;
        if (doc == null) return false;
        try
        {
            Extraction = RhinoExtractor.Extract(doc, Settings.SingleSurfaces, Rules.Cleanup.SingleSurfaceExtrudeFt);
            EnsureLayerSettings();
            Result = Analyzer.Run(new AnalysisInput
            {
                Model = Extraction.Model,
                Rules = Rules,
                Classifier = Classifier,
                RoleOverrides = Settings.RoleOverrides(),
                ProgramOverrides = Settings.ObjectPrograms,
                SpanOverrides = Settings.SpanModes,
                SingleSurfaceChoices = Settings.SingleSurfaces,
                KeepStray = Settings.KeepStray.ToHashSet(),
                Levels = Settings.Levels,
            });
            AddSectionIssues(Result);
            LastError = null;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            RhinoApp.WriteLine($"Program Diagram: analysis failed: {ex}");
        }
        Save();
        Changed?.Invoke();
        return LastError == null;
    }

    /// <summary>B2 "section line misses every mass".</summary>
    void AddSectionIssues(AnalysisResult r)
    {
        foreach (var sec in Settings.Sections.Where(x => x.Line.Length == 4 && !x.IsElevation))
        {
            var (ax, ay, _) = r.Frame.ToLocal(sec.Line[0], sec.Line[1], 0);
            var (bx, by, _) = r.Frame.ToLocal(sec.Line[2], sec.Line[3], 0);
            if (!Reports.SectionCrossesMasses(r, ax, ay, bx, by))
                r.Issues.Add(new Issue(IssueKind.SectionMissesMasses, $"Section {sec.Name} does not cross any mass", Array.Empty<string>()));
        }
    }

    /// <summary>First run: keyword pre-fill for every layer that has objects; hidden layers start unchecked.</summary>
    void EnsureLayerSettings()
    {
        if (Extraction == null) return;
        foreach (var l in Extraction.Model.Layers.Where(l => Extraction.ObjectCountByLayer.ContainsKey(l.Path)))
        {
            if (Settings.Layers.ContainsKey(l.Path)) continue;
            var g = Classifier.Guess(l);
            Settings.Layers[l.Path] = new LayerSetting { Use = l.Visible, Role = g.Role, ProgramKey = g.ProgramKey, Mode = DefaultMode(g.Role) };
        }
    }

    public static string DefaultMode(string role) => role switch
    {
        Roles.Program or Roles.Ignore => "",
        Roles.Building or Roles.Slab or Roles.Base => "grayscale",
        Roles.Facade or "BG.Line" => "line",
        _ => "color",
    };

    public bool IsConfirmed(string tab) => Settings.Confirmed.GetValueOrDefault(tab);

    public void SetConfirmed(string tab, bool value)
    {
        Settings.Confirmed[tab] = value;
        Save();
        Changed?.Invoke();
    }

    public void NotifyChanged()
    {
        Save();
        Changed?.Invoke();
    }
}
