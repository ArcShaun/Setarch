namespace SetArch.Core.Models;

/// <summary>
/// Everything the wizard asks the user. Serializable to JSON so a set of answers
/// can be saved and reused as a template for the next project.
/// </summary>
public sealed class ProjectAnswers
{
    public ProjectInfo Project { get; set; } = new();
    public SheetFormat Format { get; set; } = new();
    public TitleBlockOptions TitleBlock { get; set; } = new();
    public SheetSelection Sheets { get; set; } = new();
    public StandardsOptions Standards { get; set; } = new();

    /// <summary>
    /// The final, user-editable sheet list. When empty, the list is generated from <see cref="Sheets"/>.
    /// </summary>
    public List<SheetDefinition> SheetList { get; set; } = new();
}

public sealed class ProjectInfo
{
    public string ProjectName { get; set; } = "New Project";
    public string ProjectNumber { get; set; } = "";
    public string Client { get; set; } = "";
    public string SiteAddress { get; set; } = "";
    public string FirmName { get; set; } = "";
    public string DrawnBy { get; set; } = "";
    public string CheckedBy { get; set; } = "";
    public string Date { get; set; } = DateTime.Today.ToString("yyyy-MM-dd");
    public string Revision { get; set; } = "-";
    public string Status { get; set; } = "PRELIMINARY";
}

public enum Orientation { Landscape, Portrait }

public sealed class SheetFormat
{
    /// <summary>Id from <see cref="Standards.PaperSizes"/>, e.g. "ISO-A1" or "ARCH-D".</summary>
    public string PaperSizeId { get; set; } = "ISO-A1";
    public Orientation Orientation { get; set; } = Orientation.Landscape;
    public string PlotDevice { get; set; } = "DWG To PDF.pc3";
    public string PlotStyleTable { get; set; } = "monochrome.ctb";
}

public enum TitleBlockMode { Generated, CustomBlock }
public enum TitleBlockPlacement { RightStrip, BottomStrip }

public sealed class TitleBlockOptions
{
    public TitleBlockMode Mode { get; set; } = TitleBlockMode.Generated;
    public TitleBlockPlacement Placement { get; set; } = TitleBlockPlacement.RightStrip;

    /// <summary>
    /// Path to the user's own title block DWG (inserted at 0,0 in paper space on every sheet).
    /// Attributes are filled by tag; see <see cref="Planning.TitleBlockFields"/>.
    /// </summary>
    public string CustomBlockPath { get; set; } = "";

    /// <summary>
    /// When using a custom block: the drawing area (in paper units) available for the viewport,
    /// measured as margins from each paper edge. Ignored for the generated title block.
    /// </summary>
    public double CustomMarginLeft { get; set; } = 25;
    public double CustomMarginRight { get; set; } = 150;
    public double CustomMarginTop { get; set; } = 10;
    public double CustomMarginBottom { get; set; } = 10;
}

public enum NumberingScheme
{
    /// <summary>US National CAD Standard style: G-001, A-101, A-201 ...</summary>
    Ncs,
    /// <summary>Simple sequential: A00, A01, A02 ...</summary>
    Sequential,
}

public sealed class SheetSelection
{
    public bool IncludeCover { get; set; } = true;
    public bool IncludeGeneralNotes { get; set; } = true;
    public bool IncludeSitePlan { get; set; } = true;
    public int BasementCount { get; set; } = 0;
    public int FloorCount { get; set; } = 2;
    public bool IncludeRoofPlan { get; set; } = true;
    public bool IncludeReflectedCeilingPlans { get; set; } = false;
    public int ElevationSheetCount { get; set; } = 2;
    public int SectionSheetCount { get; set; } = 1;
    public int DetailSheetCount { get; set; } = 1;
    public bool IncludeSchedules { get; set; } = true;
    public NumberingScheme Numbering { get; set; } = NumberingScheme.Ncs;
    public string SequentialPrefix { get; set; } = "A";
}

public enum LayerStandard { Ncs, None }
public enum LayoutNaming { NumberAndTitle, NumberOnly }

public sealed class StandardsOptions
{
    public LayerStandard Layers { get; set; } = LayerStandard.Ncs;
    public bool CreateTextStyles { get; set; } = true;
    public LayoutNaming LayoutNaming { get; set; } = LayoutNaming.NumberAndTitle;
    public bool DeleteDefaultLayouts { get; set; } = true;
    public bool LockViewports { get; set; } = true;
    public bool AddViewTitles { get; set; } = true;
}

public enum SheetKind
{
    Cover,
    GeneralNotes,
    SitePlan,
    FloorPlan,
    RoofPlan,
    ReflectedCeilingPlan,
    Elevation,
    Section,
    Detail,
    Schedule,
    Other,
}

/// <summary>One row of the sheet list.</summary>
public sealed class SheetDefinition
{
    public string Number { get; set; } = "";
    public string Title { get; set; } = "";
    public SheetKind Kind { get; set; } = SheetKind.Other;

    /// <summary>Scale id from <see cref="Standards.Scales"/>, or empty for "no viewport".</summary>
    public string ScaleId { get; set; } = "";
}
