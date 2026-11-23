using SetArch.Core.Models;

namespace SetArch.Core.Planning;

public sealed class CreateResult
{
    public int FoldersCreated { get; set; }
    public int FilesCopied { get; set; }
    public List<string> Skipped { get; } = new();

    /// <summary>Copied drawings whose placeholders still need filling.</summary>
    public List<string> DrawingsToFill { get; } = new();
}

public static class ProjectCreator
{
    /// <summary>Creates the folders and copies the template files. Never overwrites anything.</summary>
    public static CreateResult Create(ProjectPlan plan, Action<string>? log = null)
    {
        if (!plan.IsValid) throw new InvalidOperationException(string.Join(Environment.NewLine, plan.Errors));
        var result = new CreateResult();

        foreach (var dir in new[] { plan.ProjectFolder }.Concat(plan.Folders))
        {
            if (Directory.Exists(dir)) continue;
            Directory.CreateDirectory(dir);
            result.FoldersCreated++;
        }
        log?.Invoke($"Folders: {result.FoldersCreated} created in {plan.ProjectFolder}");

        foreach (var f in plan.Files)
        {
            var shortName = Path.GetRelativePath(plan.ProjectFolder, f.DestinationPath);
            if (File.Exists(f.DestinationPath))
            {
                result.Skipped.Add(shortName);
                log?.Invoke($"Skipped (already exists): {shortName}");
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(f.DestinationPath)!);
            File.Copy(f.SourcePath, f.DestinationPath, overwrite: false);
            // Templates are often kept read-only; the copy must be writable so its text can be filled in.
            var attrs = File.GetAttributes(f.DestinationPath);
            if (attrs.HasFlag(FileAttributes.ReadOnly))
                File.SetAttributes(f.DestinationPath, attrs & ~FileAttributes.ReadOnly);

            result.FilesCopied++;
            log?.Invoke($"Copied: {shortName}");
            if (f.FillText) result.DrawingsToFill.Add(f.DestinationPath);
        }

        return result;
    }
}

public static class FolderImporter
{
    /// <summary>
    /// Reads an existing folder (e.g. the office's blank project template) into a folder tree, and optionally
    /// adds every file found in it as a template that goes in the same place.
    /// </summary>
    public static void Import(StandardsProfile profile, string folder, bool includeFiles)
    {
        var root = new FolderNode { Name = profile.Root.Name };
        AddChildren(root, folder);
        profile.Root = root;

        if (!includeFiles) return;
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("~$") || name.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                continue;
            if (profile.Files.Any(t => string.Equals(t.SourcePath, file, StringComparison.OrdinalIgnoreCase)))
                continue;

            var rel = Path.GetRelativePath(folder, Path.GetDirectoryName(file)!);
            profile.Files.Add(new TemplateFile
            {
                SourcePath = file,
                Folder = rel == "." ? "" : rel.Replace('\\', '/'),
                FileName = DefaultSaveAs(file),
                FillText = ProjectPlanner.IsDrawing(file) || file.EndsWith(".dwt", StringComparison.OrdinalIgnoreCase),
            });
        }
    }

    /// <summary>Suggested save-as name: the source name, with a .dwt template saved as a .dwg.</summary>
    public static string DefaultSaveAs(string sourcePath)
    {
        var name = Path.GetFileName(sourcePath);
        return name.EndsWith(".dwt", StringComparison.OrdinalIgnoreCase) ? Path.ChangeExtension(name, ".dwg") : name;
    }

    private static void AddChildren(FolderNode node, string dir)
    {
        foreach (var sub in Directory.EnumerateDirectories(dir).Order(StringComparer.OrdinalIgnoreCase))
        {
            var name = Path.GetFileName(sub);
            if (name.StartsWith('.')) continue;
            AddChildren(node.Add(name), sub);
        }
    }

    /// <summary>Renames a folder and updates every template that points into it.</summary>
    public static void Rename(StandardsProfile profile, FolderNode node, string newName)
    {
        var oldPath = node.RelativePath;
        node.Name = newName;
        var newPath = node.RelativePath;
        if (oldPath.Length == 0) return;
        foreach (var t in profile.Files)
        {
            if (string.Equals(t.Folder, oldPath, StringComparison.OrdinalIgnoreCase))
                t.Folder = newPath;
            else if (t.Folder.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase))
                t.Folder = newPath + t.Folder[oldPath.Length..];
        }
    }
}
