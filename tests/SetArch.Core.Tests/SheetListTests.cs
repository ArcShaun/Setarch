using SetArch.Core.Models;
using SetArch.Core.Planning;
using SetArch.Core.Standards;
using Xunit;

namespace SetArch.Core.Tests;

public class SheetListTests
{
    [Fact]
    public void Ncs_numbering_follows_series()
    {
        var sel = new SheetSelection { FloorCount = 2, ElevationSheetCount = 2, SectionSheetCount = 1, DetailSheetCount = 1 };
        var list = SheetListGenerator.Generate(sel, PaperUnits.Millimeters);
        var numbers = list.Select(s => s.Number).ToList();

        Assert.Equal(new[] { "G-001", "G-002", "A-100", "A-101", "A-102", "A-103", "A-201", "A-202", "A-301", "A-501", "A-601" }, numbers);
        Assert.Equal("Ground Floor Plan", list.Single(s => s.Number == "A-101").Title);
        Assert.Equal("Roof Plan", list.Single(s => s.Number == "A-103").Title);
        Assert.Equal("1:100", list.Single(s => s.Number == "A-101").ScaleId);
        Assert.Equal("", list.Single(s => s.Number == "G-001").ScaleId);
    }

    [Fact]
    public void Plans_start_at_101_without_site_plan()
    {
        var sel = new SheetSelection { IncludeSitePlan = false, FloorCount = 1, IncludeRoofPlan = false };
        var list = SheetListGenerator.Generate(sel, PaperUnits.Inches);
        Assert.Contains(list, s => s.Number == "A-101" && s.ScaleId == "1/4\"=1'-0\"");
    }

    [Fact]
    public void Sequential_numbering_uses_prefix()
    {
        var sel = new SheetSelection { Numbering = NumberingScheme.Sequential, SequentialPrefix = "AR", FloorCount = 1 };
        var list = SheetListGenerator.Generate(sel, PaperUnits.Millimeters);
        Assert.Equal("AR00", list[0].Number);
        Assert.Equal("AR01", list[1].Number);
        Assert.Equal(list.Count, list.Select(s => s.Number).Distinct().Count());
    }

    [Fact]
    public void Basements_come_before_ground_floor()
    {
        var sel = new SheetSelection { BasementCount = 2, FloorCount = 1, IncludeSitePlan = false };
        var titles = SheetListGenerator.Generate(sel, PaperUnits.Millimeters).Select(s => s.Title).ToList();
        Assert.True(titles.IndexOf("Basement 2 Plan") < titles.IndexOf("Basement Plan"));
        Assert.True(titles.IndexOf("Basement Plan") < titles.IndexOf("Ground Floor Plan"));
    }
}

public class MediaTests
{
    private static readonly string[] Plotter =
    {
        "ISO_A1_(841.00_x_594.00_MM)", "ISO_full_bleed_A10_(26.00_x_37.00_MM)", "ISO_expand_A1_(841.00_x_594.00_MM)",
        "ARCH_E1_(30.00_x_42.00_Inches)", "ARCH_E_(36.00_x_48.00_Inches)", "Letter",
    };

    [Fact]
    public void Exact_pdf_name_wins()
    {
        var p = PaperSizes.Get("ISO-A1");
        Assert.Equal(p.PdfMediaName, PaperSizes.PickMedia(Plotter.Append(p.PdfMediaName).ToList(), p));
    }

    [Fact]
    public void Falls_back_without_confusing_A1_and_A10_or_E_and_E1()
    {
        Assert.Equal("ISO_A1_(841.00_x_594.00_MM)", PaperSizes.PickMedia(Plotter, PaperSizes.Get("ISO-A1")));
        Assert.Equal("ARCH_E_(36.00_x_48.00_Inches)", PaperSizes.PickMedia(Plotter, PaperSizes.Get("ARCH-E")));
        Assert.Equal("ARCH_E1_(30.00_x_42.00_Inches)", PaperSizes.PickMedia(Plotter, PaperSizes.Get("ARCH-E1")));
        Assert.Null(PaperSizes.PickMedia(Plotter, PaperSizes.Get("ANSI-D")));
    }

    [Fact]
    public void Media_orientation_is_parsed_from_name()
    {
        Assert.False(PaperSizes.MediaIsLandscape("ISO_full_bleed_A0_(841.00_x_1189.00_MM)"));
        Assert.True(PaperSizes.MediaIsLandscape("ARCH full bleed D (36.00 x 24.00 Inches)"));
    }
}
