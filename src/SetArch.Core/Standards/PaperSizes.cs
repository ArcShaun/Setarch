namespace SetArch.Core.Standards;

public enum PaperUnits { Millimeters, Inches }

/// <param name="Id">Stable id stored in answers, e.g. "ISO-A1".</param>
/// <param name="Name">Display name.</param>
/// <param name="LongSide">Long edge in <paramref name="Units"/>.</param>
/// <param name="ShortSide">Short edge in <paramref name="Units"/>.</param>
/// <param name="PdfMediaName">Canonical media name for "DWG To PDF.pc3" (full bleed, so 0,0 is the paper corner).</param>
/// <param name="MediaToken">Token used to find a matching media on other plotters, e.g. "A1" or "ARCH_D" style hints.</param>
public sealed record PaperSize(
    string Id,
    string Family,
    string Name,
    double LongSide,
    double ShortSide,
    PaperUnits Units,
    string PdfMediaName,
    string MediaToken)
{
    /// <summary>Display form of the canonical name, used by the -PLOT command line.</summary>
    public string PdfMediaDisplayName => PdfMediaName.Replace('_', ' ');

    public override string ToString() => Name;
}

public static class PaperSizes
{
    public static readonly IReadOnlyList<PaperSize> All = new[]
    {
        new PaperSize("ISO-A0", "ISO", "ISO A0 (1189 x 841 mm)", 1189, 841, PaperUnits.Millimeters, "ISO_full_bleed_A0_(841.00_x_1189.00_MM)", "A0"),
        new PaperSize("ISO-A1", "ISO", "ISO A1 (841 x 594 mm)", 841, 594, PaperUnits.Millimeters, "ISO_full_bleed_A1_(841.00_x_594.00_MM)", "A1"),
        new PaperSize("ISO-A2", "ISO", "ISO A2 (594 x 420 mm)", 594, 420, PaperUnits.Millimeters, "ISO_full_bleed_A2_(594.00_x_420.00_MM)", "A2"),
        new PaperSize("ISO-A3", "ISO", "ISO A3 (420 x 297 mm)", 420, 297, PaperUnits.Millimeters, "ISO_full_bleed_A3_(420.00_x_297.00_MM)", "A3"),
        new PaperSize("ISO-A4", "ISO", "ISO A4 (297 x 210 mm)", 297, 210, PaperUnits.Millimeters, "ISO_full_bleed_A4_(297.00_x_210.00_MM)", "A4"),

        new PaperSize("ARCH-E", "ARCH", "ARCH E (48 x 36 in)", 48, 36, PaperUnits.Inches, "ARCH_full_bleed_E_(48.00_x_36.00_Inches)", "ARCH_full_bleed_E_"),
        new PaperSize("ARCH-E1", "ARCH", "ARCH E1 (42 x 30 in)", 42, 30, PaperUnits.Inches, "ARCH_full_bleed_E1_(42.00_x_30.00_Inches)", "ARCH_full_bleed_E1"),
        new PaperSize("ARCH-D", "ARCH", "ARCH D (36 x 24 in)", 36, 24, PaperUnits.Inches, "ARCH_full_bleed_D_(36.00_x_24.00_Inches)", "ARCH_full_bleed_D"),
        new PaperSize("ARCH-C", "ARCH", "ARCH C (24 x 18 in)", 24, 18, PaperUnits.Inches, "ARCH_full_bleed_C_(24.00_x_18.00_Inches)", "ARCH_full_bleed_C"),

        new PaperSize("ANSI-D", "ANSI", "ANSI D (34 x 22 in)", 34, 22, PaperUnits.Inches, "ANSI_full_bleed_D_(34.00_x_22.00_Inches)", "ANSI_full_bleed_D"),
        new PaperSize("ANSI-C", "ANSI", "ANSI C (22 x 17 in)", 22, 17, PaperUnits.Inches, "ANSI_full_bleed_C_(22.00_x_17.00_Inches)", "ANSI_full_bleed_C"),
        new PaperSize("ANSI-B", "ANSI", "ANSI B / Tabloid (17 x 11 in)", 17, 11, PaperUnits.Inches, "ANSI_full_bleed_B_(17.00_x_11.00_Inches)", "ANSI_full_bleed_B"),
    };

    public static PaperSize Get(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException($"Unknown paper size '{id}'.", nameof(id));

    /// <summary>
    /// Finds the best media on a plotter for the paper: exact PDF name, then a full-bleed match, then any match.
    /// </summary>
    public static string? PickMedia(IReadOnlyList<string> names, PaperSize paper)
    {
        var exact = names.FirstOrDefault(n => string.Equals(n, paper.PdfMediaName, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        bool Matches(string n) => paper.Family == "ISO"
            ? n.StartsWith("ISO", StringComparison.OrdinalIgnoreCase) && n.Contains($"_{paper.MediaToken}_", StringComparison.OrdinalIgnoreCase)
            : n.Contains(paper.MediaToken, StringComparison.OrdinalIgnoreCase)
              || n.Contains(paper.MediaToken.Replace("full_bleed_", ""), StringComparison.OrdinalIgnoreCase);

        return names.FirstOrDefault(n => Matches(n) && n.Contains("full_bleed", StringComparison.OrdinalIgnoreCase))
            ?? names.FirstOrDefault(Matches);
    }

    /// <summary>
    /// Parses the "(W_x_H_...)" part of a canonical media name to tell whether the media itself is landscape.
    /// </summary>
    public static bool? MediaIsLandscape(string canonicalMediaName)
    {
        var open = canonicalMediaName.LastIndexOf('(');
        if (open < 0) return null;
        var parts = canonicalMediaName[(open + 1)..].Split(new[] { "_x_", " x " }, StringSplitOptions.None);
        if (parts.Length < 2) return null;
        var w = ParseLeadingNumber(parts[0]);
        var h = ParseLeadingNumber(parts[1]);
        if (w is null || h is null) return null;
        return w > h;
    }

    private static double? ParseLeadingNumber(string s)
    {
        var digits = new string(s.TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        return double.TryParse(digits, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
    }
}
