using System.Text.Json;

namespace SetArch.Core.Models;

/// <summary>Reads and writes standards profiles as JSON.</summary>
public static class StandardsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SetArch", "standards.json");

    public static void Save(StandardsProfile profile, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(profile, Json));
        File.Move(tmp, path, overwrite: true);
    }

    public static StandardsProfile Load(string path)
    {
        var profile = JsonSerializer.Deserialize<StandardsProfile>(File.ReadAllText(path), Json)
                      ?? throw new InvalidDataException("This file doesn't contain SetArch standards.");
        profile.Root ??= new FolderNode { Name = "[[PROJECT_NUMBER]] - [[PROJECT_NAME]]" };
        profile.Root.LinkParents();
        return profile;
    }

    /// <summary>Loads the user's saved standards, or the built-in example if there are none yet.</summary>
    public static StandardsProfile LoadOrDefault(string path)
    {
        try
        {
            if (File.Exists(path)) return Load(path);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
        {
            // fall through to defaults; the broken file is left alone
        }
        return StandardsProfile.CreateDefault();
    }
}
