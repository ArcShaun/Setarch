using SetArch.Core.Models;
using SetArch.Core.Output;
using SetArch.Core.Planning;
using SetArch.Core.Standards;
using Xunit;

namespace SetArch.Core.Tests;

public class PlanTests
{
    public static TheoryData<string, Orientation, TitleBlockPlacement> Formats()
    {
        var data = new TheoryData<string, Orientation, TitleBlockPlacement>();
        foreach (var p in PaperSizes.All)
            foreach (var o in new[] { Orientation.Landscape, Orientation.Portrait })
                foreach (var pl in new[] { TitleBlockPlacement.RightStrip, TitleBlockPlacement.BottomStrip })
                    data.Add(p.Id, o, pl);
        return data;
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void All_geometry_fits_on_the_paper(string paperId, Orientation orientation, TitleBlockPlacement placement)
    {
        var answers = new ProjectAnswers();
        answers.Format.PaperSizeId = paperId;
        answers.Format.Orientation = orientation;
        answers.TitleBlock.Placement = placement;
        answers.Sheets.FloorCount = 12; // lots of sheets so the cover index has to wrap

        var plan = SheetPlanBuilder.Build(answers);
        Assert.Equal(orientation == Orientation.Landscape, plan.Width > plan.Height);

        foreach (var sheet in plan.Sheets)
        {
            foreach (var pt in Points(sheet))
            {
                Assert.InRange(pt.X, 0, plan.Width);
                Assert.InRange(pt.Y, 0, plan.Height);
            }
            foreach (var t in sheet.Entities.OfType<TextEntity>())
                Assert.True(t.Height >= 0, $"{sheet.Number}: negative text height");

            if (sheet.Viewport is { } vp)
            {
                Assert.True(vp.Width > 0 && vp.Height > 0);
                Assert.InRange(vp.Center.X - vp.Width / 2, 0, plan.Width);
                Assert.InRange(vp.Center.Y + vp.Height / 2, 0, plan.Height);
            }
        }

        var cover = plan.Sheets.Single(s => s.Kind == SheetKind.Cover);
        foreach (var s in plan.Sheets)
            Assert.Contains(cover.Entities.OfType<TextEntity>(), t => t.Text == s.Number);
    }

    [Fact]
    public void Imperial_paper_uses_inch_scales_and_template()
    {
        var answers = new ProjectAnswers();
        answers.Format.PaperSizeId = "ARCH-D";
        var plan = SheetPlanBuilder.Build(answers);
        Assert.Equal(PaperUnits.Inches, plan.Units);
        Assert.Equal("acad.dwt", plan.DrawingTemplate);
        Assert.Equal(1.0 / 48, plan.Sheets.First(s => s.Kind == SheetKind.FloorPlan).Viewport!.Scale.Factor, 9);
    }

    [Fact]
    public void Duplicate_numbers_fail_validation()
    {
        var answers = new ProjectAnswers();
        answers.SheetList.Add(new SheetDefinition { Number = "A-101", Title = "One" });
        answers.SheetList.Add(new SheetDefinition { Number = "a-101", Title = "Two" });
        Assert.Contains(SheetPlanBuilder.Validate(answers), e => e.Contains("A-101"));
        Assert.Throws<InvalidOperationException>(() => SheetPlanBuilder.Build(answers));
    }

    [Fact]
    public void Layout_names_are_sanitized_and_unique()
    {
        var answers = new ProjectAnswers();
        answers.SheetList.Add(new SheetDefinition { Number = "A-1", Title = "Plan: Level 1/2", Kind = SheetKind.FloorPlan, ScaleId = "1:100" });
        answers.SheetList.Add(new SheetDefinition { Number = "A-2", Title = "Plan", Kind = SheetKind.FloorPlan });
        answers.Standards.LayoutNaming = LayoutNaming.NumberAndTitle;
        var plan = SheetPlanBuilder.Build(answers);
        Assert.Equal("A-1 Plan- Level 1-2", plan.Sheets[0].LayoutName);
        Assert.Null(plan.Sheets[1].Viewport); // no scale → no viewport
    }

    [Fact]
    public void Custom_title_block_fills_attributes()
    {
        var answers = new ProjectAnswers();
        answers.Project.ProjectName = "Harbour House";
        answers.TitleBlock.Mode = TitleBlockMode.CustomBlock;
        answers.TitleBlock.CustomBlockPath = @"C:\Standards\My Title Block.dwg";
        var plan = SheetPlanBuilder.Build(answers);
        var ins = plan.Sheets[0].TitleBlockInsert!;
        Assert.Equal("My_Title_Block", ins.BlockName);
        Assert.Equal("Harbour House", ins.Attributes[TitleBlockFields.Normalize("Project-Name")]);
        Assert.Equal(plan.Sheets[0].Number, ins.Attributes["DWGNO"]);
    }

    [Fact]
    public void Lisp_output_is_balanced_and_escaped()
    {
        var answers = new ProjectAnswers();
        answers.Project.ProjectName = "The \"Quote\" House \\ Annex";
        answers.TitleBlock.Mode = TitleBlockMode.CustomBlock;
        answers.TitleBlock.CustomBlockPath = @"C:\tb\TB.dwg";
        var lisp = LispGenerator.Generate(SheetPlanBuilder.Build(answers));

        Assert.Contains("(defun c:SETARCH", lisp);
        Assert.Contains("\\\"Quote\\\"", lisp);
        Assert.Contains("C:\\\\tb\\\\TB.dwg", lisp);
        Assert.Equal(0, ParenBalance(lisp));
    }

    [Fact]
    public void Lisp_numbers_use_invariant_culture()
    {
        var old = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("12.5", LispGenerator.N(12.5));
            Assert.Equal("3.0", LispGenerator.N(3));
        }
        finally { Thread.CurrentThread.CurrentCulture = old; }
    }

    /// <summary>Counts parens outside of strings and comments; 0 means balanced.</summary>
    private static int ParenBalance(string lisp)
    {
        int depth = 0;
        bool inString = false, inComment = false;
        for (int i = 0; i < lisp.Length; i++)
        {
            var c = lisp[i];
            if (inComment) { if (c == '\n') inComment = false; continue; }
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == ';') inComment = true;
            else if (c == '"') inString = true;
            else if (c == '(') depth++;
            else if (c == ')') { depth--; Assert.True(depth >= 0, $"Unbalanced ')' at {i}"); }
        }
        Assert.False(inString, "Unterminated string");
        return depth;
    }

    private static IEnumerable<Point2> Points(SheetLayout s) => s.Entities.SelectMany(e => e switch
    {
        LineEntity l => new[] { l.Start, l.End },
        PolylineEntity p => p.Points,
        TextEntity t => new[] { t.Position },
        _ => Array.Empty<Point2>(),
    });
}

public class PreviewDump
{
    [Fact]
    public void Dump_previews_when_requested()
    {
        var dir = Environment.GetEnvironmentVariable("SETARCH_PREVIEW_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        foreach (var (paper, orient, placement) in new[] { ("ISO-A1", Orientation.Landscape, TitleBlockPlacement.RightStrip), ("ARCH-D", Orientation.Landscape, TitleBlockPlacement.BottomStrip), ("ISO-A3", Orientation.Portrait, TitleBlockPlacement.BottomStrip) })
        {
            var a = new ProjectAnswers();
            a.Project = new ProjectInfo { ProjectName = "Harbour View Residence", ProjectNumber = "2026-014", Client = "J. & M. Example", SiteAddress = "12 Example Street, Springfield", FirmName = "Studio Example Architects", DrawnBy = "AB", CheckedBy = "CD", Revision = "A" };
            a.Format.PaperSizeId = paper; a.Format.Orientation = orient; a.TitleBlock.Placement = placement;
            var plan = SheetPlanBuilder.Build(a);
            foreach (var s in plan.Sheets.Take(3))
                File.WriteAllText(Path.Combine(dir, $"{paper}-{orient}-{s.Number}.svg"), SvgPreview.Render(plan, s));
            File.WriteAllText(Path.Combine(dir, $"{paper}.lsp"), LispGenerator.Generate(plan));
        }
    }
}
