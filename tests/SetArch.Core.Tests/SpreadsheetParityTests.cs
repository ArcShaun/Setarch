using System.Text.Json;
using SetArch.Core.Calculators;
using Xunit;

namespace SetArch.Core.Tests;

/// <summary>
/// Compares the C# calculators with the office spreadsheets recalculated by a real spreadsheet engine.
/// Runs only when SETARCH_PARITY_DIR points at a folder with plumb_cases.json, egress_cases.json and expected.json
/// (produced from the workbooks with LibreOffice; see CLAUDE.md).
/// </summary>
public class SpreadsheetParityTests
{
    private static string? Dir => Environment.GetEnvironmentVariable("SETARCH_PARITY_DIR");

    [Fact]
    public void Plumbing_matches_the_workbook()
    {
        if (string.IsNullOrEmpty(Dir)) return;
        var cases = JsonSerializer.Deserialize<List<PlumbingInput>>(File.ReadAllText(Path.Combine(Dir, "plumb_cases.json")))!;
        var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir, "expected.json"))).RootElement.GetProperty("Plumbing");
        var failures = new List<string>();

        for (var i = 0; i < cases.Count; i++)
        {
            var r = PlumbingCalculator.Calculate(cases[i]);
            var e = expected[i];
            var rows = e.GetProperty("Rows");
            for (var k = 0; k < rows.GetArrayLength(); k++)
            {
                var row = r.Rows[k];
                Check(failures, $"case {i} row {k + 1} {row.Input.Function}", rows[k], new()
                {
                    ["I"] = row.Occupants, ["J"] = row.Counts.WcMale, ["K"] = row.Counts.WcFemale, ["M"] = row.Counts.LavMale,
                    ["N"] = row.Counts.LavFemale, ["P"] = row.Counts.Bath, ["Q"] = row.Counts.Fountain, ["R"] = row.Counts.ServiceSink,
                });
            }
            Check(failures, $"case {i} exact", e.GetProperty("Exact"), Map(r.Exact));
            Check(failures, $"case {i} required", e.GetProperty("Required"), Map(r.Required));
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(30)));
    }

    [Fact]
    public void Egress_matches_the_workbook()
    {
        if (string.IsNullOrEmpty(Dir)) return;
        var cases = JsonSerializer.Deserialize<List<EgressInput>>(File.ReadAllText(Path.Combine(Dir, "egress_cases.json")))!;
        var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(Dir, "expected.json"))).RootElement.GetProperty("Egress");
        var failures = new List<string>();

        for (var i = 0; i < cases.Count; i++)
        {
            var r = EgressCalculator.Calculate(cases[i]);
            var e = expected[i];
            var label = $"case {i} ({cases[i].FbcOccupancy} / {cases[i].FfpcOccupancy} / {(cases[i].Sprinklered ? "spr" : "unspr")})";
            for (var k = 0; k < 3; k++)
            {
                var want = e.GetProperty("Lines")[k].EnumerateArray().Select(x => x.GetString()!).ToArray();
                var l = r.Lines[k];
                var got = new[] { l.FbcLimit, l.FbcSection, l.FfpcLimit, l.FfpcSection };
                if (!want.SequenceEqual(got)) failures.Add($"{label} line {k}: want [{string.Join(" | ", want)}] got [{string.Join(" | ", got)}]");
            }
            var notes = e.GetProperty("Notes").EnumerateArray().Select(x => x.GetString()!).Where(s => s.Length > 0).ToArray();
            var gotNotes = r.Footnotes.Take(EgressCalculator.ShownFootnotes).Select(f => f.Id).ToArray();
            if (!notes.SequenceEqual(gotNotes)) failures.Add($"{label} notes: want [{string.Join(",", notes)}] got [{string.Join(",", gotNotes)}]");
            if (e.GetProperty("Message").GetString() != r.FootnoteMessage) failures.Add($"{label} message: want '{e.GetProperty("Message").GetString()}' got '{r.FootnoteMessage}'");
        }
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(30)));
    }

    private static Dictionary<string, double> Map(FixtureCounts c) => new()
    {
        ["J"] = c.WcMale, ["K"] = c.WcFemale, ["M"] = c.LavMale, ["N"] = c.LavFemale, ["P"] = c.Bath, ["Q"] = c.Fountain, ["R"] = c.ServiceSink,
    };

    private static void Check(List<string> failures, string label, JsonElement want, Dictionary<string, double> got)
    {
        foreach (var (col, value) in got)
        {
            var w = want.GetProperty(col);
            if (w.ValueKind != JsonValueKind.Number) { failures.Add($"{label} {col}: sheet shows '{w}'"); continue; }
            if (Math.Abs(w.GetDouble() - value) > 1e-9 * Math.Max(1, Math.Abs(value)))
                failures.Add($"{label} {col}: sheet {w.GetDouble()} vs SetArch {value}");
        }
    }
}
