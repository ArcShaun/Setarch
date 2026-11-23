using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using SetArch.Core.Models;
using SetArch.Core.Output;
using SetArch.Core.Planning;

namespace SetArch.App.ViewModels;

public sealed record Option(string Value, string Name)
{
    public override string ToString() => Name;
}

/// <summary>One question on the New Project page.</summary>
public sealed class FieldEntry : INotifyPropertyChanged
{
    private string _value;

    public FieldEntry(FieldDefinition definition, string value, string placeholder)
    {
        Definition = definition;
        _value = value;
        Placeholder = placeholder;
    }

    public FieldDefinition Definition { get; }
    public string Label => Definition.Required ? Definition.Label + " *" : Definition.Label;
    public IReadOnlyList<string> Options => Definition.OptionList;
    public bool HasOptions => Options.Count > 0;
    public string Placeholder { get; }

    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    private static readonly string LastFolderFile = Path.Combine(
        Path.GetDirectoryName(StandardsStore.DefaultPath)!, "last-folder.txt");

    private StandardsProfile _profile;
    private string _parentFolder = "";
    private ProjectPlan? _plan;
    private string _log = "";
    private bool _busy;
    private bool _fillDrawings = true;
    private bool _writeLtLisp;
    private string? _detectedConsole;
    private string _lastProjectFolder = "";

    public MainViewModel()
    {
        _profile = StandardsStore.LoadOrDefault(StandardsStore.DefaultPath);
        try { if (File.Exists(LastFolderFile)) _parentFolder = File.ReadAllText(LastFolderFile).Trim(); }
        catch (IOException) { }
        _detectedConsole = AutoCadConsole.Detect();
        _writeLtLisp = ConsolePath is null;
        RebuildFields();
        RefreshFolderPaths();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ---------------------------------------------------------------- standards

    public StandardsProfile Profile
    {
        get => _profile;
        private set
        {
            _profile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RootNodes));
            RebuildFields();
            RefreshFolderPaths();
            OnPropertyChanged(nameof(ConsoleStatus));
        }
    }

    /// <summary>The tree view needs a collection, so the project folder is wrapped in a one-item list.</summary>
    public IReadOnlyList<FolderNode> RootNodes => new[] { Profile.Root };

    private IReadOnlyList<Option> _folderPaths = Array.Empty<Option>();
    public IReadOnlyList<Option> FolderPaths { get => _folderPaths; private set { _folderPaths = value; OnPropertyChanged(); } }

    public IReadOnlyList<string> ProjectTypes =>
        new[] { "" }.Concat(Profile.Fields.FirstOrDefault(f => f.Key == "PROJECT_TYPE")?.OptionList ?? Array.Empty<string>()).ToList();

    public void RefreshFolderPaths()
    {
        Profile.Root.LinkParents();
        FolderPaths = Profile.Root.SelfAndDescendants()
            .Select(n => new Option(n.RelativePath, n.RelativePath.Length == 0 ? "(project folder)" : n.RelativePath))
            .ToList();
        OnPropertyChanged(nameof(ProjectTypes));
        RefreshPlan();
    }

    public void SaveStandards()
    {
        StandardsStore.Save(Profile, StandardsStore.DefaultPath);
        RebuildFields();
        RefreshFolderPaths();
    }

    public void ExportStandards(string path) => StandardsStore.Save(Profile, path);

    public void ImportStandards(string path)
    {
        Profile = StandardsStore.Load(path);
        StandardsStore.Save(Profile, StandardsStore.DefaultPath);
    }

    public void ResetStandards() => Profile = StandardsProfile.CreateDefault();

    public void ImportFolderTree(string folder, bool includeFiles)
    {
        FolderImporter.Import(Profile, folder, includeFiles);
        OnPropertyChanged(nameof(RootNodes));
        RefreshFolderPaths();
    }

    public void AddTemplateFiles(IEnumerable<string> paths, string folder)
    {
        foreach (var path in paths)
        {
            Profile.Files.Add(new TemplateFile
            {
                SourcePath = path,
                Folder = folder,
                FileName = FolderImporter.DefaultSaveAs(path),
                FillText = path.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".dwt", StringComparison.OrdinalIgnoreCase),
            });
        }
        RefreshPlan();
    }

    // ---------------------------------------------------------------- AutoCAD

    /// <summary>The console to use: the one set in the standards, else the detected one.</summary>
    public string? ConsolePath =>
        !string.IsNullOrWhiteSpace(Profile.AutoCadConsolePath) && File.Exists(Profile.AutoCadConsolePath)
            ? Profile.AutoCadConsolePath
            : _detectedConsole;

    public string ConsoleStatus => ConsolePath is { } p
        ? $"Using AutoCAD at {Path.GetDirectoryName(p)}"
        : "AutoCAD (full version) wasn't found on this computer, so drawings can't be filled in automatically.";

    public bool CanFillAutomatically => ConsolePath is not null;

    public string AutoCadConsolePath
    {
        get => Profile.AutoCadConsolePath;
        set
        {
            Profile.AutoCadConsolePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConsoleStatus));
            OnPropertyChanged(nameof(CanFillAutomatically));
        }
    }

    public bool FillDrawings { get => _fillDrawings; set { _fillDrawings = value; OnPropertyChanged(); } }
    public bool WriteLtLisp { get => _writeLtLisp; set { _writeLtLisp = value; OnPropertyChanged(); } }

    // ---------------------------------------------------------------- new project

    public ObservableCollection<FieldEntry> Fields { get; } = new();

    public string ParentFolder
    {
        get => _parentFolder;
        set { _parentFolder = value; OnPropertyChanged(); RefreshPlan(); }
    }

    public ProjectPlan? Plan { get => _plan; private set { _plan = value; OnPropertyChanged(); OnPropertyChanged(nameof(PreviewRoots)); OnPropertyChanged(nameof(TitleBlockPreview)); OnPropertyChanged(nameof(Messages)); OnPropertyChanged(nameof(CanCreate)); OnPropertyChanged(nameof(Summary)); } }

    /// <summary>The title block lines as they will be written, for the preview on the New project page.</summary>
    public IReadOnlyList<Option> TitleBlockPreview =>
        Plan is null ? Array.Empty<Option>() :
        Profile.TitleBlockFields
            .Where(d => !string.IsNullOrWhiteSpace(d.Key))
            .Select(d => new Option(string.IsNullOrWhiteSpace(d.Description) ? d.Key : d.Description, Plan.Values.GetValueOrDefault(d.Key, "")))
            .Concat(Profile.Fields.Where(f => !string.IsNullOrEmpty(f.Format))
                .Select(f => new Option(f.Label, Plan.Values.GetValueOrDefault(f.Key, ""))))
            .ToList();

    public IReadOnlyList<PreviewNode> PreviewRoots => Plan?.Preview is { } p ? new[] { p } : Array.Empty<PreviewNode>();

    public IReadOnlyList<string> Messages =>
        Plan is null ? Array.Empty<string>() : Plan.Errors.Select(e => "✖  " + e).Concat(Plan.Warnings.Select(w => "⚠  " + w)).ToList();

    public string Summary => Plan is null ? "" :
        $"{Plan.ProjectFolder}\n{Plan.Folders.Count} folders, {Plan.Files.Count} files ({Plan.Files.Count(f => f.FillText)} drawings to fill in)";

    public bool CanCreate => Plan is { IsValid: true } && !Busy;

    public string Log { get => _log; private set { _log = value; OnPropertyChanged(); } }

    public bool Busy { get => _busy; private set { _busy = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanCreate)); } }

    public string LastProjectFolder { get => _lastProjectFolder; private set { _lastProjectFolder = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasLastProject)); } }
    public bool HasLastProject => LastProjectFolder.Length > 0 && Directory.Exists(LastProjectFolder);

    public void AppendLog(string line) => Log = Log.Length == 0 ? line : Log + Environment.NewLine + line;

    public void RebuildFields()
    {
        var old = Fields.ToDictionary(f => f.Definition.Key, f => f.Value);
        foreach (var f in Fields) f.PropertyChanged -= OnFieldChanged;
        Fields.Clear();
        foreach (var def in Profile.Fields.Where(d => !string.IsNullOrWhiteSpace(d.Key)))
        {
            var entry = new FieldEntry(def, old.TryGetValue(def.Key, out var v) ? v : ProjectPlanner.DefaultValue(def), Profile.Token(def.Key));
            entry.PropertyChanged += OnFieldChanged;
            Fields.Add(entry);
        }
        OnPropertyChanged(nameof(ProjectTypes));
        RefreshPlan();
    }

    private void OnFieldChanged(object? sender, PropertyChangedEventArgs e) => RefreshPlan();

    public Dictionary<string, string> Values() =>
        Fields.GroupBy(f => f.Definition.Key).ToDictionary(g => g.Key, g => g.First().Value ?? "");

    public void RefreshPlan()
    {
        if (Fields.Count == 0 && Profile.Fields.Count > 0) return; // still constructing
        Plan = ProjectPlanner.Plan(Profile, Values(), ParentFolder);
    }

    public async Task CreateAsync()
    {
        RefreshPlan();
        if (Plan is not { IsValid: true } plan) return;

        Busy = true;
        Log = "";
        try
        {
            try { Directory.CreateDirectory(Path.GetDirectoryName(LastFolderFile)!); File.WriteAllText(LastFolderFile, ParentFolder); }
            catch (IOException) { }

            var result = await Task.Run(() => ProjectCreator.Create(plan, line => System.Windows.Application.Current.Dispatcher.Invoke(() => AppendLog(line))));
            LastProjectFolder = plan.ProjectFolder;

            if (result.DrawingsToFill.Count > 0)
            {
                if (FillDrawings && ConsolePath is { } console)
                {
                    AppendLog($"Filling in {result.DrawingsToFill.Count} drawing(s) with AutoCAD ...");
                    var filler = new AutoCadConsole(console);
                    var results = await filler.FillAsync(result.DrawingsToFill, plan.Values, plan.TokenPrefix, plan.TokenSuffix,
                        line => System.Windows.Application.Current.Dispatcher.Invoke(() => AppendLog(line)));
                    var failed = results.Count(r => !r.Success);
                    if (failed > 0) AppendLog($"{failed} drawing(s) could not be filled in; open them and check.");
                }

                if (WriteLtLisp)
                {
                    var lsp = Path.Combine(plan.ProjectFolder, "SetArch Fill.lsp");
                    File.WriteAllText(lsp, FillScript.LtLisp(plan.Values, plan.TokenPrefix, plan.TokenSuffix), System.Text.Encoding.ASCII);
                    AppendLog($"Saved {Path.GetFileName(lsp)} in the project folder. In each drawing: APPLOAD it, type {FillScript.LtCommand}, then save.");
                }
            }

            AppendLog($"Done. {result.FoldersCreated} folders and {result.FilesCopied} files created" +
                      (result.Skipped.Count > 0 ? $", {result.Skipped.Count} existing file(s) left untouched." : "."));
        }
        catch (Exception ex)
        {
            AppendLog("Failed: " + ex.Message);
            throw;
        }
        finally
        {
            Busy = false;
            RefreshPlan();
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
