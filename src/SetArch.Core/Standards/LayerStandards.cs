using SetArch.Core.Models;

namespace SetArch.Core.Standards;

/// <param name="Color">AutoCAD Color Index (1-255).</param>
/// <param name="Lineweight">Hundredths of a millimetre (e.g. 35 = 0.35 mm), or -3 for Default.</param>
public sealed record LayerSpec(string Name, int Color, string Linetype, int Lineweight, bool Plot, string Description);

public sealed record TextStyleSpec(string Name, string FontFile);

public static class LayerStandards
{
    public const string TitleBlock = "G-ANNO-TTLB";
    public const string TitleBlockText = "G-ANNO-TTLB-TEXT";
    public const string Viewports = "G-ANNO-VPRT";
    public const string ViewTitles = "G-ANNO-TITL";

    public const string TitleStyle = "SA-TITLE";
    public const string TextStyle = "SA-TEXT";

    /// <summary>Layers SetArch always needs for the sheets themselves.</summary>
    public static readonly IReadOnlyList<LayerSpec> SheetLayers = new[]
    {
        new LayerSpec(TitleBlock, 7, "Continuous", 50, true, "Sheet border and title block linework"),
        new LayerSpec(TitleBlockText, 7, "Continuous", 25, true, "Title block text"),
        new LayerSpec(ViewTitles, 7, "Continuous", 35, true, "View titles"),
        new LayerSpec(Viewports, 8, "Continuous", -3, false, "Viewport frames (not plotted)"),
    };

    /// <summary>Architectural layer set loosely following the US National CAD Standard (AIA layer guidelines).</summary>
    public static readonly IReadOnlyList<LayerSpec> Ncs = new[]
    {
        new LayerSpec("A-WALL", 2, "Continuous", 50, true, "Walls"),
        new LayerSpec("A-WALL-PATT", 8, "Continuous", 13, true, "Wall hatch / pattern"),
        new LayerSpec("A-WALL-FULL", 2, "Continuous", 50, true, "Full-height walls"),
        new LayerSpec("A-WALL-PRHT", 3, "Continuous", 35, true, "Partial-height walls"),
        new LayerSpec("A-DOOR", 3, "Continuous", 25, true, "Doors"),
        new LayerSpec("A-GLAZ", 4, "Continuous", 25, true, "Windows and glazing"),
        new LayerSpec("A-FLOR", 6, "Continuous", 25, true, "Floor information"),
        new LayerSpec("A-FLOR-STRS", 6, "Continuous", 25, true, "Stairs"),
        new LayerSpec("A-FLOR-OTLN", 6, "Continuous", 35, true, "Floor outline"),
        new LayerSpec("A-CLNG", 7, "Continuous", 18, true, "Ceiling information"),
        new LayerSpec("A-CLNG-GRID", 8, "Continuous", 13, true, "Ceiling grid"),
        new LayerSpec("A-ROOF", 5, "Continuous", 35, true, "Roof"),
        new LayerSpec("A-ROOF-OTLN", 5, "HIDDEN", 25, true, "Roof outline above"),
        new LayerSpec("A-EQPM", 30, "Continuous", 18, true, "Equipment"),
        new LayerSpec("A-FURN", 9, "Continuous", 13, true, "Furniture"),
        new LayerSpec("A-HIDD", 8, "HIDDEN", 18, true, "Hidden / overhead lines"),
        new LayerSpec("A-ELEV", 7, "Continuous", 25, true, "Elevation linework"),
        new LayerSpec("A-SECT", 7, "Continuous", 35, true, "Section linework"),
        new LayerSpec("A-DETL", 7, "Continuous", 25, true, "Detail linework"),
        new LayerSpec("A-ANNO-DIMS", 1, "Continuous", 18, true, "Dimensions"),
        new LayerSpec("A-ANNO-TEXT", 7, "Continuous", 25, true, "Text"),
        new LayerSpec("A-ANNO-NOTE", 7, "Continuous", 25, true, "Notes"),
        new LayerSpec("A-ANNO-SYMB", 7, "Continuous", 25, true, "Symbols, tags, callouts"),
        new LayerSpec("A-ANNO-NPLT", 6, "Continuous", -3, false, "Non-plotting construction notes"),
        new LayerSpec("A-GRID", 8, "CENTER", 18, true, "Column grid"),
        new LayerSpec("A-GRID-IDEN", 7, "Continuous", 25, true, "Grid bubbles and tags"),
        new LayerSpec("S-COLS", 1, "Continuous", 50, true, "Structural columns"),
        new LayerSpec("C-PROP", 7, "PHANTOM", 35, true, "Property line"),
        new LayerSpec("L-PLNT", 3, "Continuous", 18, true, "Landscape planting"),
        new LayerSpec("X-REFS", 7, "Continuous", -3, true, "External references"),
    };

    public static IReadOnlyList<LayerSpec> For(LayerStandard standard) => standard switch
    {
        LayerStandard.Ncs => SheetLayers.Concat(Ncs).ToList(),
        _ => SheetLayers,
    };

    public static IReadOnlyList<TextStyleSpec> TextStyles(bool includeProjectStyles) =>
        includeProjectStyles
            ? new[]
            {
                new TextStyleSpec(TitleStyle, "arialbd.ttf"),
                new TextStyleSpec(TextStyle, "arial.ttf"),
                new TextStyleSpec("SA-NOTES", "romans.shx"),
            }
            : new[]
            {
                new TextStyleSpec(TitleStyle, "arialbd.ttf"),
                new TextStyleSpec(TextStyle, "arial.ttf"),
            };
}
