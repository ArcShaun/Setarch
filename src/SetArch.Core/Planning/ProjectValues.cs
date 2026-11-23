using System.Globalization;
using SetArch.Core.Models;

namespace SetArch.Core.Planning;

/// <summary>
/// Turns the typed answers into every value a placeholder can use:
/// <list type="bullet">
/// <item>each answer, e.g. PROJECT_NAME (dates re-written in their field's format);</item>
/// <item>each answer split over two lines: KEY_1 / KEY_2 (word wrap at <see cref="StandardsProfile.WrapLength"/>;
///   for ADDRESS, split at the first comma: street / city, state ZIP);</item>
/// <item>YEAR, from the first date answer (else this year);</item>
/// <item>the title block lines, e.g. LINE2 = [[PROJECT_NAME_1]].</item>
/// </list>
/// </summary>
public static class ProjectValues
{
    private static readonly string[] DateInputFormats =
    {
        "M/d/yyyy", "M/d/yy", "M-d-yyyy", "M.d.yyyy", "yyyy-M-d", "MMM d yyyy", "MMM d, yyyy", "MMMM d yyyy", "MMMM d, yyyy", "d MMM yyyy",
    };

    public static Dictionary<string, string> Compute(StandardsProfile profile, IReadOnlyDictionary<string, string> answers, List<string>? errors = null)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, v) in answers)
            values[Tokens.NormalizeKey(k)] = (v ?? "").Trim();

        // Dates.
        int? year = null;
        foreach (var f in profile.Fields.Where(f => !string.IsNullOrWhiteSpace(f.Format)))
        {
            if (!values.TryGetValue(f.Key, out var raw) || raw.Length == 0) continue;
            if (TryParseDate(raw, out var date))
            {
                values[f.Key] = date.ToString(f.Format, CultureInfo.InvariantCulture);
                year ??= date.Year;
            }
            else
            {
                errors?.Add($"\"{f.Label}\" must be a date, e.g. {DateTime.Today.ToString(f.Format, CultureInfo.InvariantCulture)}.");
            }
        }
        values.TryAdd("YEAR", (year ?? DateTime.Today.Year).ToString(CultureInfo.InvariantCulture));

        // Two-line versions of every answer.
        foreach (var f in profile.Fields)
        {
            var value = values.GetValueOrDefault(f.Key, "");
            var (line1, line2) = f.Key == "ADDRESS" ? SplitAddress(value) : Wrap(value, profile.WrapLength);
            values.TryAdd(f.Key + "_1", line1);
            values.TryAdd(f.Key + "_2", line2);
        }

        // Title block lines (may refer to everything above, but not to each other).
        var answerKeys = new HashSet<string>(profile.Fields.Select(f => f.Key), StringComparer.OrdinalIgnoreCase);
        var resolved = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase);
        foreach (var d in profile.TitleBlockFields.Where(d => !string.IsNullOrWhiteSpace(d.Key)))
        {
            if (answerKeys.Contains(d.Key))
            {
                errors?.Add($"Title block line {d.Key} has the same name as a question.");
                continue;
            }
            resolved[d.Key] = Tokens.Resolve(d.Value, values, profile.TokenPrefix, profile.TokenSuffix).Trim();
        }
        return resolved;
    }

    public static bool TryParseDate(string text, out DateTime date) =>
        DateTime.TryParseExact(text.Trim(), DateInputFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date)
        || DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out date);

    /// <summary>
    /// Breaks between words so line 1 is at most <paramref name="max"/> characters; everything else goes on line 2.
    /// A single word longer than the limit stays whole on line 1.
    /// </summary>
    public static (string Line1, string Line2) Wrap(string text, int max)
    {
        text = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (max <= 0 || text.Length <= max) return (text, "");

        var cut = text.LastIndexOf(' ', max);
        if (cut <= 0) cut = text.IndexOf(' ');
        if (cut <= 0) return (text, "");
        return (text[..cut].TrimEnd(), text[(cut + 1)..].TrimStart());
    }

    /// <summary>"12 Oak St, Springfield, IL 62704" → ("12 Oak St", "Springfield, IL 62704"). A line break also works as the split.</summary>
    public static (string Street, string CityStateZip) SplitAddress(string address)
    {
        var text = address.Replace("\r\n", "\n").Trim();
        var i = text.IndexOf('\n');
        if (i < 0) i = text.IndexOf(',');
        if (i < 0) return (text, "");
        return (text[..i].Trim().TrimEnd(','), text[(i + 1)..].Replace('\n', ' ').Trim());
    }
}
