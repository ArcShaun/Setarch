using System.Reflection;
using System.Text.Json;

namespace SetArch.Core.Calculators;

public sealed class EgressData
{
    public sealed record Choice(string Name, int Row);
    public sealed record Metric(string Name, int Unsprinklered, int Sprinklered);
    public sealed record Footnote(string Id, string Text);

    public string Source { get; set; } = "";
    /// <summary>'Resolved Table' rows 4–50, columns A:U, keyed by spreadsheet row number.</summary>
    public Dictionary<int, string[]> Rows { get; set; } = new();
    public List<Choice> Fbc { get; set; } = new();
    public List<Choice> Ffpc { get; set; } = new();
    public List<Metric> Metrics { get; set; } = new();
    public List<Footnote> Footnotes { get; set; } = new();

    private static readonly Lazy<EgressData> Embedded = new(() =>
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("SetArch.Core.Calculators.Data.egress.json")
                      ?? throw new InvalidOperationException("Missing calculator data egress.json.");
        return JsonSerializer.Deserialize<EgressData>(s) ?? throw new InvalidDataException("egress.json");
    });

    public static EgressData Default => Embedded.Value;

    /// <summary>INDEX('Resolved Table'!$A:$U, row, column) with a 1-based column.</summary>
    public string Cell(int row, int column) =>
        Rows.TryGetValue(row, out var r) && column >= 1 && column <= r.Length ? r[column - 1] : "";
}

public sealed class EgressInput
{
    /// <summary>FBC occupancy, e.g. "BUSINESS (B)" (cell B4).</summary>
    public string FbcOccupancy { get; set; } = "";
    /// <summary>FFPC / NFPA 101 occupancy, e.g. "BUSINESS - Existing" (cell B5).</summary>
    public string FfpcOccupancy { get; set; } = "";
    public bool Sprinklered { get; set; } = true;
}

public sealed record EgressLine(string Measurement, string FbcLimit, string FbcSection, string FfpcLimit, string FfpcSection);

public sealed record EgressResult(IReadOnlyList<EgressLine> Lines, IReadOnlyList<EgressData.Footnote> Footnotes, string FootnoteMessage);

/// <summary>
/// The egress distance calculator from common_path-dead_end-travel_distance.xlsx ('Egress Calculator' rows 10–20),
/// done in code so Excel isn't needed.
/// </summary>
public static class EgressCalculator
{
    public const int ShownFootnotes = 5;

    public static EgressResult Calculate(EgressInput input, EgressData? data = null)
    {
        data ??= EgressData.Default;
        var fbcRow = data.Fbc.FirstOrDefault(c => Same(c.Name, input.FbcOccupancy))?.Row ?? 0;   // E4
        var ffpcRow = data.Ffpc.FirstOrDefault(c => Same(c.Name, input.FfpcOccupancy))?.Row ?? 0; // E5

        var lines = new List<EgressLine>();
        foreach (var m in data.Metrics)
        {
            var col = input.Sprinklered ? m.Sprinklered : m.Unsprinklered; // G10:G12
            var fbc = fbcRow == 0 ? "Select FBC occupancy"
                : Blank(data.Cell(fbcRow, col)) ? "Not listed - see code" : data.Cell(fbcRow, col);
            var section = data.Cell(4, col);
            var ffpc = ffpcRow == 0 ? "Select FFPC occupancy"
                : Blank(data.Cell(ffpcRow, col + 1)) ? "Not listed - see code" : data.Cell(ffpcRow, col + 1);
            var ffpcSection = ffpcRow == 0 ? ""
                : Blank(data.Cell(ffpcRow, col + 2)) ? "See code" : "NFPA 101 " + data.Cell(ffpcRow, col + 2);
            lines.Add(new EgressLine(m.Name, fbc, section, ffpc, ffpcSection));
        }

        // H31: the text that is scanned for footnote ids.
        var scan = "";
        if (fbcRow != 0)
            scan += " " + data.Cell(fbcRow, 2) + " " + data.Cell(fbcRow, 3) + " " +
                    string.Join(" ", lines.Select(l => l.FbcLimit)) + " " + string.Join(" ", lines.Select(l => l.FbcSection));
        if (ffpcRow != 0)
            scan += " " + data.Cell(ffpcRow, 2) + " " + data.Cell(ffpcRow, 3) + " " + string.Join(" ", lines.Select(l => l.FfpcLimit));
        scan += " ";

        var notes = new List<EgressData.Footnote>();
        foreach (var note in data.Footnotes)
        {
            var referenced = note.Id == "a"
                ? lines.Any(l => l.FfpcSection.EndsWith("a", StringComparison.Ordinal))   // I49
                : scan.Contains(" " + note.Id, StringComparison.OrdinalIgnoreCase);        // I34:I48
            if (referenced) notes.Add(note);
        }

        var message = notes.Count == 0 ? "No footnotes apply to the current selection."
            : notes.Count > ShownFootnotes ? $"... plus {notes.Count - ShownFootnotes} more - see the footnote block on 'Egress Distances'."
            : "";
        return new EgressResult(lines, notes, message);
    }

    /// <summary>Placeholder values for drawings, e.g. [[CP_FBC]] = 100'.</summary>
    public static Dictionary<string, string> Placeholders(EgressInput input, EgressResult r)
    {
        var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["EGRESS_FBC_OCCUPANCY"] = input.FbcOccupancy,
            ["EGRESS_FFPC_OCCUPANCY"] = input.FfpcOccupancy,
            ["SPRINKLERED"] = input.Sprinklered ? "SPRINKLERED" : "UNSPRINKLERED",
            ["EGRESS_NOTES_MORE"] = r.FootnoteMessage,
        };
        var prefixes = new[] { "CP", "DE", "TD" }; // common path, dead end, travel distance — same order as the sheet
        for (var i = 0; i < r.Lines.Count && i < prefixes.Length; i++)
        {
            var l = r.Lines[i];
            v[prefixes[i] + "_FBC"] = l.FbcLimit;
            v[prefixes[i] + "_FBC_SECTION"] = l.FbcSection;
            v[prefixes[i] + "_FFPC"] = l.FfpcLimit;
            v[prefixes[i] + "_FFPC_SECTION"] = l.FfpcSection;
        }
        for (var i = 1; i <= ShownFootnotes; i++)
        {
            var note = i <= r.Footnotes.Count ? r.Footnotes[i - 1] : null;
            v[$"EGRESS_NOTE{i}_ID"] = note?.Id ?? "";
            v[$"EGRESS_NOTE{i}_TEXT"] = note?.Text ?? "";
            v[$"EGRESS_NOTE{i}"] = note is null ? "" : $"{note.Id} - {note.Text}";
        }
        return v;
    }

    private static bool Blank(string s) => string.IsNullOrWhiteSpace(s);
    private static bool Same(string a, string b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
