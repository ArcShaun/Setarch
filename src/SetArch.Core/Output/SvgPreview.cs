using System.Globalization;
using System.Net;
using System.Text;
using SetArch.Core.Planning;

namespace SetArch.Core.Output;

/// <summary>Renders one sheet of a plan as an SVG image (for previews and checking layouts).</summary>
public static class SvgPreview
{
    public static string Render(SheetPlan plan, SheetLayout sheet, double pixelWidth = 1200)
    {
        var k = pixelWidth / plan.Width;
        string X(double x) => (x * k).ToString("0.##", CultureInfo.InvariantCulture);
        string Y(double y) => ((plan.Height - y) * k).ToString("0.##", CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{X(plan.Width)}\" height=\"{X(plan.Height)}\" viewBox=\"0 0 {X(plan.Width)} {X(plan.Height)}\">");
        sb.Append($"<rect width=\"100%\" height=\"100%\" fill=\"white\"/>");
        sb.Append("<g stroke=\"black\" stroke-width=\"1\" fill=\"none\" font-family=\"Arial\">");

        foreach (var e in sheet.Entities)
        {
            switch (e)
            {
                case LineEntity l:
                    sb.Append($"<line x1=\"{X(l.Start.X)}\" y1=\"{Y(l.Start.Y)}\" x2=\"{X(l.End.X)}\" y2=\"{Y(l.End.Y)}\"/>");
                    break;
                case PolylineEntity p:
                    var pts = string.Join(" ", p.Points.Select(pt => $"{X(pt.X)},{Y(pt.Y)}"));
                    sb.Append(p.Closed ? $"<polygon points=\"{pts}\"/>" : $"<polyline points=\"{pts}\"/>");
                    break;
                case TextEntity t:
                    var anchor = t.Align == TextAlign.MiddleCenter ? "middle" : "start";
                    var baseline = t.Align == TextAlign.BaselineLeft ? "auto" : "central";
                    var weight = t.Style.Contains("TITLE") ? "bold" : "normal";
                    sb.Append($"<text x=\"{X(t.Position.X)}\" y=\"{Y(t.Position.Y)}\" font-size=\"{X(t.Height * 1.4)}\" text-anchor=\"{anchor}\" dominant-baseline=\"{baseline}\" font-weight=\"{weight}\" fill=\"black\" stroke=\"none\">{WebUtility.HtmlEncode(t.Text)}</text>");
                    break;
            }
        }

        if (sheet.Viewport is { } vp)
        {
            sb.Append($"<rect x=\"{X(vp.Center.X - vp.Width / 2)}\" y=\"{Y(vp.Center.Y + vp.Height / 2)}\" width=\"{X(vp.Width)}\" height=\"{X(vp.Height)}\" stroke=\"#9aa\" stroke-dasharray=\"6 4\"/>");
            sb.Append($"<text x=\"{X(vp.Center.X)}\" y=\"{Y(vp.Center.Y)}\" text-anchor=\"middle\" fill=\"#9aa\" stroke=\"none\" font-size=\"16\">Viewport {WebUtility.HtmlEncode(vp.Scale.Name)}</text>");
        }

        if (sheet.TitleBlockInsert is { } ins)
            sb.Append($"<text x=\"{X(plan.Width - 10 * plan.Width / 100)}\" y=\"{Y(plan.Height / 2)}\" text-anchor=\"middle\" fill=\"#9aa\" stroke=\"none\" font-size=\"16\">[{WebUtility.HtmlEncode(ins.BlockName)}]</text>");

        sb.Append("</g></svg>");
        return sb.ToString();
    }
}
