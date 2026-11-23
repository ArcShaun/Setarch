using SetArch.Core.Models;
using SetArch.Core.Standards;

namespace SetArch.Core.Planning;

/// <summary>
/// Lays out the border, title block, view titles and cover sheet for a given paper size.
/// Sizes are designed in millimetres for an A1-sized sheet and scaled to the actual paper,
/// then converted to inches for imperial sheets.
/// </summary>
internal sealed class SheetGeometry
{
    private const string Tb = LayerStandards.TitleBlock;
    private const string TbText = LayerStandards.TitleBlockText;

    private readonly double _width;
    private readonly double _height;
    private readonly double _k;      // paper units per mm
    private readonly double _s;      // size factor relative to an A1 sheet
    private readonly TitleBlockOptions _options;

    public PaperRect Border { get; }
    public PaperRect Strip { get; }
    public PaperRect DrawingArea { get; }

    public SheetGeometry(double width, double height, PaperUnits units, TitleBlockOptions options)
    {
        _width = width;
        _height = height;
        _options = options;
        _k = units == PaperUnits.Millimeters ? 1.0 : 1.0 / 25.4;
        var minMm = Math.Min(width, height) / _k;
        _s = Math.Clamp(minMm / 594.0, 0.35, 1.5);

        if (options.Mode == TitleBlockMode.CustomBlock)
        {
            Border = new PaperRect(0, 0, width, height);
            Strip = new PaperRect(width, 0, width, 0);
            DrawingArea = new PaperRect(
                options.CustomMarginLeft,
                options.CustomMarginBottom,
                width - options.CustomMarginRight,
                height - options.CustomMarginTop);
            if (DrawingArea.Width <= 0 || DrawingArea.Height <= 0)
                DrawingArea = new PaperRect(width * 0.05, height * 0.05, width * 0.8, height * 0.95);
            return;
        }

        var left = Mm(Math.Max(10, 20 * _s));
        var other = Mm(Math.Max(5, 10 * _s));
        Border = new PaperRect(left, other, width - other, height - other);

        if (options.Placement == TitleBlockPlacement.RightStrip)
        {
            var sw = Mm(Math.Max(50, 115 * _s));
            Strip = new PaperRect(Border.X2 - sw, Border.Y1, Border.X2, Border.Y2);
            DrawingArea = new PaperRect(Border.X1, Border.Y1, Strip.X1, Border.Y2);
        }
        else
        {
            var sh = Mm(Math.Max(25, 45 * _s));
            Strip = new PaperRect(Border.X1, Border.Y1, Border.X2, Border.Y1 + sh);
            DrawingArea = new PaperRect(Border.X1, Strip.Y2, Border.X2, Border.Y2);
        }
    }

    /// <summary>Converts a design size in mm (already scaled or not) to paper units.</summary>
    public double Mm(double mm) => mm * _k;

    /// <summary>A size designed for A1 in mm, scaled to this paper and converted to paper units.</summary>
    private double S(double mmAtA1) => mmAtA1 * _s * _k;

    public void AddGeneratedTitleBlock(SheetLayout sheet, ProjectInfo p, string scale, int index, int total)
    {
        var e = sheet.Entities;
        e.Add(PolylineEntity.Rectangle(Border.X1, Border.Y1, Border.X2, Border.Y2, Tb));
        e.Add(PolylineEntity.Rectangle(Strip.X1, Strip.Y1, Strip.X2, Strip.Y2, Tb));

        var vertical = _options.Placement == TitleBlockPlacement.RightStrip;
        var rows = new List<Row>();
        if (vertical) rows.Add(new Row(3.0, new Cell("REVISIONS", "")));
        rows.Add(new Row(1.1, new Cell("ARCHITECT", p.FirmName, Emphasis: true)));
        rows.Add(new Row(1.5, new Cell("PROJECT", p.ProjectName, Emphasis: true)));
        rows.Add(new Row(0.8, new Cell("CLIENT", p.Client)));
        rows.Add(new Row(0.8, new Cell("SITE", p.SiteAddress)));
        rows.Add(new Row(1.3, new Cell("SHEET TITLE", sheet.Title.ToUpperInvariant(), Emphasis: true)));
        rows.Add(new Row(0.6, new Cell("STATUS", p.Status)));
        rows.Add(new Row(0.6, new Cell("DRAWN", p.DrawnBy), new Cell("CHECKED", p.CheckedBy)));
        rows.Add(new Row(0.6, new Cell("DATE", p.Date), new Cell("SCALE", scale)));
        rows.Add(new Row(0.6, new Cell("PROJECT NO", p.ProjectNumber), new Cell("REV", p.Revision)));
        rows.Add(new Row(1.6, new Cell($"SHEET {index} OF {total}", sheet.Number, Emphasis: true, Centered: true)));

        var totalWeight = rows.Sum(r => r.Weight);
        var cursor = vertical ? Strip.Y2 : Strip.X1;

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            PaperRect rowRect;
            if (vertical)
            {
                var h = Strip.Height * row.Weight / totalWeight;
                rowRect = new PaperRect(Strip.X1, cursor - h, Strip.X2, cursor);
                cursor -= h;
                if (i < rows.Count - 1) e.Add(new LineEntity(new(Strip.X1, rowRect.Y1), new(Strip.X2, rowRect.Y1), Tb));
            }
            else
            {
                var w = Strip.Width * row.Weight / totalWeight;
                rowRect = new PaperRect(cursor, Strip.Y1, cursor + w, Strip.Y2);
                cursor += w;
                if (i < rows.Count - 1) e.Add(new LineEntity(new(rowRect.X2, Strip.Y1), new(rowRect.X2, Strip.Y2), Tb));
            }

            // Split cells side by side (vertical strip) or stacked (bottom strip).
            var n = row.Cells.Length;
            for (var c = 0; c < n; c++)
            {
                PaperRect cellRect;
                if (vertical)
                {
                    var w = rowRect.Width / n;
                    cellRect = new PaperRect(rowRect.X1 + c * w, rowRect.Y1, rowRect.X1 + (c + 1) * w, rowRect.Y2);
                    if (c > 0) e.Add(new LineEntity(new(cellRect.X1, cellRect.Y1), new(cellRect.X1, cellRect.Y2), Tb));
                }
                else
                {
                    var h = rowRect.Height / n;
                    cellRect = new PaperRect(rowRect.X1, rowRect.Y2 - (c + 1) * h, rowRect.X2, rowRect.Y2 - c * h);
                    if (c > 0) e.Add(new LineEntity(new(cellRect.X1, cellRect.Y2), new(cellRect.X2, cellRect.Y2), Tb));
                }
                AddCell(e, cellRect, row.Cells[c]);
            }
        }
    }

    private void AddCell(List<PaperEntity> e, PaperRect r, Cell cell)
    {
        var pad = S(1.5);
        var labelH = Math.Min(S(1.8), r.Height * 0.22);
        e.Add(new TextEntity(cell.Label, new Point2(r.X1 + pad, r.Y2 - pad - labelH), labelH, TextAlign.BaselineLeft, LayerStandards.TextStyle, TbText));

        if (string.IsNullOrWhiteSpace(cell.Value)) return;

        var desired = cell.Centered ? S(12) : cell.Emphasis ? S(5) : S(3);
        var availableH = r.Height - labelH - 3 * pad;
        var h = FitHeight(cell.Value, desired, r.Width - 2 * pad, availableH * 0.8);
        var centerY = r.Y1 + pad + availableH / 2;
        var style = cell.Emphasis ? LayerStandards.TitleStyle : LayerStandards.TextStyle;

        e.Add(cell.Centered
            ? new TextEntity(cell.Value, new Point2((r.X1 + r.X2) / 2, centerY), h, TextAlign.MiddleCenter, style, TbText)
            : new TextEntity(cell.Value, new Point2(r.X1 + pad, centerY), h, TextAlign.MiddleLeft, style, TbText));
    }

    /// <summary>Largest height ≤ desired that keeps the text inside the box (approximate width of 0.7·h per character).</summary>
    private static double FitHeight(string text, double desired, double maxWidth, double maxHeight)
    {
        var byWidth = maxWidth / Math.Max(1, text.Length * 0.7);
        return Math.Max(0, Math.Min(desired, Math.Min(byWidth, maxHeight)));
    }

    /// <summary>Adds a view title under the drawing area and returns the area left for the viewport.</summary>
    public PaperRect AddViewTitle(SheetLayout sheet, PaperRect area, string title, string scale)
    {
        var band = S(16);
        var x = area.X1 + S(8);
        var titleH = S(5);
        var lineY = area.Y1 + band * 0.45;
        var lineLen = Math.Min(area.Width * 0.5, S(160));

        sheet.Entities.Add(new TextEntity(title.ToUpperInvariant(), new Point2(x, lineY + S(1.5)), FitHeight(title, titleH, lineLen, titleH), TextAlign.BaselineLeft, LayerStandards.TitleStyle, LayerStandards.ViewTitles));
        sheet.Entities.Add(new LineEntity(new Point2(x, lineY), new Point2(x + lineLen, lineY), LayerStandards.ViewTitles));
        sheet.Entities.Add(new TextEntity($"SCALE: {scale}", new Point2(x, lineY - S(1.5) - S(2.5)), S(2.5), TextAlign.BaselineLeft, LayerStandards.TextStyle, LayerStandards.ViewTitles));

        return new PaperRect(area.X1, area.Y1 + band, area.X2, area.Y2);
    }

    public void AddCoverContent(SheetLayout sheet, ProjectInfo p, IReadOnlyList<SheetDefinition> all)
    {
        var a = DrawingArea;
        var e = sheet.Entities;
        var cx = a.Center.X;
        var maxW = a.Width * 0.85;

        e.Add(new TextEntity(p.ProjectName.ToUpperInvariant(), new Point2(cx, a.Y1 + a.Height * 0.80), FitHeight(p.ProjectName, S(16), maxW, S(16)), TextAlign.MiddleCenter, LayerStandards.TitleStyle, LayerStandards.TitleBlockText));

        var y = a.Y1 + a.Height * 0.80 - S(18);
        foreach (var line in new[] { p.SiteAddress, p.Client, p.FirmName }.Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            e.Add(new TextEntity(line, new Point2(cx, y), FitHeight(line, S(6), maxW, S(6)), TextAlign.MiddleCenter, LayerStandards.TextStyle, LayerStandards.TitleBlockText));
            y -= S(10);
        }

        // Sheet index, filling the lower part of the drawing area in as many columns as needed.
        var top = Math.Min(y - S(6), a.Y1 + a.Height * 0.55);
        var bottom = a.Y1 + S(10);
        var left = a.X1 + S(12);
        var right = a.X2 - S(12);

        for (var t = 1.0; t >= 0.3; t -= 0.05)
        {
            var rowH = S(8) * t;
            var numW = S(32) * t;
            var colW = S(150) * t;
            var gap = S(12) * t;
            var headH = S(12) * t;
            var rowsPerCol = (int)Math.Floor((top - bottom - headH) / rowH);
            if (rowsPerCol < 1) continue;
            var cols = (int)Math.Ceiling(all.Count / (double)rowsPerCol);
            var totalW = cols * colW + (cols - 1) * gap;
            if (totalW > right - left && t > 0.31) continue;

            e.Add(new TextEntity("SHEET INDEX", new Point2(left, top - headH * 0.55), S(5) * t, TextAlign.MiddleLeft, LayerStandards.TitleStyle, LayerStandards.TitleBlockText));
            for (var c = 0; c < cols; c++)
            {
                var x0 = left + c * (colW + gap);
                var yTop = top - headH;
                e.Add(new LineEntity(new Point2(x0, yTop), new Point2(x0 + colW, yTop), LayerStandards.TitleBlock));
                for (var r = 0; r < rowsPerCol; r++)
                {
                    var i = c * rowsPerCol + r;
                    if (i >= all.Count) break;
                    var rowMid = yTop - (r + 0.5) * rowH;
                    var textH = S(3) * t;
                    e.Add(new TextEntity(all[i].Number, new Point2(x0 + S(1) * t, rowMid), textH, TextAlign.MiddleLeft, LayerStandards.TitleStyle, LayerStandards.TitleBlockText));
                    e.Add(new TextEntity(all[i].Title.ToUpperInvariant(), new Point2(x0 + numW, rowMid), FitHeight(all[i].Title, textH, colW - numW, textH), TextAlign.MiddleLeft, LayerStandards.TextStyle, LayerStandards.TitleBlockText));
                    e.Add(new LineEntity(new Point2(x0, yTop - (r + 1) * rowH), new Point2(x0 + colW, yTop - (r + 1) * rowH), LayerStandards.TitleBlockText));
                }
            }
            return;
        }
    }

    private sealed record Row(double Weight, params Cell[] Cells);
    private sealed record Cell(string Label, string Value, bool Emphasis = false, bool Centered = false);
}
