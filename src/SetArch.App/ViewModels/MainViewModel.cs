using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using SetArch.Core.Models;
using SetArch.Core.Planning;
using SetArch.Core.Standards;

namespace SetArch.App.ViewModels;

public sealed record Option<T>(T Value, string Name)
{
    public override string ToString() => Name;
}

public sealed class MainViewModel : INotifyPropertyChanged
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SetArch", "last-answers.json");

    public const int ProjectStep = 0, PaperStep = 1, TitleBlockStep = 2, SheetsStep = 3, SheetListStep = 4, StandardsStep = 5, CreateStep = 6;

    public IReadOnlyList<string> StepNames { get; } = new[]
    {
        "1. Project",
        "2. Paper & plotting",
        "3. Title block",
        "4. Which sheets?",
        "5. Sheet list",
        "6. Drawing standards",
        "7. Review & create",
    };

    private ProjectAnswers _answers;
    private int _step;
    private bool _sheetListEdited;
    private string _log = "";
    private bool _busy;
    private SheetPlan? _previewPlan;
    private SheetLayout? _previewSheet;
    private string _reviewSummary = "";
    private IReadOnlyList<string> _validationErrors = Array.Empty<string>();

    public MainViewModel()
    {
        _answers = LoadLast() ?? new ProjectAnswers();
        // The sheet list is rebuilt from the "which sheets" answers unless the user edits it.
        _answers.SheetList.Clear();
        SheetRows = new ObservableCollection<SheetDefinition>();
        SheetRows.CollectionChanged += (_, _) => { if (!_regenerating) _sheetListEdited = true; };
        RefreshScaleOptions();
        RegenerateSheetList();

        NextCommand = new RelayCommand(() => Step++, () => Step < StepNames.Count - 1 && !Busy);
        BackCommand = new RelayCommand(() => Step--, () => Step > 0 && !Busy);
        RegenerateCommand = new RelayCommand(RegenerateSheetList);
        AddSheetCommand = new RelayCommand(AddSheet);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ProjectAnswers Answers
    {
        get => _answers;
        private set
        {
            _answers = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedPaper));
            OnPropertyChanged(nameof(UseCustomTitleBlock));
            OnPropertyChanged(nameof(UseGeneratedTitleBlock));
        }
    }

    public ObservableCollection<SheetDefinition> SheetRows { get; }

    public RelayCommand NextCommand { get; }
    public RelayCommand BackCommand { get; }
    public ICommand RegenerateCommand { get; }
    public ICommand AddSheetCommand { get; }

    public int Step
    {
        get => _step;
        set
        {
            if (value == _step || value < 0 || value >= StepNames.Count) return;
            _step = value;
            if (_step == SheetListStep && !_sheetListEdited) RegenerateSheetList();
            if (_step == CreateStep) RefreshReview();
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsLastStep));
            NextCommand.RaiseCanExecuteChanged();
            BackCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsLastStep => Step == StepNames.Count - 1;

    // ---------------------------------------------------------------- choices

    public IReadOnlyList<PaperSize> PaperSizes => Core.Standards.PaperSizes.All;
    public IReadOnlyList<Option<Orientation>> Orientations { get; } = new[] { new Option<Orientation>(Orientation.Landscape, "Landscape"), new Option<Orientation>(Orientation.Portrait, "Portrait") };
    public IReadOnlyList<Option<TitleBlockPlacement>> Placements { get; } = new[] { new Option<TitleBlockPlacement>(TitleBlockPlacement.RightStrip, "Vertical strip on the right"), new Option<TitleBlockPlacement>(TitleBlockPlacement.BottomStrip, "Horizontal strip along the bottom") };
    public IReadOnlyList<Option<NumberingScheme>> NumberingSchemes { get; } = new[] { new Option<NumberingScheme>(NumberingScheme.Ncs, "By drawing type (G-001, A-101, A-201 ...)"), new Option<NumberingScheme>(NumberingScheme.Sequential, "Sequential (A00, A01, A02 ...)") };
    public IReadOnlyList<Option<LayerStandard>> LayerStandardsOptions { get; } = new[] { new Option<LayerStandard>(LayerStandard.Ncs, "Architectural layer set (AIA / NCS style: A-WALL, A-DOOR ...)"), new Option<LayerStandard>(LayerStandard.None, "Only the layers the sheets need") };
    public IReadOnlyList<Option<LayoutNaming>> LayoutNamings { get; } = new[] { new Option<LayoutNaming>(LayoutNaming.NumberAndTitle, "Number and title (A-101 Ground Floor Plan)"), new Option<LayoutNaming>(LayoutNaming.NumberOnly, "Number only (A-101)") };
    public IReadOnlyList<SheetKind> SheetKinds { get; } = Enum.GetValues<SheetKind>();
    public IReadOnlyList<string> PlotDevices { get; } = new[] { "DWG To PDF.pc3", "AutoCAD PDF (High Quality Print).pc3", "AutoCAD PDF (General Documentation).pc3", "DWF6 ePlot.pc3" };
    public IReadOnlyList<string> PlotStyles { get; } = new[] { "monochrome.ctb", "acad.ctb", "Grayscale.ctb", "Screening 50%.ctb", "monochrome.stb", "acad.stb", "" };

    private IReadOnlyList<Option<string>> _scaleOptions = Array.Empty<Option<string>>();
    public IReadOnlyList<Option<string>> ScaleOptions { get => _scaleOptions; private set { _scaleOptions = value; OnPropertyChanged(); } }

    public PaperSize SelectedPaper
    {
        get => Core.Standards.PaperSizes.Get(Answers.Format.PaperSizeId);
        set
        {
            var unitsChanged = value.Units != SelectedPaper.Units;
            Answers.Format.PaperSizeId = value.Id;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UnitsLabel));
            if (unitsChanged)
            {
                RefreshScaleOptions();
                // Scales are unit-specific, so the list has to be rebuilt.
                _sheetListEdited = false;
                RegenerateSheetList();
            }
        }
    }

    public string UnitsLabel => SelectedPaper.Units == PaperUnits.Millimeters ? "millimetres" : "inches";

    public bool UseCustomTitleBlock
    {
        get => Answers.TitleBlock.Mode == TitleBlockMode.CustomBlock;
        set
        {
            Answers.TitleBlock.Mode = value ? TitleBlockMode.CustomBlock : TitleBlockMode.Generated;
            OnPropertyChanged();
            OnPropertyChanged(nameof(UseGeneratedTitleBlock));
        }
    }

    public bool UseGeneratedTitleBlock
    {
        get => !UseCustomTitleBlock;
        set => UseCustomTitleBlock = !value;
    }

    public string CustomBlockPath
    {
        get => Answers.TitleBlock.CustomBlockPath;
        set { Answers.TitleBlock.CustomBlockPath = value; OnPropertyChanged(); }
    }

    // ---------------------------------------------------------------- output

    private bool _createInAutoCad = true;
    public bool CreateInAutoCad { get => _createInAutoCad; set { _createInAutoCad = value; OnPropertyChanged(); OnPropertyChanged(nameof(SaveLisp)); } }
    public bool SaveLisp { get => !_createInAutoCad; set => CreateInAutoCad = !value; }

    private bool _newDrawing = true;
    public bool NewDrawing { get => _newDrawing; set { _newDrawing = value; OnPropertyChanged(); } }

    private string _saveDrawingPath = "";
    public string SaveDrawingPath { get => _saveDrawingPath; set { _saveDrawingPath = value; OnPropertyChanged(); } }

    private bool _lispPageSetup = true;
    public bool LispPageSetup { get => _lispPageSetup; set { _lispPageSetup = value; OnPropertyChanged(); } }

    public string Log { get => _log; private set { _log = value; OnPropertyChanged(); } }

    public bool Busy
    {
        get => _busy;
        set
        {
            _busy = value;
            OnPropertyChanged();
            NextCommand.RaiseCanExecuteChanged();
            BackCommand.RaiseCanExecuteChanged();
        }
    }

    public void AppendLog(string line) => Log = Log.Length == 0 ? line : Log + Environment.NewLine + line;
    public void ClearLog() => Log = "";

    // ---------------------------------------------------------------- review

    public SheetPlan? PreviewPlan { get => _previewPlan; private set { _previewPlan = value; OnPropertyChanged(); } }
    public SheetLayout? PreviewSheet { get => _previewSheet; set { _previewSheet = value; OnPropertyChanged(); } }
    public string ReviewSummary { get => _reviewSummary; private set { _reviewSummary = value; OnPropertyChanged(); } }
    public IReadOnlyList<string> ValidationErrors { get => _validationErrors; private set { _validationErrors = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsValid)); } }
    public bool IsValid => ValidationErrors.Count == 0;

    public void RefreshReview()
    {
        SyncSheetList();
        ValidationErrors = SheetPlanBuilder.Validate(Answers);
        if (!IsValid)
        {
            PreviewPlan = null;
            PreviewSheet = null;
            ReviewSummary = "";
            return;
        }

        var plan = SheetPlanBuilder.Build(Answers);
        PreviewPlan = plan;
        PreviewSheet = plan.Sheets.FirstOrDefault(s => s.Viewport is not null) ?? plan.Sheets.FirstOrDefault();
        ReviewSummary =
            $"{plan.Sheets.Count} sheets on {plan.Paper.Name}, {plan.Orientation.ToString().ToLowerInvariant()}\n" +
            $"Plotter: {plan.PlotDevice}   Plot style: {(string.IsNullOrEmpty(plan.PlotStyleTable) ? "none" : plan.PlotStyleTable)}\n" +
            $"{plan.Layers.Count} layers, {plan.TextStyles.Count} text styles, " +
            $"{(Answers.TitleBlock.Mode == TitleBlockMode.Generated ? "generated title block" : "your title block: " + Path.GetFileName(Answers.TitleBlock.CustomBlockPath))}";
    }

    /// <summary>Builds the plan for the current answers (call after <see cref="RefreshReview"/> reports it is valid).</summary>
    public SheetPlan BuildPlan()
    {
        SyncSheetList();
        return SheetPlanBuilder.Build(Answers);
    }

    // ---------------------------------------------------------------- sheet list

    private bool _regenerating;

    public void RegenerateSheetList()
    {
        _regenerating = true;
        try
        {
            SheetRows.Clear();
            foreach (var s in SheetListGenerator.Generate(Answers.Sheets, SelectedPaper.Units))
                SheetRows.Add(s);
            _sheetListEdited = false;
        }
        finally
        {
            _regenerating = false;
        }
    }

    /// <summary>Called by the grid when the user edits a cell, so later answer changes don't overwrite their edits.</summary>
    public void MarkSheetListEdited() => _sheetListEdited = true;

    private void AddSheet()
    {
        SheetRows.Add(new SheetDefinition
        {
            Number = "A-" + (SheetRows.Count + 1).ToString("000"),
            Title = "New Sheet",
            Kind = SheetKind.Other,
            ScaleId = Scales.DefaultFor(SheetKind.Other, SelectedPaper.Units),
        });
    }

    private void SyncSheetList()
    {
        Answers.SheetList = SheetRows.ToList();
    }

    private void RefreshScaleOptions()
    {
        var list = new List<Option<string>> { new(Scales.None, "(no viewport)") };
        list.AddRange(Scales.For(SelectedPaper.Units).Select(s => new Option<string>(s.Id, s.Name)));
        ScaleOptions = list;
    }

    // ---------------------------------------------------------------- persistence

    public void SaveAnswers(string path)
    {
        SyncSheetList();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(Answers, Json));
    }

    public void LoadAnswers(string path)
    {
        var loaded = JsonSerializer.Deserialize<ProjectAnswers>(File.ReadAllText(path), Json)
                     ?? throw new InvalidDataException("The file does not contain SetArch answers.");
        _ = Core.Standards.PaperSizes.Get(loaded.Format.PaperSizeId); // validate
        Answers = loaded;
        RefreshScaleOptions();

        _regenerating = true;
        try
        {
            SheetRows.Clear();
            var rows = loaded.SheetList.Count > 0 ? loaded.SheetList : SheetListGenerator.Generate(loaded.Sheets, SelectedPaper.Units);
            foreach (var s in rows) SheetRows.Add(s);
            _sheetListEdited = loaded.SheetList.Count > 0;
        }
        finally
        {
            _regenerating = false;
        }
        Step = ProjectStep;
    }

    /// <summary>Remembers the answers (firm name, paper, standards ...) for next time. Project-specific list is kept too.</summary>
    public void SaveLast()
    {
        try { SaveAnswers(SettingsPath); }
        catch (Exception) { /* not important enough to bother the user */ }
    }

    private static ProjectAnswers? LoadLast()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return null;
            var a = JsonSerializer.Deserialize<ProjectAnswers>(File.ReadAllText(SettingsPath), Json);
            if (a is null) return null;
            _ = Core.Standards.PaperSizes.Get(a.Format.PaperSizeId);
            a.Project.Date = DateTime.Today.ToString("yyyy-MM-dd");
            return a;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
