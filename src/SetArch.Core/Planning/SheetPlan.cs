using SetArch.Core.Models;
using SetArch.Core.Standards;

namespace SetArch.Core.Planning;

public readonly record struct Point2(double X, double Y)
{
    public Point2 Offset(double dx, double dy) => new(X + dx, Y + dy);
}

public enum TextAlign { BaselineLeft, MiddleLeft, MiddleCenter }

/// <summary>A primitive drawn in paper space. Coordinates are in paper units, origin at the paper's lower-left corner.</summary>
public abstract record PaperEntity(string Layer);

public sealed record LineEntity(Point2 Start, Point2 End, string Layer) : PaperEntity(Layer);

public sealed record PolylineEntity(IReadOnlyList<Point2> Points, bool Closed, string Layer) : PaperEntity(Layer)
{
    public static PolylineEntity Rectangle(double x1, double y1, double x2, double y2, string layer) =>
        new(new[] { new Point2(x1, y1), new Point2(x2, y1), new Point2(x2, y2), new Point2(x1, y2) }, true, layer);
}

public sealed record TextEntity(string Text, Point2 Position, double Height, TextAlign Align, string Style, string Layer) : PaperEntity(Layer);

public sealed record ViewportSpec(Point2 Center, double Width, double Height, DrawingScale Scale, string Layer, bool Lock);

public sealed record BlockInsertSpec(string Path, string BlockName, Point2 Position, IReadOnlyDictionary<string, string> Attributes, string Layer);

public sealed class SheetLayout
{
    public required string Number { get; init; }
    public required string Title { get; init; }
    public required string LayoutName { get; init; }
    public required SheetKind Kind { get; init; }
    public List<PaperEntity> Entities { get; } = new();
    public ViewportSpec? Viewport { get; set; }
    public BlockInsertSpec? TitleBlockInsert { get; set; }
}

/// <summary>
/// Everything needed to build the sheets, already resolved to concrete geometry.
/// Both the AutoCAD COM writer and the AutoLISP exporter consume this, so they produce the same result.
/// </summary>
public sealed class SheetPlan
{
    public required ProjectInfo Project { get; init; }
    public required PaperSize Paper { get; init; }
    public required Orientation Orientation { get; init; }

    /// <summary>Paper width/height as laid out (after orientation), in paper units.</summary>
    public required double Width { get; init; }
    public required double Height { get; init; }

    public PaperUnits Units => Paper.Units;
    public required string PlotDevice { get; init; }
    public required string PlotStyleTable { get; init; }

    public required IReadOnlyList<LayerSpec> Layers { get; init; }
    public required IReadOnlyList<TextStyleSpec> TextStyles { get; init; }
    public required IReadOnlyList<SheetLayout> Sheets { get; init; }

    public bool DeleteDefaultLayouts { get; init; }

    /// <summary>Linetypes referenced by layers that must be loaded first.</summary>
    public IEnumerable<string> RequiredLinetypes =>
        Layers.Select(l => l.Linetype)
              .Where(l => !string.Equals(l, "Continuous", StringComparison.OrdinalIgnoreCase))
              .Distinct(StringComparer.OrdinalIgnoreCase);

    public string LinetypeFile => Units == PaperUnits.Millimeters ? "acadiso.lin" : "acad.lin";
    public string DrawingTemplate => Units == PaperUnits.Millimeters ? "acadiso.dwt" : "acad.dwt";
}
