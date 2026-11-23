using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using SetArch.Core.Calculators;

namespace SetArch.App.ViewModels;

public sealed class PlumbingRowViewModel : INotifyPropertyChanged
{
    private string _function = "";
    private string _area = "";

    public string Function { get => _function; set { _function = value ?? ""; Changed(); Changed(nameof(BasisHint)); } }

    /// <summary>Area in sq ft, or a count for unit-based occupancies (typed text, e.g. "2,450").</summary>
    public string Area { get => _area; set { _area = value ?? ""; Changed(); } }

    public string BasisHint
    {
        get
        {
            var fn = PlumbingData.Default.Function(Function);
            var occ = fn is null ? null : PlumbingData.Default.Occupancy(fn.Occupancy);
            if (occ is null) return "";
            if (occ.IsUnits) return "units";
            return fn!.Factor == 0 ? "occupants / seats" : $"sq ft ({fn.Factor:0} {fn.Basis.ToLowerInvariant()}/person)";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>Inputs and live results for the plumbing fixture and egress distance calculators.</summary>
public sealed class CodeAnalysisViewModel : INotifyPropertyChanged
{
    private string _malePercent = "50";
    private string _floors = "1";
    private bool _fountainMinimumOne = true;
    private string _fbc = "";
    private string _ffpc = "";
    private bool _sprinklered = true;
    private bool _existingBuilding;
    private bool _plumbingTouched, _fbcTouched, _ffpcTouched;
    private string _lastGroup = "";
    private bool _suggesting;

    public CodeAnalysisViewModel()
    {
        AddRow();
        Rows.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (PlumbingRowViewModel r in e.NewItems) r.PropertyChanged += OnRowChanged;
            Recalculate();
        };
        foreach (var r in Rows) r.PropertyChanged += OnRowChanged;
        Recalculate();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised whenever a result changes, so the project preview can refresh.</summary>
    public event EventHandler? ResultsChanged;

    public IReadOnlyList<string> Functions { get; } = PlumbingData.Default.Functions.Select(f => f.Name).ToList();
    public IReadOnlyList<string> FbcOccupancies { get; } = EgressData.Default.Fbc.Select(c => c.Name).ToList();
    public IReadOnlyList<string> FfpcOccupancies { get; } = EgressData.Default.Ffpc.Select(c => c.Name).ToList();

    public ObservableCollection<PlumbingRowViewModel> Rows { get; } = new();

    public string MalePercent { get => _malePercent; set { _malePercent = value; Changed(); Recalculate(); } }
    public string Floors { get => _floors; set { _floors = value; Changed(); Recalculate(); } }
    public bool FountainMinimumOne { get => _fountainMinimumOne; set { _fountainMinimumOne = value; Changed(); Recalculate(); } }

    public string FbcOccupancy { get => _fbc; set { _fbc = value ?? ""; if (!_suggesting) _fbcTouched = true; Changed(); Recalculate(); } }
    public string FfpcOccupancy { get => _ffpc; set { _ffpc = value ?? ""; if (!_suggesting) _ffpcTouched = true; Changed(); Recalculate(); } }
    public bool Sprinklered { get => _sprinklered; set { _sprinklered = value; Changed(); Changed(nameof(Unsprinklered)); Recalculate(); } }
    public bool Unsprinklered { get => !_sprinklered; set => Sprinklered = !value; }

    public bool ExistingBuilding
    {
        get => _existingBuilding;
        set
        {
            _existingBuilding = value;
            Changed();
            Changed(nameof(NewBuilding));
            if (!_ffpcTouched && CodeDefaults.FfpcEgress(_lastGroup, value) is { } ffpc) Suggest(() => FfpcOccupancy = ffpc);
        }
    }

    public bool NewBuilding { get => !_existingBuilding; set => ExistingBuilding = !value; }

    public PlumbingResult? Plumbing { get; private set; }
    public EgressResult? Egress { get; private set; }
    public PlumbingInput PlumbingInput { get; private set; } = new();
    public EgressInput EgressInput { get; private set; } = new();

    public string PlumbingSummary { get; private set; } = "";
    public string EgressSummary { get; private set; } = "";
    public string Problems { get; private set; } = "";

    public void AddRow()
    {
        if (Rows.Count < PlumbingCalculator.MaxRows) Rows.Add(new PlumbingRowViewModel());
    }

    public void RemoveRow(PlumbingRowViewModel row)
    {
        Rows.Remove(row);
        if (Rows.Count == 0) AddRow();
    }

    /// <summary>
    /// Fills the inputs from the project answers (occupancy group and area) until the user changes them by hand.
    /// </summary>
    public void SuggestFrom(string occupancyGroup, string area)
    {
        var group = CodeDefaults.Group(occupancyGroup);
        _lastGroup = group;
        Suggest(() =>
        {
            if (!_plumbingTouched && Rows.Count > 0)
            {
                var first = Rows[0];
                if (CodeDefaults.FunctionOfSpace(group) is { } fn) first.Function = fn;
                var fnData = PlumbingData.Default.Function(first.Function);
                var isUnits = fnData is not null && PlumbingData.Default.Occupancy(fnData.Occupancy)?.IsUnits == true;
                first.Area = isUnits ? "1" : (ParseNumber(area)?.ToString("#,##0.##", CultureInfo.InvariantCulture) ?? first.Area);
            }
            if (!_fbcTouched && CodeDefaults.FbcEgress(group) is { } fbc) FbcOccupancy = fbc;
            if (!_ffpcTouched && CodeDefaults.FfpcEgress(group, ExistingBuilding) is { } ffpc) FfpcOccupancy = ffpc;
        });
    }

    /// <summary>All calculator values for placeholders, e.g. WC_MALE_REQ, CP_FBC.</summary>
    public Dictionary<string, string> Placeholders()
    {
        var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Plumbing is not null)
            foreach (var kv in PlumbingCalculator.Placeholders(PlumbingInput, Plumbing)) v[kv.Key] = kv.Value;
        if (Egress is not null)
            foreach (var kv in EgressCalculator.Placeholders(EgressInput, Egress)) v[kv.Key] = kv.Value;
        return v;
    }

    private void Suggest(Action action)
    {
        _suggesting = true;
        try { action(); }
        finally { _suggesting = false; }
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlumbingRowViewModel.BasisHint)) return;
        if (!_suggesting) _plumbingTouched = true;
        Recalculate();
    }

    public static double? ParseNumber(string? text)
    {
        var m = Regex.Match(text ?? "", @"\d[\d,]*(\.\d+)?");
        return m.Success && double.TryParse(m.Value.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private void Recalculate()
    {
        var problems = new List<string>();
        var male = ParseNumber(MalePercent) ?? 50;
        PlumbingInput = new PlumbingInput
        {
            MaleRatio = Math.Clamp(male, 0, 100) / 100.0,
            Floors = (int)Math.Max(1, ParseNumber(Floors) ?? 1),
            FountainMinimumOnePerOccupancy = FountainMinimumOne,
            Rows = Rows.Where(r => !string.IsNullOrWhiteSpace(r.Function))
                       .Select(r => new PlumbingRow { Function = r.Function, AreaOrUnits = ParseNumber(r.Area) ?? 0 })
                       .ToList(),
        };
        Plumbing = PlumbingCalculator.Calculate(PlumbingInput);
        problems.AddRange(Plumbing.Rows.Where(r => r.Problem.Length > 0).Select(r => $"Row {r.Number}: {r.Problem}"));

        var q = Plumbing.Required;
        PlumbingSummary = Plumbing.Rows.Count == 0 ? "Choose a function of space." :
            $"Occupants: {Plumbing.TotalOccupants:#,##0.##}\n" +
            $"Water closets: {q.WcMale:0} M / {q.WcFemale:0} F  (total {q.WcTotal:0})\n" +
            $"Lavatories: {q.LavMale:0} M / {q.LavFemale:0} F  (total {q.LavTotal:0})\n" +
            $"Bathtubs/showers: {q.Bath:0}    Drinking fountains: {q.Fountain:0}    Service sinks: {q.ServiceSink:0}";

        EgressInput = new EgressInput { FbcOccupancy = FbcOccupancy, FfpcOccupancy = FfpcOccupancy, Sprinklered = Sprinklered };
        Egress = EgressCalculator.Calculate(EgressInput);
        EgressSummary = string.Join("\n", Egress.Lines.Select(l =>
                            $"{l.Measurement}: FBC {l.FbcLimit} ({l.FbcSection})   FFPC {l.FfpcLimit} ({l.FfpcSection})"))
                        + (Egress.Footnotes.Count > 0 ? "\nFootnotes: " + string.Join(", ", Egress.Footnotes.Select(f => f.Id)) : "");

        Problems = string.Join("\n", problems);
        Changed(nameof(PlumbingSummary));
        Changed(nameof(EgressSummary));
        Changed(nameof(Problems));
        ResultsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Changed([CallerMemberName] string? n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
