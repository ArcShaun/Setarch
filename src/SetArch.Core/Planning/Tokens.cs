using System.Text;
using System.Text.RegularExpressions;

namespace SetArch.Core.Planning;

public static class Tokens
{
    private static readonly char[] InvalidNameChars = "<>:\"/\\|?*".ToCharArray();
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>Replaces every prefix+KEY+suffix in <paramref name="text"/> (keys are case-insensitive).</summary>
    public static string Resolve(string text, IReadOnlyDictionary<string, string> values, string prefix, string suffix)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(prefix) || !text.Contains(prefix, StringComparison.OrdinalIgnoreCase))
            return text;

        var pattern = Regex.Escape(prefix) + @"\s*([A-Za-z0-9_ ]+?)\s*" + Regex.Escape(suffix);
        return Regex.Replace(text, pattern, m =>
        {
            var key = NormalizeKey(m.Groups[1].Value);
            return values.TryGetValue(key, out var v) ? v : m.Value;
        });
    }

    /// <summary>Keys that appear in <paramref name="text"/> but have no value.</summary>
    public static IEnumerable<string> Unknown(string text, IReadOnlyDictionary<string, string> values, string prefix, string suffix)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(prefix)) yield break;
        var pattern = Regex.Escape(prefix) + @"\s*([A-Za-z0-9_ ]+?)\s*" + Regex.Escape(suffix);
        foreach (Match m in Regex.Matches(text, pattern))
        {
            var key = NormalizeKey(m.Groups[1].Value);
            if (!values.ContainsKey(key)) yield return key;
        }
    }

    public static string NormalizeKey(string key) => key.Trim().ToUpperInvariant().Replace(' ', '_');

    /// <summary>Normalizes attribute tags for matching: PROJECT_NAME, Project-Name and PROJECTNAME are the same.</summary>
    public static string NormalizeTag(string tag) =>
        new string(tag.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>Makes a single file or folder name valid on Windows.</summary>
    public static string SafeName(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(c < 32 || Array.IndexOf(InvalidNameChars, c) >= 0 ? '-' : c);
        var clean = Regex.Replace(sb.ToString(), @"\s+", " ").Trim().TrimEnd('.', ' ');
        if (clean.Length == 0) clean = "_";
        if (ReservedNames.Contains(Path.GetFileNameWithoutExtension(clean))) clean = "_" + clean;
        return clean.Length > 150 ? clean[..150].TrimEnd('.', ' ') : clean;
    }
}
