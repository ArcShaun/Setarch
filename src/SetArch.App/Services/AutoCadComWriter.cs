using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using SetArch.Core.Models;
using SetArch.Core.Planning;
using SetArch.Core.Standards;

namespace SetArch.App.Services;

public sealed class AutoCadWriteOptions
{
    /// <summary>Create the sheets in a new drawing (from acad.dwt / acadiso.dwt) instead of the active one.</summary>
    public bool NewDrawing { get; init; } = true;

    /// <summary>Optional path to save the drawing to when finished.</summary>
    public string? SaveAs { get; init; }
}

/// <summary>
/// Drives a running (or newly started) full AutoCAD through its ActiveX/COM automation interface.
/// Late-bound (dynamic) so it works with any AutoCAD version without referencing version-specific interop DLLs.
/// AutoCAD LT has no COM automation — use the AutoLISP export instead.
/// </summary>
public sealed class AutoCadComWriter
{
    private const string ProgId = "AutoCAD.Application";

    // AutoCAD enum values
    private const int AcAlignmentMiddleLeft = 9;
    private const int AcAlignmentMiddleCenter = 10;
    private const int AcInches = 0, AcMillimeters = 1;
    private const int AcLayout = 1;          // AcPlotType.acLayout
    private const int Ac1To1 = 16;           // AcPlotScale.ac1_1
    private const int Ac0Degrees = 0, Ac90Degrees = 1;

    private readonly Action<string> _log;
    private int _warnings;

    public AutoCadComWriter(Action<string> log) => _log = log;

    public Task WriteAsync(SheetPlan plan, AutoCadWriteOptions options) =>
        ComSupport.RunSta(() => Write(plan, options));

    private void Write(SheetPlan plan, AutoCadWriteOptions options)
    {
        _warnings = 0;
        dynamic acad = Connect();
        Try("Show AutoCAD", () => acad.Visible = true);

        dynamic doc = options.NewDrawing ? NewDocument(acad, plan) : acad.ActiveDocument;
        _log($"Drawing: {(string)doc.Name}");

        SetVar(doc, "INSUNITS", (short)(plan.Units == PaperUnits.Millimeters ? 4 : 1));
        SetVar(doc, "MEASUREMENT", (short)(plan.Units == PaperUnits.Millimeters ? 1 : 0));
        SetVar(doc, "PSLTSCALE", (short)1);
        SetVar(doc, "MSLTSCALE", (short)1);
        SetVar(doc, "LAYOUTCREATEVIEWPORT", (short)0);

        CreateLinetypes(doc, plan);
        CreateLayers(doc, plan);
        CreateTextStyles(doc, plan);

        var existing = LayoutNames(doc);
        string? titleBlockName = null;
        var created = 0;

        foreach (var sheet in plan.Sheets)
        {
            if (existing.Contains(sheet.LayoutName))
            {
                _log($"Skipped '{sheet.LayoutName}' - a layout with that name already exists.");
                continue;
            }

            _log($"Creating {sheet.LayoutName} ...");
            dynamic layout = doc.Layouts.Add(sheet.LayoutName);
            doc.ActiveLayout = layout;
            ApplyPageSetup(layout, plan);

            dynamic space = doc.PaperSpace;
            if (sheet.TitleBlockInsert is { } tb)
                titleBlockName = InsertTitleBlock(doc, space, tb, titleBlockName);

            foreach (var entity in sheet.Entities)
                AddEntity(doc, space, entity);

            if (sheet.Viewport is { } vp)
                AddViewport(doc, space, vp);

            created++;
        }

        if (plan.DeleteDefaultLayouts)
            DeleteDefaultLayouts(doc, plan, options.NewDrawing);

        if (plan.Sheets.Count > 0)
        {
            Try("Activate first sheet", () =>
            {
                doc.ActiveLayout = doc.Layouts.Item(plan.Sheets[0].LayoutName);
                acad.ZoomExtents();
            });
        }

        if (!string.IsNullOrWhiteSpace(options.SaveAs))
        {
            Try("Save drawing", () => doc.SaveAs(options.SaveAs));
            _log($"Saved to {options.SaveAs}");
        }

        _log(_warnings == 0
            ? $"Done - created {created} sheet(s)."
            : $"Done - created {created} sheet(s) with {_warnings} warning(s). See messages above.");
    }

    private dynamic Connect()
    {
        var running = ComSupport.TryGetActiveObject(ProgId);
        if (running is not null)
        {
            _log("Connected to running AutoCAD.");
            return running;
        }

        var type = Type.GetTypeFromProgID(ProgId)
            ?? throw new InvalidOperationException(
                "AutoCAD is not installed or not registered for automation. " +
                "AutoCAD LT does not support automation - choose \"Save AutoLISP file\" instead.");

        _log("Starting AutoCAD (this can take a minute) ...");
        dynamic app = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("AutoCAD could not be started.");

        // Wait until AutoCAD has finished starting up and accepts calls.
        for (var i = 0; i < 120; i++)
        {
            try
            {
                _ = app.Documents.Count;
                return app;
            }
            catch (COMException)
            {
                Thread.Sleep(1000);
            }
        }
        throw new TimeoutException("AutoCAD did not become ready in time.");
    }

    private dynamic NewDocument(dynamic acad, SheetPlan plan)
    {
        try
        {
            return acad.Documents.Add(plan.DrawingTemplate);
        }
        catch (Exception ex)
        {
            Warn($"Template {plan.DrawingTemplate} not found ({ex.Message}); using AutoCAD's default template.");
            return acad.Documents.Add();
        }
    }

    private void CreateLinetypes(dynamic doc, SheetPlan plan)
    {
        foreach (var lt in plan.RequiredLinetypes)
        {
            if (Exists(() => doc.Linetypes.Item(lt))) continue;
            Try($"Load linetype {lt}", () => doc.Linetypes.Load(lt, plan.LinetypeFile));
        }
    }

    private void CreateLayers(dynamic doc, SheetPlan plan)
    {
        var added = 0;
        foreach (var spec in plan.Layers)
        {
            if (Exists(() => doc.Layers.Item(spec.Name))) continue; // never change the user's existing layers
            Try($"Layer {spec.Name}", () =>
            {
                dynamic layer = doc.Layers.Add(spec.Name);
                layer.color = spec.Color;
                if (Exists(() => doc.Linetypes.Item(spec.Linetype))) layer.Linetype = spec.Linetype;
                layer.Lineweight = spec.Lineweight;
                layer.Plottable = spec.Plot;
                Try($"Layer {spec.Name} description", () => layer.Description = spec.Description);
                added++;
            });
        }
        _log($"Layers: {added} added.");
    }

    private void CreateTextStyles(dynamic doc, SheetPlan plan)
    {
        foreach (var style in plan.TextStyles)
        {
            if (Exists(() => doc.TextStyles.Item(style.Name))) continue;
            Try($"Text style {style.Name}", () =>
            {
                dynamic st = doc.TextStyles.Add(style.Name);
                st.fontFile = style.FontFile;
            });
        }
    }

    private void ApplyPageSetup(dynamic layout, SheetPlan plan)
    {
        Try("Plot device", () =>
        {
            layout.ConfigName = plan.PlotDevice;
            layout.RefreshPlotDeviceInfo();
        });

        string? media = null;
        Try("Paper size", () =>
        {
            var names = ToStrings(layout.GetCanonicalMediaNames());
            media = PaperSizes.PickMedia(names, plan.Paper);
            if (media is null)
            {
                Warn($"Plotter '{plan.PlotDevice}' has no {plan.Paper.Name} paper; set the page setup manually.");
                return;
            }
            layout.CanonicalMediaName = media;
        });

        Try("Paper units", () => layout.PaperUnits = plan.Units == PaperUnits.Millimeters ? AcMillimeters : AcInches);
        Try("Plot area", () => layout.PlotType = AcLayout);
        Try("Plot scale", () =>
        {
            layout.UseStandardScale = true;
            layout.StandardScale = Ac1To1;
        });
        Try("Plot origin", () =>
        {
            layout.CenterPlot = false;
            layout.PlotOrigin = new double[] { 0, 0 };
        });

        if (media is not null)
        {
            Try("Orientation", () =>
            {
                bool? mediaLandscape = null;
                try
                {
                    double w, h;
                    layout.GetPaperSize(out w, out h);
                    if (w > 0 && h > 0) mediaLandscape = w > h;
                }
                catch (Exception)
                {
                    mediaLandscape = PaperSizes.MediaIsLandscape(media);
                }

                var wantLandscape = plan.Width > plan.Height;
                layout.PlotRotation = mediaLandscape is null || mediaLandscape == wantLandscape ? Ac0Degrees : Ac90Degrees;
            });
        }

        if (!string.IsNullOrWhiteSpace(plan.PlotStyleTable))
        {
            Try("Plot style table", () =>
            {
                layout.StyleSheet = plan.PlotStyleTable;
                layout.PlotWithPlotStyles = true;
            });
        }
    }

    private string InsertTitleBlock(dynamic doc, dynamic space, BlockInsertSpec tb, string? knownName)
    {
        if (knownName is null && !File.Exists(tb.Path))
            throw new FileNotFoundException($"Title block drawing not found: {tb.Path}");

        dynamic br = space.InsertBlock(P3(tb.Position), knownName ?? tb.Path, 1.0, 1.0, 1.0, 0.0);
        Try("Title block layer", () => br.Layer = tb.Layer);

        var filled = 0;
        if ((bool)br.HasAttributes)
        {
            foreach (dynamic att in ToObjects(br.GetAttributes()))
            {
                var tag = TitleBlockFields.Normalize((string)att.TagString);
                if (tb.Attributes.TryGetValue(tag, out var value))
                {
                    att.TextString = value;
                    filled++;
                }
            }
        }
        if (knownName is null)
            _log($"Title block '{(string)br.Name}': {filled} attribute(s) filled.");
        return (string)br.Name;
    }

    private void AddEntity(dynamic doc, dynamic space, PaperEntity entity)
    {
        switch (entity)
        {
            case LineEntity l:
                Try("Line", () => { dynamic e = space.AddLine(P3(l.Start), P3(l.End)); e.Layer = l.Layer; });
                break;

            case PolylineEntity p:
                Try("Polyline", () =>
                {
                    var coords = p.Points.SelectMany(pt => new[] { pt.X, pt.Y }).ToArray();
                    dynamic e = space.AddLightWeightPolyline(coords);
                    e.Closed = p.Closed;
                    e.Layer = p.Layer;
                });
                break;

            case TextEntity t when !string.IsNullOrEmpty(t.Text) && t.Height > 0:
                Try("Text", () =>
                {
                    dynamic e = space.AddText(t.Text, P3(t.Position), t.Height);
                    e.Layer = t.Layer;
                    if (Exists(() => doc.TextStyles.Item(t.Style))) e.StyleName = t.Style;
                    if (t.Align != TextAlign.BaselineLeft)
                    {
                        e.Alignment = t.Align == TextAlign.MiddleCenter ? AcAlignmentMiddleCenter : AcAlignmentMiddleLeft;
                        e.TextAlignmentPoint = P3(t.Position);
                    }
                });
                break;
        }
    }

    private void AddViewport(dynamic doc, dynamic space, ViewportSpec vp)
    {
        Try($"Viewport {vp.Scale.Name}", () =>
        {
            dynamic v = space.AddPViewport(P3(vp.Center), vp.Width, vp.Height);
            v.Layer = vp.Layer;
            Try("Viewport on", () => v.Display(true));
            v.CustomScale = vp.Scale.Factor;
            if (vp.Lock) v.DisplayLocked = true;
        });
    }

    private void DeleteDefaultLayouts(dynamic doc, SheetPlan plan, bool newDrawing)
    {
        var ours = new HashSet<string>(plan.Sheets.Select(s => s.LayoutName), StringComparer.OrdinalIgnoreCase);
        var toDelete = new List<dynamic>();
        foreach (dynamic layout in doc.Layouts)
        {
            if ((bool)layout.ModelType) continue;
            string name = layout.Name;
            if (ours.Contains(name)) continue;
            // In a new drawing every template layout goes; in an existing drawing only the untouched defaults.
            var isDefault = name.Equals("Layout1", StringComparison.OrdinalIgnoreCase) || name.Equals("Layout2", StringComparison.OrdinalIgnoreCase);
            if (newDrawing || (isDefault && (int)layout.Block.Count <= 1))
                toDelete.Add(layout);
        }

        if (toDelete.Count == 0 || ours.Count == 0) return;
        Try("Switch layout", () => doc.ActiveLayout = doc.Layouts.Item(plan.Sheets[0].LayoutName));
        foreach (var layout in toDelete)
        {
            string name = layout.Name;
            Try($"Delete layout {name}", () => layout.Delete());
        }
    }

    private static HashSet<string> LayoutNames(dynamic doc)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (dynamic layout in doc.Layouts) names.Add((string)layout.Name);
        return names;
    }

    private void SetVar(dynamic doc, string name, object value) =>
        Try($"Set {name}", () => doc.SetVariable(name, value));

    private static bool Exists(Func<object> lookup)
    {
        try { return lookup() is not null; }
        catch (COMException) { return false; }
        catch (ArgumentException) { return false; }
    }

    private void Try(string what, Action action)
    {
        try { action(); }
        catch (Exception ex) { Warn($"{what}: {ex.Message}"); }
    }

    private void Warn(string message)
    {
        _warnings++;
        _log("  Warning - " + message);
    }

    private static double[] P3(Point2 p) => new[] { p.X, p.Y, 0.0 };

    private static List<string> ToStrings(object value) =>
        value is IEnumerable e ? e.Cast<object>().Select(o => o?.ToString() ?? "").ToList() : new List<string>();

    private static IEnumerable<object> ToObjects(object value) =>
        value is IEnumerable e ? e.Cast<object>() : Enumerable.Empty<object>();
}
