using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace SetArch.Core.Calculators;

/// <summary>One row of FBC Plumbing Table 403.1 (columns A:Y of 'Code Data' in Plumbing_Calculator.xlsx).</summary>
public sealed class FixtureOccupancy
{
    public string Key { get; set; } = "";
    public string Classification { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>"Occupants" or "Units".</summary>
    public string Basis { get; set; } = "";
    /// <summary>[1 per, tier limit, then 1 per] — e.g. 1 per 25 for the first 50, then 1 per 50.</summary>
    public double[] WcMale { get; set; } = new double[3];
    public double[] WcFemale { get; set; } = new double[3];
    public double[] LavMale { get; set; } = new double[3];
    public double[] LavFemale { get; set; } = new double[3];
    public double BathPer { get; set; }
    public double FountainPer { get; set; }
    /// <summary>"None", "Building" or "Floor".</summary>
    public string ServiceSink { get; set; } = "None";
    public bool SexSeparated { get; set; }
    /// <summary>Footnote e: no service sink needed for 15 or fewer occupants.</summary>
    public bool FountainExceptionE { get; set; }
    public bool IsUnits => Basis == "Units";
}

/// <summary>A function of space from FBC Building Table 1004.5 and the 403.1 occupancy it maps to.</summary>
public sealed class SpaceFunction
{
    public string Name { get; set; } = "";
    /// <summary>Occupant load factor, sq ft per person (0 = use a head/seat count).</summary>
    public double Factor { get; set; }
    public string Basis { get; set; } = "";
    public string Occupancy { get; set; } = "";
}

public sealed class PlumbingData
{
    public string Source { get; set; } = "";
    public List<FixtureOccupancy> Occupancies { get; set; } = new();
    public List<SpaceFunction> Functions { get; set; } = new();

    private static readonly Lazy<PlumbingData> Embedded = new(() => Load("plumbing.json"));
    public static PlumbingData Default => Embedded.Value;

    private static PlumbingData Load(string name)
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("SetArch.Core.Calculators.Data." + name)
                      ?? throw new InvalidOperationException($"Missing calculator data {name}.");
        return JsonSerializer.Deserialize<PlumbingData>(s) ?? throw new InvalidDataException(name);
    }

    public SpaceFunction? Function(string name) =>
        Functions.FirstOrDefault(f => string.Equals(f.Name, name?.Trim(), StringComparison.OrdinalIgnoreCase));

    public FixtureOccupancy? Occupancy(string key) =>
        Occupancies.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase));
}

/// <summary>One line of the calculator: a function of space and its area (or unit/seat count).</summary>
public sealed class PlumbingRow
{
    public string Function { get; set; } = "";
    /// <summary>Area in sq ft, or the number of units for unit-based occupancies.</summary>
    public double AreaOrUnits { get; set; }
    public double? FactorOverride { get; set; }
    public double? OccupantOverride { get; set; }
}

public sealed class PlumbingInput
{
    public List<PlumbingRow> Rows { get; set; } = new();
    /// <summary>Share of occupants that are male (cell B3, default 50%).</summary>
    public double MaleRatio { get; set; } = 0.5;
    /// <summary>Number of floors, for occupancies that need a service sink per floor (cell E3).</summary>
    public int Floors { get; set; } = 1;
    /// <summary>Drinking fountains: "Minimum 1 per occupancy" (true) or "Sum exact fractions" (false), cell I3.</summary>
    public bool FountainMinimumOnePerOccupancy { get; set; } = true;
}

/// <summary>Fixture counts: one per column J:R of the calculator.</summary>
public sealed record FixtureCounts(double WcMale, double WcFemale, double LavMale, double LavFemale, double Bath, double Fountain, double ServiceSink)
{
    public double WcTotal => WcMale + WcFemale;
    public double LavTotal => LavMale + LavFemale;
}

public sealed record PlumbingRowResult(
    int Number, PlumbingRow Input, SpaceFunction? Function, FixtureOccupancy? Occupancy,
    double Factor, double Occupants, FixtureCounts Counts, string Problem);

public sealed record PlumbingResult(IReadOnlyList<PlumbingRowResult> Rows, FixtureCounts Exact, FixtureCounts Required)
{
    public double TotalOccupants => Rows.Sum(r => r.Occupants);
}

/// <summary>
/// The plumbing fixture analysis from Plumbing_Calculator.xlsx ('Fixture Calculator' rows 6–15), done in code
/// so Excel isn't needed. Each formula mirrors the spreadsheet column it is named after.
/// </summary>
public static class PlumbingCalculator
{
    public const int MaxRows = 8;

    public static PlumbingResult Calculate(PlumbingInput input, PlumbingData? data = null)
    {
        data ??= PlumbingData.Default;
        var rows = new List<PlumbingRowResult>();
        var n = 0;
        foreach (var row in input.Rows.Where(r => !string.IsNullOrWhiteSpace(r.Function)).Take(MaxRows))
        {
            n++;
            var fn = data.Function(row.Function);
            var occ = fn is null ? null : data.Occupancy(fn.Occupancy);
            if (fn is null || occ is null)
            {
                rows.Add(new PlumbingRowResult(n, row, fn, occ, 0, 0, Zero, fn is null ? $"Unknown function of space \"{row.Function}\"." : "No Table 403.1 occupancy for this function."));
                continue;
            }

            // AO: factor used; I: occupants or units.
            var factor = row.FactorOverride ?? fn.Factor;
            var occupants = row.OccupantOverride
                            ?? (occ.IsUnits ? row.AreaOrUnits : factor == 0 ? row.AreaOrUnits : row.AreaOrUnits / factor);

            // AM / AN: male and female counts.
            double male, female;
            if (occ.SexSeparated)
            {
                male = occ.IsUnits ? occupants : occupants * input.MaleRatio;
                female = occ.IsUnits ? occupants : occupants * (1 - input.MaleRatio);
            }
            else
            {
                male = occupants;
                female = 0;
            }

            var wcMale = Tiered(occ.WcMale, male);                                      // J
            var wcFemale = occ.SexSeparated ? Tiered(occ.WcFemale, female) : 0;         // K
            var lavMale = Tiered(occ.LavMale, male);                                    // M
            var lavFemale = occ.SexSeparated ? Tiered(occ.LavFemale, female) : 0;       // N
            var bath = occ.BathPer == 0 ? 0 : occupants / occ.BathPer;                  // P
            var fountain = occ.FountainPer == 0 ? 0                                     // Q
                : input.FountainMinimumOnePerOccupancy ? Math.Max(1, occupants / occ.FountainPer) : occupants / occ.FountainPer;
            var sink = occ.ServiceSink switch                                           // R
            {
                "None" => 0,
                "Floor" => Math.Max(1, input.Floors),
                _ => occ.FountainExceptionE && occupants <= 15 ? 0 : 1,
            };

            rows.Add(new PlumbingRowResult(n, row, fn, occ, factor, occupants,
                new FixtureCounts(wcMale, wcFemale, lavMale, lavFemale, bath, fountain, sink), ""));
        }

        // Row 14: exact sums (service sinks: the largest, not the sum).
        var exact = new FixtureCounts(
            rows.Sum(r => r.Counts.WcMale), rows.Sum(r => r.Counts.WcFemale),
            rows.Sum(r => r.Counts.LavMale), rows.Sum(r => r.Counts.LavFemale),
            rows.Sum(r => r.Counts.Bath), rows.Sum(r => r.Counts.Fountain),
            rows.Count == 0 ? 0 : rows.Max(r => r.Counts.ServiceSink));

        // Row 15: rounded up, at least 1 when any is needed.
        var required = new FixtureCounts(
            Required(exact.WcMale), Required(exact.WcFemale), Required(exact.LavMale), Required(exact.LavFemale),
            Required(exact.Bath), Required(exact.Fountain), RoundUp(exact.ServiceSink));

        return new PlumbingResult(rows, exact, required);
    }

    private static readonly FixtureCounts Zero = new(0, 0, 0, 0, 0, 0, 0);

    /// <summary>IF(per=0,0,IF(AND(lim>0,count>lim), lim/per + (count-lim)/then, count/per)).</summary>
    private static double Tiered(double[] t, double count)
    {
        double per = t[0], limit = t[1], then = t[2];
        if (per == 0) return 0;
        return limit > 0 && count > limit ? limit / per + (count - limit) / then : count / per;
    }

    /// <summary>IF(x&lt;=0,0,MAX(1,ROUNDUP(x,0))).</summary>
    private static double Required(double x) => x <= 0 ? 0 : Math.Max(1, RoundUp(x));

    /// <summary>Excel ROUNDUP(x,0), ignoring floating-point noise (Excel works to 15 significant digits).</summary>
    private static double RoundUp(double x) => Math.Ceiling(Math.Round(x, 9));

    /// <summary>The placeholder values for drawings, e.g. [[WC_MALE_REQ]] = 2.</summary>
    public static Dictionary<string, string> Placeholders(PlumbingInput input, PlumbingResult r)
    {
        var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Counts(string suffix, FixtureCounts c, bool exact)
        {
            string F(double x) => exact ? Fmt(x, "0.00") : Fmt(x, "0");
            v[$"WC_MALE{suffix}"] = F(c.WcMale);
            v[$"WC_FEMALE{suffix}"] = F(c.WcFemale);
            v[$"WC_TOTAL{suffix}"] = F(c.WcTotal);
            v[$"LAV_MALE{suffix}"] = F(c.LavMale);
            v[$"LAV_FEMALE{suffix}"] = F(c.LavFemale);
            v[$"LAV_TOTAL{suffix}"] = F(c.LavTotal);
            v[$"BATH{suffix}"] = F(c.Bath);
            v[$"FOUNTAIN{suffix}"] = F(c.Fountain);
            v[$"SERVICE_SINK{suffix}"] = F(c.ServiceSink);
        }
        Counts("_REQ", r.Required, exact: false);
        Counts("_PROV", r.Required, exact: false); // provided defaults to required, as in the spreadsheet
        Counts("_CALC", r.Exact, exact: true);

        v["PLUMB_OCCUPANTS"] = Fmt(r.TotalOccupants, "#,##0.##");
        v["PLUMB_MALE_PCT"] = Fmt(input.MaleRatio * 100, "0.#") + "%";
        v["PLUMB_FEMALE_PCT"] = Fmt((1 - input.MaleRatio) * 100, "0.#") + "%";
        v["PLUMB_FLOORS"] = input.Floors.ToString(CultureInfo.InvariantCulture);

        for (var i = 1; i <= MaxRows; i++)
        {
            var row = r.Rows.FirstOrDefault(x => x.Number == i);
            var p = $"PL{i}_";
            v[p + "FUNCTION"] = row?.Input.Function ?? "";
            v[p + "OCCUPANCY"] = row?.Occupancy?.Description ?? "";
            v[p + "CLASSIFICATION"] = row?.Occupancy?.Classification ?? "";
            v[p + "BASIS"] = row?.Occupancy?.Basis ?? "";
            v[p + "AREA"] = row is null ? "" : Fmt(row.Input.AreaOrUnits, "#,##0.##");
            v[p + "FACTOR"] = row is null ? "" : Fmt(row.Factor, "0.##");
            v[p + "OCCUPANTS"] = row is null ? "" : Fmt(row.Occupants, "#,##0.##");
            v[p + "WC_MALE"] = row is null ? "" : Fmt(row.Counts.WcMale, "0.00");
            v[p + "WC_FEMALE"] = row is null ? "" : Fmt(row.Counts.WcFemale, "0.00");
            v[p + "LAV_MALE"] = row is null ? "" : Fmt(row.Counts.LavMale, "0.00");
            v[p + "LAV_FEMALE"] = row is null ? "" : Fmt(row.Counts.LavFemale, "0.00");
            v[p + "BATH"] = row is null ? "" : Fmt(row.Counts.Bath, "0.00");
            v[p + "FOUNTAIN"] = row is null ? "" : Fmt(row.Counts.Fountain, "0.00");
            v[p + "SERVICE_SINK"] = row is null ? "" : Fmt(row.Counts.ServiceSink, "0");
        }
        return v;
    }

    internal static string Fmt(double x, string format) => x.ToString(format, CultureInfo.InvariantCulture);
}
