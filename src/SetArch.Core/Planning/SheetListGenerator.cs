using SetArch.Core.Models;
using SetArch.Core.Standards;

namespace SetArch.Core.Planning;

/// <summary>Turns the "which sheets do you need?" answers into a numbered sheet list.</summary>
public static class SheetListGenerator
{
    public static List<SheetDefinition> Generate(SheetSelection s, PaperUnits units)
    {
        var groups = new List<(int Series, string Discipline, List<(string Title, SheetKind Kind)> Items)>();

        var general = new List<(string, SheetKind)>();
        if (s.IncludeCover) general.Add(("Cover Sheet", SheetKind.Cover));
        if (s.IncludeGeneralNotes) general.Add(("General Notes", SheetKind.GeneralNotes));
        groups.Add((0, "G", general));

        var plans = new List<(string, SheetKind)>();
        if (s.IncludeSitePlan) plans.Add(("Site Plan", SheetKind.SitePlan));
        for (var b = Math.Max(0, s.BasementCount); b >= 1; b--)
            plans.Add((b == 1 ? "Basement Plan" : $"Basement {b} Plan", SheetKind.FloorPlan));
        for (var f = 0; f < Math.Max(0, s.FloorCount); f++)
            plans.Add((FloorName(f) + " Plan", SheetKind.FloorPlan));
        if (s.IncludeRoofPlan) plans.Add(("Roof Plan", SheetKind.RoofPlan));
        if (s.IncludeReflectedCeilingPlans)
            for (var f = 0; f < Math.Max(0, s.FloorCount); f++)
                plans.Add((FloorName(f) + " Reflected Ceiling Plan", SheetKind.ReflectedCeilingPlan));
        groups.Add((1, "A", plans));

        groups.Add((2, "A", Numbered("Elevations", s.ElevationSheetCount, SheetKind.Elevation)));
        groups.Add((3, "A", Numbered("Sections", s.SectionSheetCount, SheetKind.Section)));
        groups.Add((5, "A", Numbered("Details", s.DetailSheetCount, SheetKind.Detail)));
        groups.Add((6, "A", s.IncludeSchedules ? new() { ("Door and Window Schedules", SheetKind.Schedule) } : new()));

        var result = new List<SheetDefinition>();
        var sequential = 0;
        foreach (var (series, discipline, items) in groups)
        {
            for (var i = 0; i < items.Count; i++)
            {
                var (title, kind) = items[i];
                string number;
                if (s.Numbering == NumberingScheme.Ncs)
                {
                    // Series 0 starts at x01 (G-001), plan series starts at x00 when a site plan is present (A-100 Site Plan).
                    var start = series == 1 && s.IncludeSitePlan ? 0 : 1;
                    number = $"{discipline}-{series}{i + start:00}";
                }
                else
                {
                    var prefix = string.IsNullOrWhiteSpace(s.SequentialPrefix) ? "A" : s.SequentialPrefix.Trim();
                    number = $"{prefix}{sequential++:00}";
                }

                result.Add(new SheetDefinition
                {
                    Number = number,
                    Title = title,
                    Kind = kind,
                    ScaleId = Scales.DefaultFor(kind, units),
                });
            }
        }

        return result;
    }

    public static string FloorName(int index) => index switch
    {
        0 => "Ground Floor",
        1 => "First Floor",
        2 => "Second Floor",
        3 => "Third Floor",
        _ => $"{Ordinal(index)} Floor",
    };

    private static List<(string, SheetKind)> Numbered(string title, int count, SheetKind kind)
    {
        var list = new List<(string, SheetKind)>();
        for (var i = 1; i <= Math.Max(0, count); i++)
            list.Add((count == 1 ? title : $"{title} {i}", kind));
        return list;
    }

    private static string Ordinal(int n)
    {
        var suffix = (n % 100) is 11 or 12 or 13 ? "th" : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return n + suffix;
    }
}
