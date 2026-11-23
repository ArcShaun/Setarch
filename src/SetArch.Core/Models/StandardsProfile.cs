using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SetArch.Core.Models;

/// <summary>Minimal INotifyPropertyChanged base so the WPF editor can bind straight to the model.</summary>
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>
/// The office's standards: which questions to ask, the project folder tree, and which template files
/// go where. Saved as JSON so it can be shared (e.g. on a network drive).
/// </summary>
public sealed class StandardsProfile
{
    public const int CurrentVersion = 2;

    /// <summary>File format version, used to upgrade standards saved by older versions of SetArch.</summary>
    /// <summary>0 for files saved before versioning; new profiles are created at <see cref="CurrentVersion"/>.</summary>
    public int Version { get; set; }

    public string Name { get; set; } = "My standards";

    /// <summary>Placeholders look like <c>[[PROJECT_NAME]]</c> by default.</summary>
    public string TokenPrefix { get; set; } = "[[";
    public string TokenSuffix { get; set; } = "]]";

    public ObservableCollection<FieldDefinition> Fields { get; set; } = new();

    /// <summary>The project folder itself; its name is a pattern such as "[[PROJECT_NUMBER]] - [[PROJECT_NAME]]".</summary>
    public FolderNode Root { get; set; } = new() { Name = "[[PROJECT_NUMBER]] - [[PROJECT_NAME]]" };

    public ObservableCollection<TemplateFile> Files { get; set; } = new();

    /// <summary>
    /// Long answers are also available split over two lines, e.g. [[PROJECT_NAME_1]] and [[PROJECT_NAME_2]],
    /// breaking between words so line 1 is at most this many characters.
    /// </summary>
    public int WrapLength { get; set; } = 25;

    /// <summary>
    /// Title block lines built from the answers, e.g. LINE1 = [[PROJECT_TYPE]]. Each is filled into
    /// attributes tagged with its key and into [[KEY]] placeholders, like a question.
    /// </summary>
    public ObservableCollection<DerivedField> TitleBlockFields { get; set; } = new();

    /// <summary>Path to AutoCAD's accoreconsole.exe. Empty = detect automatically.</summary>
    public string AutoCadConsolePath { get; set; } = "";

    public string Token(string key) => TokenPrefix + key + TokenSuffix;

    /// <summary>The default title block: 5 lines, job number, date, copyright year.</summary>
    public static void AddDefaultTitleBlock(StandardsProfile p)
    {
        p.TitleBlockFields.Add(new DerivedField("LINE1", p.Token("PROJECT_TYPE"), "Line 1 - project type"));
        p.TitleBlockFields.Add(new DerivedField("LINE2", p.Token("PROJECT_NAME_1"), "Line 2 - project name"));
        p.TitleBlockFields.Add(new DerivedField("LINE3", p.Token("PROJECT_NAME_2"), "Line 3 - rest of a long project name"));
        p.TitleBlockFields.Add(new DerivedField("LINE4", p.Token("ADDRESS_1"), "Line 4 - street address"));
        p.TitleBlockFields.Add(new DerivedField("LINE5", p.Token("ADDRESS_2"), "Line 5 - city, state ZIP"));
        p.TitleBlockFields.Add(new DerivedField("JOB_NO", p.Token("PROJECT_NUMBER"), "Job number"));
        p.TitleBlockFields.Add(new DerivedField("COPYRIGHT", p.Token("YEAR"), "Copyright year"));
    }

    public static StandardsProfile CreateDefault()
    {
        var p = new StandardsProfile { Version = CurrentVersion };
        p.Fields.Add(new FieldDefinition("PROJECT_NAME", "Project name", required: true));
        p.Fields.Add(new FieldDefinition("PROJECT_NUMBER", "Project #", required: true));
        p.Fields.Add(new FieldDefinition("ADDRESS", "Address"));
        p.Fields.Add(new FieldDefinition("PROJECT_TYPE", "Project type",
            options: "Single-Family Residential; Multi-Family Residential; Commercial; Office; Retail; Restaurant; " +
                     "Industrial / Warehouse; Educational; Healthcare; Hospitality; Mixed-Use; Tenant Improvement; Addition / Renovation"));
        p.Fields.Add(new FieldDefinition("DATE", "Date") { DefaultValue = "{today}", Format = "MM/dd/yyyy" });
        p.Fields.Add(new FieldDefinition("OCCUPANCY_TYPE", "Occupancy type",
            options: "A-1; A-2; A-3; A-4; A-5; B; E; F-1; F-2; H-1; H-2; H-3; H-4; H-5; I-1; I-2; I-3; I-4; M; R-1; R-2; R-3; R-4; S-1; S-2; U"));
        p.Fields.Add(new FieldDefinition("AREA", "Area (e.g. 2,450 SF)"));
        p.Fields.Single(f => f.Key == "ADDRESS").Label = "Address (street, city, state ZIP)";
        AddDefaultTitleBlock(p);

        var r = p.Root;
        r.Add("01 Admin").Add("Contracts").Parent!.Add("Correspondence").Parent!.Add("Invoices");
        r.Add("02 Design").Add("Sketches").Parent!.Add("Renderings");
        var cad = r.Add("03 CAD");
        cad.Add("Sheets");
        cad.Add("Xrefs");
        cad.Add("Details");
        r.Add("04 Consultants");
        r.Add("05 Permit").Add("Submittals").Parent!.Add("Comments");
        r.Add("06 Construction");
        r.Add("07 Photos");
        return p;
    }
}

public sealed class FieldDefinition : Observable
{
    private string _key = "";
    private string _label = "";
    private string _options = "";
    private bool _required;
    private string _defaultValue = "";
    private string _format = "";

    public FieldDefinition() { }

    public FieldDefinition(string key, string label, string options = "", bool required = false)
    {
        _key = key;
        _label = label;
        _options = options;
        _required = required;
    }

    /// <summary>Placeholder name, e.g. PROJECT_NAME → [[PROJECT_NAME]]. Also matched against attribute tags.</summary>
    public string Key { get => _key; set => Set(ref _key, (value ?? "").Trim().ToUpperInvariant().Replace(' ', '_')); }
    public string Label { get => _label; set => Set(ref _label, value ?? ""); }

    /// <summary>Semicolon-separated choices offered in a drop-down (the user can still type anything).</summary>
    public string Options { get => _options; set => Set(ref _options, value ?? ""); }
    public bool Required { get => _required; set => Set(ref _required, value); }

    /// <summary>
    /// When set, the answer must be a date and is written in this format (e.g. MM/dd/yyyy).
    /// </summary>
    public string Format { get => _format; set => Set(ref _format, value ?? ""); }

    /// <summary>Pre-filled value. "{today}" becomes today's date.</summary>
    public string DefaultValue { get => _defaultValue; set => Set(ref _defaultValue, value ?? ""); }

    public IReadOnlyList<string> OptionList =>
        Options.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>A value built from the answers, e.g. LINE2 = [[PROJECT_NAME_1]].</summary>
public sealed class DerivedField : Observable
{
    private string _key = "";
    private string _value = "";
    private string _description = "";

    public DerivedField() { }

    public DerivedField(string key, string value, string description = "")
    {
        _key = key;
        _value = value;
        _description = description;
    }

    /// <summary>Attribute tag / placeholder name this value is written to.</summary>
    public string Key { get => _key; set => Set(ref _key, (value ?? "").Trim().ToUpperInvariant().Replace(' ', '_')); }

    /// <summary>What to write; can mix placeholders and text, e.g. "JOB NO. [[PROJECT_NUMBER]]".</summary>
    public string Value { get => _value; set => Set(ref _value, value ?? ""); }

    public string Description { get => _description; set => Set(ref _description, value ?? ""); }
}

public sealed class FolderNode : Observable
{
    private string _name = "";

    public string Name { get => _name; set => Set(ref _name, value ?? ""); }
    public ObservableCollection<FolderNode> Children { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public FolderNode? Parent { get; set; }

    public FolderNode Add(string name)
    {
        var child = new FolderNode { Name = name, Parent = this };
        Children.Add(child);
        return child;
    }

    /// <summary>Path relative to the project folder using '/', e.g. "03 CAD/Sheets". Empty for the project folder itself.</summary>
    public string RelativePath
    {
        get
        {
            var parts = new List<string>();
            for (var n = this; n.Parent is not null; n = n.Parent) parts.Add(n.Name);
            parts.Reverse();
            return string.Join('/', parts);
        }
    }

    public IEnumerable<FolderNode> SelfAndDescendants()
    {
        yield return this;
        foreach (var c in Children)
            foreach (var d in c.SelfAndDescendants())
                yield return d;
    }

    /// <summary>Restores <see cref="Parent"/> links after loading from JSON.</summary>
    public void LinkParents()
    {
        foreach (var c in Children)
        {
            c.Parent = this;
            c.LinkParents();
        }
    }
}

public sealed class TemplateFile : Observable
{
    private string _sourcePath = "";
    private string _folder = "";
    private string _fileName = "";
    private bool _fillText = true;
    private string _onlyForProjectType = "";

    /// <summary>The standard file to copy (title block, cover sheet, spreadsheet ...).</summary>
    public string SourcePath { get => _sourcePath; set => Set(ref _sourcePath, value ?? ""); }

    /// <summary>Destination folder, as a path in the folder tree ("03 CAD/Sheets"; empty = project folder).</summary>
    public string Folder { get => _folder; set => Set(ref _folder, value ?? ""); }

    /// <summary>Save-as name pattern, e.g. "[[PROJECT_NUMBER]] A-000 Cover.dwg". Empty = keep the source name.</summary>
    public string FileName { get => _fileName; set => Set(ref _fileName, value ?? ""); }

    /// <summary>Replace placeholders inside the file (DWG files only, needs AutoCAD).</summary>
    public bool FillText { get => _fillText; set => Set(ref _fillText, value); }

    /// <summary>Only copy this file for this project type. Empty = always.</summary>
    public string OnlyForProjectType { get => _onlyForProjectType; set => Set(ref _onlyForProjectType, value ?? ""); }
}
