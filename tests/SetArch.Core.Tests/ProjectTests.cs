using SetArch.Core.Models;
using SetArch.Core.Output;
using SetArch.Core.Planning;
using Xunit;

namespace SetArch.Core.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("setarch-test-").FullName;
    public string Sub(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());
    public string File(string relative, string content = "x")
    {
        var p = Sub(relative.Split('/'));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
        System.IO.File.WriteAllText(p, content);
        return p;
    }
    public void Dispose() { try { Directory.Delete(Path, true); } catch (IOException) { } }
}

public class ProjectTests
{
    private static Dictionary<string, string> Values() => new()
    {
        ["PROJECT_NAME"] = "Smith Residence",
        ["PROJECT_NUMBER"] = "2026-014",
        ["ADDRESS"] = "12 Oak St, Springfield",
        ["PROJECT_TYPE"] = "Single-Family Residential",
        ["DATE"] = "11/23/2026",
        ["OCCUPANCY_TYPE"] = "R-3",
        ["AREA"] = "2,450 SF",
    };

    [Fact]
    public void Tokens_resolve_case_insensitively_and_leave_unknown()
    {
        var v = Values();
        Assert.Equal("2026-014 - Smith Residence", Tokens.Resolve("[[PROJECT_NUMBER]] - [[project name]]", v, "[[", "]]"));
        Assert.Equal("[[CLIENT]] R-3", Tokens.Resolve("[[CLIENT]] [[ OCCUPANCY_TYPE ]]", v, "[[", "]]"));
        Assert.Equal(new[] { "CLIENT" }, Tokens.Unknown("[[CLIENT]] [[AREA]]", v, "[[", "]]"));
    }

    [Theory]
    [InlineData("A/B: C?", "A-B- C-")]
    [InlineData("  name.  ", "name")]
    [InlineData("CON", "_CON")]
    public void Names_are_made_safe(string input, string expected) => Assert.Equal(expected, Tokens.SafeName(input));

    [Fact]
    public void Plan_creates_tree_and_places_files()
    {
        using var t = new TempDir();
        var cover = t.File("std/Cover.dwg");
        var fee = t.File("std/Fee.xlsx");
        var commercial = t.File("std/Commercial Notes.dwg");

        var p = StandardsProfile.CreateDefault();
        p.Files.Add(new TemplateFile { SourcePath = cover, Folder = "03 CAD/Sheets", FileName = "[[PROJECT_NUMBER]] A-000 Cover" });
        p.Files.Add(new TemplateFile { SourcePath = fee, Folder = "01 Admin" });
        p.Files.Add(new TemplateFile { SourcePath = commercial, Folder = "03 CAD", OnlyForProjectType = "Commercial" });

        var plan = ProjectPlanner.Plan(p, Values(), t.Sub("Projects"));
        Assert.Contains(plan.Errors, e => e.Contains("doesn't exist"));

        Directory.CreateDirectory(t.Sub("Projects"));
        plan = ProjectPlanner.Plan(p, Values(), t.Sub("Projects"));
        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
        Assert.Equal(t.Sub("Projects", "2026-014 - Smith Residence"), plan.ProjectFolder);
        Assert.Contains(t.Sub("Projects", "2026-014 - Smith Residence", "03 CAD", "Sheets"), plan.Folders);

        Assert.Equal(2, plan.Files.Count); // commercial-only file skipped for a residence
        var c = plan.Files.Single(f => f.SourcePath == cover);
        Assert.Equal(t.Sub("Projects", "2026-014 - Smith Residence", "03 CAD", "Sheets", "2026-014 A-000 Cover.dwg"), c.DestinationPath);
        Assert.True(c.FillText);
        Assert.False(plan.Files.Single(f => f.SourcePath == fee).FillText); // not a drawing

        var sheets = plan.Preview!.Children.Single(n => n.Name == "03 CAD").Children.Single(n => n.Name == "Sheets");
        Assert.Contains(sheets.Children, n => n.IsFile && n.Name == "2026-014 A-000 Cover.dwg");
    }

    [Fact]
    public void Plan_reports_missing_values_sources_and_folders()
    {
        using var t = new TempDir();
        var p = StandardsProfile.CreateDefault();
        p.Files.Add(new TemplateFile { SourcePath = t.Sub("missing.dwg"), Folder = "03 CAD" });
        p.Files.Add(new TemplateFile { SourcePath = t.File("a.dwg"), Folder = "99 Gone" });
        var v = Values();
        v["PROJECT_NAME"] = " ";

        var plan = ProjectPlanner.Plan(p, v, t.Path);
        Assert.Contains(plan.Errors, e => e.Contains("Project name"));
        Assert.Contains(plan.Errors, e => e.Contains("not found"));
        Assert.Contains(plan.Errors, e => e.Contains("99 Gone"));
    }

    [Fact]
    public void Create_copies_without_overwriting()
    {
        using var t = new TempDir();
        var src = t.File("std/Title.dwg", "template");
        var p = StandardsProfile.CreateDefault();
        p.Files.Add(new TemplateFile { SourcePath = src, Folder = "03 CAD/Sheets" });
        File.SetAttributes(src, FileAttributes.ReadOnly);

        var plan = ProjectPlanner.Plan(p, Values(), t.Path);
        var r = ProjectCreator.Create(plan);
        Assert.Equal(1, r.FilesCopied);
        Assert.Single(r.DrawingsToFill);
        Assert.True(r.FoldersCreated > 10);
        var dest = plan.Files[0].DestinationPath;
        Assert.False(File.GetAttributes(dest).HasFlag(FileAttributes.ReadOnly));

        File.WriteAllText(dest, "user edits");
        var again = ProjectCreator.Create(ProjectPlanner.Plan(p, Values(), t.Path));
        Assert.Equal(0, again.FilesCopied);
        Assert.Single(again.Skipped);
        Assert.Equal("user edits", File.ReadAllText(dest));
        File.SetAttributes(src, FileAttributes.Normal);
    }

    [Fact]
    public void Import_reads_folder_tree_and_files()
    {
        using var t = new TempDir();
        Directory.CreateDirectory(t.Sub("tpl", "A", "A1"));
        Directory.CreateDirectory(t.Sub("tpl", "B"));
        t.File("tpl/A/A1/Title.dwt");
        t.File("tpl/B/Letter.docx");
        t.File("tpl/B/~$Letter.docx");

        var p = StandardsProfile.CreateDefault();
        FolderImporter.Import(p, t.Sub("tpl"), includeFiles: true);

        Assert.Equal(new[] { "A", "B" }, p.Root.Children.Select(c => c.Name));
        Assert.Equal("A/A1", p.Root.Children[0].Children[0].RelativePath);
        Assert.Equal(2, p.Files.Count);
        var title = p.Files.Single(f => f.SourcePath.EndsWith("Title.dwt"));
        Assert.Equal("A/A1", title.Folder);
        Assert.Equal("Title.dwg", title.FileName);
        Assert.True(title.FillText);
    }

    [Fact]
    public void Renaming_a_folder_updates_templates()
    {
        var p = StandardsProfile.CreateDefault();
        p.Files.Add(new TemplateFile { SourcePath = "x.dwg", Folder = "03 CAD/Sheets" });
        p.Files.Add(new TemplateFile { SourcePath = "y.dwg", Folder = "03 CAD" });
        p.Files.Add(new TemplateFile { SourcePath = "z.dwg", Folder = "03 CADD" });
        var cad = p.Root.Children.Single(c => c.Name == "03 CAD");
        FolderImporter.Rename(p, cad, "03 Drawings");
        Assert.Equal(new[] { "03 Drawings/Sheets", "03 Drawings", "03 CADD" }, p.Files.Select(f => f.Folder));
    }

    [Fact]
    public void Standards_round_trip_through_json()
    {
        using var t = new TempDir();
        var p = StandardsProfile.CreateDefault();
        p.Files.Add(new TemplateFile { SourcePath = @"C:\std\Cover.dwg", Folder = "03 CAD/Sheets", FileName = "[[PROJECT_NUMBER]] Cover.dwg" });
        StandardsStore.Save(p, t.Sub("s.json"));

        var loaded = StandardsStore.Load(t.Sub("s.json"));
        Assert.Equal(p.Fields.Select(f => f.Key), loaded.Fields.Select(f => f.Key));
        Assert.Equal("03 CAD/Sheets", loaded.Root.Children.Single(c => c.Name == "03 CAD").Children[0].RelativePath);
        Assert.Equal("[[PROJECT_NUMBER]] Cover.dwg", loaded.Files[0].FileName);
        Assert.Contains("R-3", loaded.Fields.Single(f => f.Key == "OCCUPANCY_TYPE").OptionList);
    }
}

public class FillScriptTests
{
    [Fact]
    public void Values_are_escaped_for_lisp_mtext_and_unicode()
    {
        var v = new Dictionary<string, string> { ["PROJECT_NAME"] = "Café \"Le Bistro\" {A\\B}", ["ADDRESS"] = "1 Main St\n2nd floor" };
        var lisp = FillScript.Lisp(v, "[[", "]]");

        Assert.Contains("\"[[PROJECT_NAME]]\"", lisp);
        // plain: é → \U+00E9, quotes escaped
        Assert.Contains("\"Caf\\\\U+00E9 \\\"Le Bistro\\\" {A\\\\B}\"", lisp);
        // mtext: braces and backslash escaped too
        Assert.Contains("\"Caf\\\\U+00E9 \\\"Le Bistro\\\" \\\\{A\\\\\\\\B\\\\}\"", lisp);
        Assert.Contains("\"1 Main St, 2nd floor\"", lisp);
        Assert.Contains("\"1 Main St\\\\P2nd floor\"", lisp);
        Assert.Contains("(cons \"PROJECTNAME\"", lisp);
        Assert.All(lisp, c => Assert.True(c < 128));
        Assert.Equal(0, ParenBalance(lisp));
    }

    [Fact]
    public void Console_script_has_no_blank_lines_and_saves()
    {
        var script = FillScript.ConsoleScript(new Dictionary<string, string> { ["AREA"] = "100 m2" }, "[[", "]]");
        Assert.DoesNotContain("\n\n", script);
        Assert.EndsWith("(setarch:fill)\n_.QSAVE\n", script);
        Assert.Equal(0, ParenBalance(script.Replace("_.QSAVE", "")));
    }

    [Fact]
    public void Lt_lisp_defines_command()
    {
        var lisp = FillScript.LtLisp(new Dictionary<string, string> { ["AREA"] = "1" }, "[[", "]]");
        Assert.Contains("(defun c:SETARCHFILL", lisp);
        Assert.Equal(0, ParenBalance(lisp));
    }

    [Theory]
    [InlineData("Command: \0S\0E\0T\0A\0R\0C\0H\0-\0R\0E\0S\0U\0L\0T\0 \03\0", true, 3)]
    [InlineData("SETARCH-RESULT 0", true, 0)]
    [InlineData("Error: file is read-only", false, 0)]
    public void Console_output_is_interpreted(string output, bool ok, int changes)
    {
        var r = AutoCadConsole.Interpret("a.dwg", output.Replace("\0", ""), 0);
        Assert.Equal(ok, r.Success);
        Assert.Equal(changes, r.Changes);
    }

    internal static int ParenBalance(string lisp)
    {
        int depth = 0;
        bool inString = false, inComment = false;
        for (int i = 0; i < lisp.Length; i++)
        {
            var c = lisp[i];
            if (inComment) { if (c == '\n') inComment = false; continue; }
            if (inString)
            {
                if (c == '\\') i++;
                else if (c == '"') inString = false;
                continue;
            }
            if (c == ';') inComment = true;
            else if (c == '"') inString = true;
            else if (c == '(') depth++;
            else if (c == ')') { depth--; Assert.True(depth >= 0, $"Unbalanced ')' at {i}"); }
        }
        Assert.False(inString, "Unterminated string");
        return depth;
    }
}

public class TitleBlockValueTests
{
    private static Dictionary<string, string> Answers(string name, string address, string date = "3/7/2026") => new()
    {
        ["PROJECT_TYPE"] = "Single-Family Residential",
        ["PROJECT_NAME"] = name,
        ["ADDRESS"] = address,
        ["PROJECT_NUMBER"] = "2026-014",
        ["DATE"] = date,
    };

    [Fact]
    public void Default_title_block_lines_are_filled()
    {
        var p = StandardsProfile.CreateDefault();
        var v = ProjectValues.Compute(p, Answers("The Smith Family Lake House Renovation", "1234 Lakeshore Drive, Springfield, IL 62704"));

        Assert.Equal("Single-Family Residential", v["LINE1"]);
        Assert.Equal("The Smith Family Lake", v["LINE2"]);
        Assert.Equal("House Renovation", v["LINE3"]);
        Assert.Equal("1234 Lakeshore Drive", v["LINE4"]);
        Assert.Equal("Springfield, IL 62704", v["LINE5"]);
        Assert.Equal("2026-014", v["JOB_NO"]);
        Assert.Equal("03/07/2026", v["DATE"]);
        Assert.Equal("2026", v["COPYRIGHT"]);
    }

    [Fact]
    public void Short_name_leaves_line_3_empty()
    {
        var v = ProjectValues.Compute(StandardsProfile.CreateDefault(), Answers("Smith Residence", "12 Oak St"));
        Assert.Equal("Smith Residence", v["LINE2"]);
        Assert.Equal("", v["LINE3"]);
        Assert.Equal("12 Oak St", v["LINE4"]);
        Assert.Equal("", v["LINE5"]);
    }

    [Theory]
    [InlineData("2026-03-07")]
    [InlineData("03/07/2026")]
    [InlineData("3-7-2026")]
    [InlineData("March 7, 2026")]
    public void Dates_are_written_mm_dd_yyyy(string typed)
    {
        var v = ProjectValues.Compute(StandardsProfile.CreateDefault(), Answers("A", "B", typed));
        Assert.Equal("03/07/2026", v["DATE"]);
    }

    [Fact]
    public void Copyright_year_follows_the_project_date()
    {
        var v = ProjectValues.Compute(StandardsProfile.CreateDefault(), Answers("A", "B", "12/31/2025"));
        Assert.Equal("2025", v["COPYRIGHT"]);
    }

    [Fact]
    public void Bad_date_is_an_error()
    {
        var errors = new List<string>();
        ProjectValues.Compute(StandardsProfile.CreateDefault(), Answers("A", "B", "next tuesday"), errors);
        Assert.Contains(errors, e => e.Contains("must be a date"));
    }

    [Theory]
    [InlineData("Smith Residence", 25, "Smith Residence", "")]
    [InlineData("Supercalifragilisticexpialidocious Hall", 10, "Supercalifragilisticexpialidocious", "Hall")]
    [InlineData("A  B   C", 3, "A B", "C")]
    public void Wrap_breaks_between_words(string text, int max, string l1, string l2) =>
        Assert.Equal((l1, l2), ProjectValues.Wrap(text, max));

    [Fact]
    public void Title_block_lines_reach_the_fill_script()
    {
        using var t = new TempDir();
        var p = StandardsProfile.CreateDefault();
        var plan = ProjectPlanner.Plan(p, Answers("Smith Residence", "12 Oak St, Springfield, IL 62704"), t.Path);
        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
        var lisp = FillScript.Lisp(plan.Values, "[[", "]]");
        Assert.Contains("(cons \"LINE5\" \"Springfield, IL 62704\")", lisp);
        Assert.Contains("(cons \"JOBNO\" \"2026-014\")", lisp);
        Assert.Contains("(cons \"COPYRIGHT\" \"2026\")", lisp);
    }

    [Fact]
    public void Standards_saved_by_the_previous_version_get_the_title_block()
    {
        using var t = new TempDir();
        var path = t.File("old.json", """
            { "Name": "Office", "TokenPrefix": "[[", "TokenSuffix": "]]",
              "Fields": [ { "Key": "DATE", "Label": "Date", "Options": "", "Required": false, "DefaultValue": "{today}" } ],
              "Root": { "Name": "[[PROJECT_NUMBER]]", "Children": [] }, "Files": [], "AutoCadConsolePath": "" }
            """);
        var p = StandardsStore.Load(path);
        Assert.Equal(StandardsProfile.CurrentVersion, p.Version);
        Assert.Equal("MM/dd/yyyy", p.Fields[0].Format);
        Assert.Contains(p.TitleBlockFields, d => d.Key == "LINE3");
    }
}
