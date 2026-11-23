using SetArch.Core.Models;

namespace SetArch.Core.Standards;

/// <param name="Factor">Paper units per model unit (viewport CustomScale). Model is in mm for metric, inches for imperial.</param>
public sealed record DrawingScale(string Id, string Name, double Factor, PaperUnits Units)
{
    public override string ToString() => Name;
}

public static class Scales
{
    public const string None = "";

    public static readonly IReadOnlyList<DrawingScale> Metric = new[]
    {
        M(1), M(2), M(5), M(10), M(20), M(25), M(50), M(100), M(200), M(250), M(500), M(1000),
    };

    public static readonly IReadOnlyList<DrawingScale> Imperial = new[]
    {
        I("3\"=1'-0\"", 3.0 / 12),
        I("1-1/2\"=1'-0\"", 1.5 / 12),
        I("1\"=1'-0\"", 1.0 / 12),
        I("3/4\"=1'-0\"", 0.75 / 12),
        I("1/2\"=1'-0\"", 0.5 / 12),
        I("3/8\"=1'-0\"", 0.375 / 12),
        I("1/4\"=1'-0\"", 0.25 / 12),
        I("3/16\"=1'-0\"", 0.1875 / 12),
        I("1/8\"=1'-0\"", 0.125 / 12),
        I("3/32\"=1'-0\"", 0.09375 / 12),
        I("1/16\"=1'-0\"", 0.0625 / 12),
        I("1\"=10'-0\"", 1.0 / 120),
        I("1\"=20'-0\"", 1.0 / 240),
        I("1\"=50'-0\"", 1.0 / 600),
    };

    public static IReadOnlyList<DrawingScale> For(PaperUnits units) => units == PaperUnits.Millimeters ? Metric : Imperial;

    public static DrawingScale? Find(string? id) =>
        string.IsNullOrWhiteSpace(id) ? null : Metric.Concat(Imperial).FirstOrDefault(s => s.Id == id);

    /// <summary>Typical default scale for a sheet type; empty means the sheet gets no viewport.</summary>
    public static string DefaultFor(SheetKind kind, PaperUnits units)
    {
        var metric = units == PaperUnits.Millimeters;
        return kind switch
        {
            SheetKind.SitePlan => metric ? "1:200" : "1\"=20'-0\"",
            SheetKind.FloorPlan => metric ? "1:100" : "1/4\"=1'-0\"",
            SheetKind.RoofPlan => metric ? "1:100" : "1/8\"=1'-0\"",
            SheetKind.ReflectedCeilingPlan => metric ? "1:100" : "1/4\"=1'-0\"",
            SheetKind.Elevation => metric ? "1:100" : "1/4\"=1'-0\"",
            SheetKind.Section => metric ? "1:50" : "1/2\"=1'-0\"",
            SheetKind.Detail => metric ? "1:10" : "1-1/2\"=1'-0\"",
            SheetKind.Other => metric ? "1:100" : "1/4\"=1'-0\"",
            _ => None,
        };
    }

    private static DrawingScale M(int n) => new($"1:{n}", $"1:{n}", 1.0 / n, PaperUnits.Millimeters);
    private static DrawingScale I(string name, double factor) => new(name, name, factor, PaperUnits.Inches);
}
