using SetArch.Core.Models;
using SetArch.Core.Standards;

namespace SetArch.Core.Planning;

/// <summary>Builds a fully resolved <see cref="SheetPlan"/> from the wizard answers.</summary>
public static class SheetPlanBuilder
{
    private static readonly char[] InvalidLayoutChars = "<>/\\\":;?*|,=`".ToCharArray();

    public static List<SheetDefinition> EffectiveSheetList(ProjectAnswers a)
    {
        var paper = PaperSizes.Get(a.Format.PaperSizeId);
        return a.SheetList.Count > 0 ? a.SheetList : SheetListGenerator.Generate(a.Sheets, paper.Units);
    }

    /// <summary>Problems that should stop the user from pressing Create.</summary>
    public static List<string> Validate(ProjectAnswers a)
    {
        var errors = new List<string>();
        if (PaperSizes.All.All(p => !string.Equals(p.Id, a.Format.PaperSizeId, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"Unknown paper size '{a.Format.PaperSizeId}'.");
            return errors;
        }

        var sheets = EffectiveSheetList(a);
        if (sheets.Count == 0) errors.Add("The sheet list is empty. Add at least one sheet.");

        foreach (var blank in sheets.Where(s => string.IsNullOrWhiteSpace(s.Number)))
            errors.Add($"Sheet '{blank.Title}' has no sheet number.");

        foreach (var dup in sheets.Where(s => !string.IsNullOrWhiteSpace(s.Number))
                                  .GroupBy(s => s.Number.Trim(), StringComparer.OrdinalIgnoreCase)
                                  .Where(g => g.Count() > 1))
            errors.Add($"Sheet number '{dup.Key}' is used {dup.Count()} times.");

        if (a.TitleBlock.Mode == TitleBlockMode.CustomBlock)
        {
            if (string.IsNullOrWhiteSpace(a.TitleBlock.CustomBlockPath))
                errors.Add("Choose your title block DWG, or switch to the generated title block.");
            else if (!a.TitleBlock.CustomBlockPath.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                errors.Add("The custom title block must be a .dwg file.");
        }

        return errors;
    }

    public static SheetPlan Build(ProjectAnswers a)
    {
        var errors = Validate(a);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));

        var paper = PaperSizes.Get(a.Format.PaperSizeId);
        var landscape = a.Format.Orientation == Orientation.Landscape;
        var width = landscape ? paper.LongSide : paper.ShortSide;
        var height = landscape ? paper.ShortSide : paper.LongSide;

        var defs = EffectiveSheetList(a);
        var geometry = new SheetGeometry(width, height, paper.Units, a.TitleBlock);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sheets = new List<SheetLayout>();
        for (var i = 0; i < defs.Count; i++)
        {
            var def = defs[i];
            var scale = Scales.Find(def.ScaleId);
            var scaleText = scale?.Name ?? (def.Kind is SheetKind.Cover or SheetKind.GeneralNotes or SheetKind.Schedule ? "" : "AS NOTED");

            var sheet = new SheetLayout
            {
                Number = def.Number.Trim(),
                Title = def.Title.Trim(),
                Kind = def.Kind,
                LayoutName = UniqueLayoutName(def, a.Standards.LayoutNaming, usedNames),
            };

            var fields = TitleBlockFields.Values(a.Project, sheet.Number, sheet.Title.ToUpperInvariant(), scaleText, i + 1, defs.Count);

            if (a.TitleBlock.Mode == TitleBlockMode.CustomBlock)
            {
                var path = a.TitleBlock.CustomBlockPath;
                sheet.TitleBlockInsert = new BlockInsertSpec(path, BlockNameFromPath(path), new Point2(0, 0), fields, LayerStandards.TitleBlock);
            }
            else
            {
                geometry.AddGeneratedTitleBlock(sheet, a.Project, scaleText, i + 1, defs.Count);
            }

            var area = geometry.DrawingArea;
            if (def.Kind == SheetKind.Cover)
            {
                geometry.AddCoverContent(sheet, a.Project, defs);
            }
            else if (scale is not null)
            {
                if (a.Standards.AddViewTitles)
                    area = geometry.AddViewTitle(sheet, area, sheet.Title, scale.Name);

                var pad = geometry.Mm(6);
                var vpW = area.Width - 2 * pad;
                var vpH = area.Height - 2 * pad;
                if (vpW > 0 && vpH > 0)
                {
                    sheet.Viewport = new ViewportSpec(area.Center, vpW, vpH, scale, LayerStandards.Viewports, a.Standards.LockViewports);
                }
            }

            sheets.Add(sheet);
        }

        return new SheetPlan
        {
            Project = a.Project,
            Paper = paper,
            Orientation = a.Format.Orientation,
            Width = width,
            Height = height,
            PlotDevice = string.IsNullOrWhiteSpace(a.Format.PlotDevice) ? "DWG To PDF.pc3" : a.Format.PlotDevice.Trim(),
            PlotStyleTable = a.Format.PlotStyleTable?.Trim() ?? "",
            Layers = LayerStandards.For(a.Standards.Layers),
            TextStyles = LayerStandards.TextStyles(a.Standards.CreateTextStyles),
            Sheets = sheets,
            DeleteDefaultLayouts = a.Standards.DeleteDefaultLayouts,
        };
    }

    public static string BlockNameFromPath(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
        var clean = new string(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '$' ? c : '_').ToArray());
        return string.IsNullOrEmpty(clean) ? "SA_TITLEBLOCK" : clean;
    }

    private static string UniqueLayoutName(SheetDefinition def, LayoutNaming naming, HashSet<string> used)
    {
        var raw = naming == LayoutNaming.NumberOnly || string.IsNullOrWhiteSpace(def.Title)
            ? def.Number.Trim()
            : $"{def.Number.Trim()} {def.Title.Trim()}";

        var clean = new string(raw.Select(c => Array.IndexOf(InvalidLayoutChars, c) >= 0 ? '-' : c).ToArray()).Trim();
        if (clean.Length > 200) clean = clean[..200];
        if (clean.Length == 0) clean = "Sheet";
        if (string.Equals(clean, "Model", StringComparison.OrdinalIgnoreCase)) clean = "Model sheet";

        var name = clean;
        for (var n = 2; !used.Add(name); n++) name = $"{clean} ({n})";
        return name;
    }
}

internal readonly record struct PaperRect(double X1, double Y1, double X2, double Y2)
{
    public double Width => X2 - X1;
    public double Height => Y2 - Y1;
    public Point2 Center => new((X1 + X2) / 2, (Y1 + Y2) / 2);
}
