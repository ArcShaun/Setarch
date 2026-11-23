using SetArch.Core.Models;

namespace SetArch.Core.Planning;

/// <summary>
/// Maps project/sheet values to attribute tags in a user's own title block.
/// Tags are matched case-insensitively ignoring spaces, dashes and underscores,
/// so PROJECT_NAME, ProjectName and PROJECT-NAME all match.
/// </summary>
public static class TitleBlockFields
{
    private static readonly (string Canonical, string[] Aliases)[] Map =
    {
        ("PROJECT_NAME", new[] { "PROJECT", "PROJ_NAME", "JOB_NAME", "JOB" }),
        ("PROJECT_NO", new[] { "PROJECT_NUMBER", "PROJ_NO", "JOB_NO", "JOB_NUMBER" }),
        ("CLIENT", new[] { "OWNER", "CLIENT_NAME" }),
        ("ADDRESS", new[] { "SITE_ADDRESS", "SITE", "LOCATION", "PROJECT_ADDRESS" }),
        ("FIRM", new[] { "ARCHITECT", "COMPANY", "FIRM_NAME", "OFFICE" }),
        ("SHEET_TITLE", new[] { "TITLE", "DWG_TITLE", "DRAWING_TITLE", "SHEET_NAME" }),
        ("SHEET_NO", new[] { "SHEET_NUMBER", "DWG_NO", "DRAWING_NO", "DRAWING_NUMBER", "SHEET" }),
        ("SHEET_OF", new[] { "SHEET_COUNT", "SHEETS", "TOTAL_SHEETS" }),
        ("SCALE", new[] { "DWG_SCALE", "DRAWING_SCALE" }),
        ("DRAWN", new[] { "DRAWN_BY", "DRN", "DRAFTER" }),
        ("CHECKED", new[] { "CHECKED_BY", "CHK", "CHECKER" }),
        ("DATE", new[] { "ISSUE_DATE", "DWG_DATE" }),
        ("REV", new[] { "REVISION", "REV_NO", "REVISION_NO" }),
        ("STATUS", new[] { "ISSUE", "ISSUE_STATUS", "STAGE" }),
    };

    public static string Normalize(string tag) =>
        new string(tag.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>Returns normalized tag → value for every known alias.</summary>
    public static IReadOnlyDictionary<string, string> Values(ProjectInfo p, string sheetNo, string sheetTitle, string scale, int index, int total)
    {
        var values = new Dictionary<string, string>
        {
            ["PROJECT_NAME"] = p.ProjectName,
            ["PROJECT_NO"] = p.ProjectNumber,
            ["CLIENT"] = p.Client,
            ["ADDRESS"] = p.SiteAddress,
            ["FIRM"] = p.FirmName,
            ["SHEET_TITLE"] = sheetTitle,
            ["SHEET_NO"] = sheetNo,
            ["SHEET_OF"] = $"{index} OF {total}",
            ["SCALE"] = scale,
            ["DRAWN"] = p.DrawnBy,
            ["CHECKED"] = p.CheckedBy,
            ["DATE"] = p.Date,
            ["REV"] = p.Revision,
            ["STATUS"] = p.Status,
        };

        var result = new Dictionary<string, string>();
        foreach (var (canonical, aliases) in Map)
        {
            var value = values[canonical] ?? "";
            result[Normalize(canonical)] = value;
            foreach (var alias in aliases)
                result.TryAdd(Normalize(alias), value);
        }
        return result;
    }
}
