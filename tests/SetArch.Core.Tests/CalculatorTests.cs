using SetArch.Core.Calculators;
using Xunit;

namespace SetArch.Core.Tests;

public class CalculatorTests
{
    [Fact]
    public void Plumbing_matches_the_saved_spreadsheet_example()
    {
        // The workbook as saved: row 1 = Exercise rooms, 2,243 sq ft, 50/50, 1 floor, minimum 1 fountain.
        var input = new PlumbingInput { Rows = { new PlumbingRow { Function = "Exercise rooms", AreaOrUnits = 2243 } } };
        var r = PlumbingCalculator.Calculate(input);
        var row = r.Rows.Single();

        Assert.Equal(50, row.Factor);
        Assert.Equal(44.86, row.Occupants, 9);
        Assert.Equal(0.17944, row.Counts.WcMale, 9);
        Assert.Equal(0.34507692307692306, row.Counts.WcFemale, 9);
        Assert.Equal(0.11215, row.Counts.LavMale, 9);
        Assert.Equal(0.11215, row.Counts.LavFemale, 9);
        Assert.Equal(0, row.Counts.Bath);
        Assert.Equal(1, row.Counts.Fountain);
        Assert.Equal(1, row.Counts.ServiceSink);

        Assert.Equal(new FixtureCounts(1, 1, 1, 1, 0, 1, 1), r.Required);
        var v = PlumbingCalculator.Placeholders(input, r);
        Assert.Equal("2", v["WC_TOTAL_REQ"]);
        Assert.Equal("0.35", v["WC_FEMALE_CALC"]);
        Assert.Equal("Exercise rooms", v["PL1_FUNCTION"]);
        Assert.Equal("", v["PL2_FUNCTION"]);
    }

    [Fact]
    public void Egress_matches_the_saved_spreadsheet_example()
    {
        var input = new EgressInput { FbcOccupancy = "BUSINESS (B)", FfpcOccupancy = "BUSINESS - Existing", Sprinklered = true };
        var r = EgressCalculator.Calculate(input);

        Assert.Equal(new EgressLine("COMMON PATH OF EGRESS TRAVEL", "100'", "FBC 1006.2.1 2a", "100'", "NFPA 101 39.2.5.2.1"), r.Lines[0]);
        Assert.Equal(new EgressLine("DEAD END", "50'", "FBC 1020.5", "50'", "NFPA 101 39.2.5.3"), r.Lines[1]);
        Assert.Equal(new EgressLine("EXIT ACCESS TRAVEL DISTANCE", "300'", "FBC 1017.2", "300'", "NFPA 101 39.2.6.3"), r.Lines[2]);
        Assert.Equal(new[] { "2a", "3c" }, r.Footnotes.Select(f => f.Id));
        Assert.Equal("", r.FootnoteMessage);

        var v = EgressCalculator.Placeholders(input, r);
        Assert.Equal("300'", v["TD_FBC"]);
        Assert.StartsWith("3c - First story", v["EGRESS_NOTE2"]);
        Assert.Equal("", v["EGRESS_NOTE3"]);
    }

    [Fact]
    public void Unit_based_occupancies_use_the_count()
    {
        var input = new PlumbingInput { Rows = { new PlumbingRow { Function = "Residential - One- and two-family dwelling or lodging house", AreaOrUnits = 1 } } };
        var r = PlumbingCalculator.Calculate(input);
        Assert.Equal(1, r.Rows[0].Occupants);
        Assert.Equal(1, r.Required.WcMale);   // 1 WC per dwelling unit
        Assert.Equal(0, r.Required.WcFemale); // not separated by sex
        Assert.Equal(1, r.Required.Bath);
    }

    [Fact]
    public void Every_function_of_space_maps_to_an_occupancy()
    {
        foreach (var f in PlumbingData.Default.Functions)
            Assert.True(PlumbingData.Default.Occupancy(f.Occupancy) is not null, f.Name);
    }

    [Theory]
    [InlineData("B", "Business areas", "BUSINESS (B)", "BUSINESS - New")]
    [InlineData("r3", "Residential - One- and two-family dwelling or lodging house", "RESIDENTIAL R-3", null)]
    [InlineData("A-2 Assembly", "Assembly without fixed seats - Unconcentrated (tables and chairs)", "ASSEMBLY (A-1 thru A-5)", "ASSEMBLY - New")]
    public void Occupancy_group_suggests_calculator_inputs(string group, string function, string fbc, string? ffpc)
    {
        Assert.Equal(function, CodeDefaults.FunctionOfSpace(group));
        Assert.Equal(fbc, CodeDefaults.FbcEgress(group));
        Assert.Equal(ffpc, CodeDefaults.FfpcEgress(group, existingBuilding: false));
    }

    [Fact]
    public void Every_suggestion_exists_in_the_calculators()
    {
        foreach (var g in new[] { "A-1", "A-2", "A-3", "A-4", "A-5", "B", "E", "F-1", "F-2", "H-1", "I-1", "I-2", "I-3", "I-4", "M", "R-1", "R-2", "R-3", "R-4", "S-1", "S-2" })
        {
            Assert.NotNull(PlumbingData.Default.Function(CodeDefaults.FunctionOfSpace(g)!));
            if (CodeDefaults.FbcEgress(g) is { } fbc) Assert.Contains(EgressData.Default.Fbc, c => c.Name == fbc);
            foreach (var existing in new[] { true, false })
                if (CodeDefaults.FfpcEgress(g, existing) is { } ffpc) Assert.Contains(EgressData.Default.Ffpc, c => c.Name == ffpc);
        }
    }
}
