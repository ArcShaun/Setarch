using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SetArch.Core.Planning;

namespace SetArch.App;

/// <summary>Draws a sheet from the plan, scaled to fit, so the user can check the layout before creating it.</summary>
public sealed class SheetPreview : FrameworkElement
{
    public static readonly DependencyProperty PlanProperty = DependencyProperty.Register(
        nameof(Plan), typeof(SheetPlan), typeof(SheetPreview), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SheetProperty = DependencyProperty.Register(
        nameof(Sheet), typeof(SheetLayout), typeof(SheetPreview), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public SheetPlan? Plan { get => (SheetPlan?)GetValue(PlanProperty); set => SetValue(PlanProperty, value); }
    public SheetLayout? Sheet { get => (SheetLayout?)GetValue(SheetProperty); set => SetValue(SheetProperty, value); }

    private static readonly Typeface Regular = new(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private static readonly Typeface Bold = new(new FontFamily("Arial"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    protected override void OnRender(DrawingContext dc)
    {
        var plan = Plan;
        var sheet = Sheet;
        if (plan is null || sheet is null || ActualWidth <= 0 || ActualHeight <= 0) return;

        const double margin = 8;
        var k = Math.Min((ActualWidth - 2 * margin) / plan.Width, (ActualHeight - 2 * margin) / plan.Height);
        var ox = (ActualWidth - plan.Width * k) / 2;
        var oy = (ActualHeight - plan.Height * k) / 2;
        Point P(Point2 p) => new(ox + p.X * k, oy + (plan.Height - p.Y) * k);

        var ink = new Pen(Brushes.Black, 1);
        var faint = new Pen(Brushes.SteelBlue, 1) { DashStyle = DashStyles.Dash };
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        // Paper with a soft shadow.
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), null, new Rect(ox + 3, oy + 3, plan.Width * k, plan.Height * k));
        dc.DrawRectangle(Brushes.White, new Pen(Brushes.Gray, 0.5), new Rect(ox, oy, plan.Width * k, plan.Height * k));

        foreach (var entity in sheet.Entities)
        {
            switch (entity)
            {
                case LineEntity l:
                    dc.DrawLine(ink, P(l.Start), P(l.End));
                    break;

                case PolylineEntity p:
                    var geo = new StreamGeometry();
                    using (var ctx = geo.Open())
                    {
                        ctx.BeginFigure(P(p.Points[0]), false, p.Closed);
                        ctx.PolyLineTo(p.Points.Skip(1).Select(P).ToList(), true, false);
                    }
                    dc.DrawGeometry(null, ink, geo);
                    break;

                case TextEntity t when t.Height * k >= 1.5:
                    var face = t.Style.Contains("TITLE", StringComparison.OrdinalIgnoreCase) ? Bold : Regular;
                    // AutoCAD text height is cap height; WPF font size is em size (~1.4x cap height for Arial).
                    var ft = new FormattedText(t.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, t.Height * k * 1.4, Brushes.Black, dpi);
                    var anchor = P(t.Position);
                    var x = t.Align == TextAlign.MiddleCenter ? anchor.X - ft.Width / 2 : anchor.X;
                    var y = t.Align == TextAlign.BaselineLeft ? anchor.Y - ft.Baseline : anchor.Y - ft.Height / 2;
                    dc.DrawText(ft, new Point(x, y));
                    break;
            }
        }

        if (sheet.Viewport is { } vp)
        {
            var tl = P(new Point2(vp.Center.X - vp.Width / 2, vp.Center.Y + vp.Height / 2));
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(18, 70, 130, 180)), faint, new Rect(tl, new Size(vp.Width * k, vp.Height * k)));
            var label = new FormattedText($"Viewport  {vp.Scale.Name}", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Regular, 13, Brushes.SteelBlue, dpi);
            var c = P(vp.Center);
            dc.DrawText(label, new Point(c.X - label.Width / 2, c.Y - label.Height / 2));
        }

        if (sheet.TitleBlockInsert is { } tb)
        {
            var label = new FormattedText($"Your title block: {tb.BlockName}", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Regular, 13, Brushes.SteelBlue, dpi);
            dc.DrawText(label, new Point(ox + 10, oy + 10));
        }
    }
}
