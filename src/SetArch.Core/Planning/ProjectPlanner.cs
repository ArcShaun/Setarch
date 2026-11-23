using System.Globalization;
using SetArch.Core.Models;

namespace SetArch.Core.Planning;

public sealed record PlannedFile(TemplateFile Template, string SourcePath, string DestinationPath, bool FillText);

/// <summary>A node of the "what will be created" preview tree.</summary>
public sealed class PreviewNode
{
    public required string Name { get; init; }
    public bool IsFile { get; init; }
    public bool Fill { get; init; }
    public List<PreviewNode> Children { get; } = new();
}

public sealed class ProjectPlan
{
    public required string ProjectFolder { get; init; }
    public required IReadOnlyDictionary<string, string> Values { get; init; }
    public required string TokenPrefix { get; init; }
    public required string TokenSuffix { get; init; }
    public List<string> Folders { get; } = new();
    public List<PlannedFile> Files { get; } = new();

    /// <summary>Problems that stop creation.</summary>
    public List<string> Errors { get; } = new();

    /// <summary>Things the user should know but that don't stop creation.</summary>
    public List<string> Warnings { get; } = new();

    public PreviewNode? Preview { get; set; }
    public bool IsValid => Errors.Count == 0;
}

public static class ProjectPlanner
{
    public static string DefaultValue(FieldDefinition f) =>
        f.DefaultValue.Replace("{today}", DateTime.Today.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Works out everything that will be created, without touching the disk (apart from checking that sources exist).
    /// </summary>
    /// <param name="values">Field key → value, as typed by the user.</param>
    public static ProjectPlan Plan(StandardsProfile profile, IReadOnlyDictionary<string, string> values, string parentFolder)
    {
        var normalized = values.ToDictionary(kv => Tokens.NormalizeKey(kv.Key), kv => (kv.Value ?? "").Trim());
        var prefix = profile.TokenPrefix;
        var suffix = profile.TokenSuffix;
        profile.Root.LinkParents();

        string Resolve(string s) => Tokens.Resolve(s, normalized, prefix, suffix);

        var rootName = Tokens.SafeName(Resolve(profile.Root.Name));
        var plan = new ProjectPlan
        {
            ProjectFolder = string.IsNullOrWhiteSpace(parentFolder) ? rootName : Path.Combine(parentFolder, rootName),
            Values = normalized,
            TokenPrefix = prefix,
            TokenSuffix = suffix,
        };

        foreach (var f in profile.Fields.Where(f => f.Required))
            if (!normalized.TryGetValue(f.Key, out var v) || string.IsNullOrWhiteSpace(v))
                plan.Errors.Add($"Please fill in \"{f.Label}\".");

        if (string.IsNullOrWhiteSpace(parentFolder))
            plan.Errors.Add("Choose where to create the project folder.");
        else if (!Directory.Exists(parentFolder))
            plan.Errors.Add($"The folder \"{parentFolder}\" doesn't exist.");

        foreach (var key in Tokens.Unknown(profile.Root.Name, normalized, prefix, suffix).Distinct())
            plan.Warnings.Add($"The project folder name uses {prefix}{key}{suffix}, which isn't one of your fields.");

        if (Directory.Exists(plan.ProjectFolder))
            plan.Warnings.Add("The project folder already exists. Missing folders and files will be added; nothing will be overwritten.");

        // Folders: map every tree path (unresolved) to its real path.
        var folderMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var previewByPath = new Dictionary<string, PreviewNode>(StringComparer.OrdinalIgnoreCase);
        plan.Preview = new PreviewNode { Name = rootName };
        folderMap[""] = plan.ProjectFolder;
        previewByPath[""] = plan.Preview;

        foreach (var node in profile.Root.SelfAndDescendants().Skip(1))
        {
            var parentPath = node.Parent!.RelativePath;
            if (!folderMap.TryGetValue(parentPath, out var parentReal)) continue;

            var name = Tokens.SafeName(Resolve(node.Name));
            var real = Path.Combine(parentReal, name);
            if (!folderMap.TryAdd(node.RelativePath, real))
            {
                plan.Warnings.Add($"The folder \"{node.RelativePath}\" appears twice in the folder tree.");
                continue;
            }
            plan.Folders.Add(real);

            var preview = new PreviewNode { Name = name };
            previewByPath[parentPath].Children.Add(preview);
            previewByPath[node.RelativePath] = preview;
        }

        // Files.
        var projectType = normalized.GetValueOrDefault("PROJECT_TYPE", "");
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in profile.Files)
        {
            if (string.IsNullOrWhiteSpace(t.SourcePath)) continue;
            if (!string.IsNullOrWhiteSpace(t.OnlyForProjectType) &&
                !string.Equals(t.OnlyForProjectType.Trim(), projectType, StringComparison.OrdinalIgnoreCase))
                continue;

            var label = Path.GetFileName(t.SourcePath);
            var folderKey = (t.Folder ?? "").Trim().Trim('/').Replace('\\', '/');
            if (!folderMap.TryGetValue(folderKey, out var folderReal))
            {
                plan.Errors.Add($"\"{label}\" goes in \"{folderKey}\", which is no longer in the folder tree.");
                continue;
            }

            if (!File.Exists(t.SourcePath))
            {
                plan.Errors.Add($"Template file not found: {t.SourcePath}");
                continue;
            }

            var name = string.IsNullOrWhiteSpace(t.FileName) ? Path.GetFileName(t.SourcePath) : Resolve(t.FileName);
            name = Tokens.SafeName(name);
            if (string.IsNullOrEmpty(Path.GetExtension(name)))
                name += Path.GetExtension(t.SourcePath);

            var dest = Path.Combine(folderReal, name);
            if (!destinations.Add(dest))
            {
                plan.Warnings.Add($"Two templates would both be saved as \"{name}\"; only the first is copied.");
                continue;
            }

            foreach (var key in Tokens.Unknown(t.FileName, normalized, prefix, suffix).Distinct())
                plan.Warnings.Add($"\"{t.FileName}\" uses {prefix}{key}{suffix}, which isn't one of your fields.");

            var fill = t.FillText && IsDrawing(dest);
            plan.Files.Add(new PlannedFile(t, t.SourcePath, dest, fill));
            previewByPath[folderKey].Children.Add(new PreviewNode { Name = name, IsFile = true, Fill = fill });
        }

        return plan;
    }

    public static bool IsDrawing(string path) =>
        string.Equals(Path.GetExtension(path), ".dwg", StringComparison.OrdinalIgnoreCase);
}
