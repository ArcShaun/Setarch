using System.Globalization;
using System.Text;
using SetArch.Core.Planning;
using SetArch.Core.Standards;

namespace SetArch.Core.Output;

/// <summary>
/// Writes a <see cref="SheetPlan"/> as a self-contained AutoLISP file defining the SETARCH command.
/// Uses only core AutoLISP (entmake/entmod/command) — no ActiveX — so it also runs in AutoCAD LT 2024+.
/// </summary>
public static class LispGenerator
{
    public const string CommandName = "SETARCH";

    public static string Generate(SheetPlan plan, bool applyPageSetup = true)
    {
        var sb = new StringBuilder();
        var metric = plan.Units == PaperUnits.Millimeters;

        sb.AppendLine(";;; SetArch - architectural sheet setup");
        sb.AppendLine($";;; Project : {OneLine(plan.Project.ProjectName)}");
        sb.AppendLine($";;; Paper   : {plan.Paper.Name}, {plan.Orientation}");
        sb.AppendLine($";;; Sheets  : {plan.Sheets.Count}");
        sb.AppendLine(";;; Usage   : APPLOAD this file (or drag it into the drawing), then type " + CommandName + ".");
        sb.AppendLine();
        sb.AppendLine(Helpers);

        sb.AppendLine($"(defun c:{CommandName} ( / *error* sa:old sa:first)");
        sb.AppendLine("  (defun *error* (msg)");
        sb.AppendLine("    (sa:restore sa:old)");
        sb.AppendLine("    (if (not (member msg '(\"Function cancelled\" \"quit / exit abort\"))) (princ (strcat \"\\nSetArch error: \" msg)))");
        sb.AppendLine("    (princ))");
        sb.AppendLine("  (setq sa:old (sa:save '(\"CMDECHO\" \"OSMODE\" \"FILEDIA\" \"ATTREQ\" \"CLAYER\" \"LAYOUTREGENCTL\" \"LAYOUTCREATEVIEWPORT\")))");
        sb.AppendLine("  (setvar \"CMDECHO\" 0) (setvar \"OSMODE\" 0) (setvar \"FILEDIA\" 0)");
        sb.AppendLine("  (setvar \"LAYOUTCREATEVIEWPORT\" 0)");
        sb.AppendLine("  (command \"_.UNDO\" \"_BEGIN\")");
        sb.AppendLine();

        sb.AppendLine("  ;; Units");
        sb.AppendLine($"  (setvar \"INSUNITS\" {(metric ? 4 : 1)})");
        sb.AppendLine($"  (setvar \"MEASUREMENT\" {(metric ? 1 : 0)})");
        sb.AppendLine("  (setvar \"PSLTSCALE\" 1)");
        sb.AppendLine("  (setvar \"MSLTSCALE\" 1)");
        sb.AppendLine();

        sb.AppendLine("  ;; Linetypes, layers and text styles");
        foreach (var lt in plan.RequiredLinetypes)
            sb.AppendLine($"  (sa:ltype {Str(lt)} {Str(plan.LinetypeFile)})");
        foreach (var l in plan.Layers)
            sb.AppendLine($"  (sa:layer {Str(l.Name)} {l.Color} {Str(l.Linetype)} {l.Lineweight} {(l.Plot ? 1 : 0)})");
        foreach (var s in plan.TextStyles)
            sb.AppendLine($"  (sa:style {Str(s.Name)} {Str(s.FontFile)})");
        sb.AppendLine();

        foreach (var sheet in plan.Sheets)
        {
            sb.AppendLine($"  ;; ---- {OneLine(sheet.Number)} {OneLine(sheet.Title)}");
            sb.AppendLine($"  (if (not (sa:layout {Str(sheet.LayoutName)}))");
            sb.AppendLine($"    (princ {Str("\nSetArch: layout already exists, skipped: " + sheet.LayoutName)})");
            sb.AppendLine("   (progn");
            sb.AppendLine($"  (princ {Str("\nSetArch: " + sheet.LayoutName)})");
            if (applyPageSetup)
            {
                sb.AppendLine($"  (sa:pagesetup {Str(plan.PlotDevice)} {Str(plan.Paper.PdfMediaDisplayName)} {Str(metric ? "_M" : "_I")} {Str(plan.Orientation == Models.Orientation.Landscape ? "_L" : "_P")} {Str(string.IsNullOrWhiteSpace(plan.PlotStyleTable) ? "." : plan.PlotStyleTable)})");
            }

            if (sheet.TitleBlockInsert is { } ins)
            {
                sb.Append($"  (sa:insert {Str(ins.Path)} {Str(ins.BlockName)} {N(ins.Position.X)} {N(ins.Position.Y)} {Str(ins.Layer)} '(");
                foreach (var kv in ins.Attributes)
                    sb.Append($"({Str(kv.Key)} . {Str(kv.Value)})");
                sb.AppendLine("))");
            }

            foreach (var entity in sheet.Entities)
                sb.AppendLine("  " + Entity(entity));

            if (sheet.Viewport is { } vp)
                sb.AppendLine($"  (sa:vport {N(vp.Center.X)} {N(vp.Center.Y)} {N(vp.Width)} {N(vp.Height)} {N(vp.Scale.Factor)} {Str(vp.Layer)} {(vp.Lock ? "T" : "nil")})");

            sb.AppendLine("  ))");
            sb.AppendLine();
        }

        var keep = string.Join(" ", plan.Sheets.Select(s => Str(s.LayoutName.ToUpperInvariant())));
        if (plan.DeleteDefaultLayouts)
            sb.AppendLine($"  (sa:deldefaults (list {keep}))");
        if (plan.Sheets.Count > 0)
            sb.AppendLine($"  (setvar \"CTAB\" {Str(plan.Sheets[0].LayoutName)})");
        sb.AppendLine("  (command \"_.ZOOM\" \"_E\")");
        sb.AppendLine("  (command \"_.UNDO\" \"_END\")");
        sb.AppendLine("  (sa:restore sa:old)");
        sb.AppendLine($"  (princ {Str($"\nSetArch: created {plan.Sheets.Count} sheet(s).")})");
        sb.AppendLine("  (princ))");
        sb.AppendLine();
        sb.AppendLine($"(princ {Str($"\nSetArch loaded. Type {CommandName} to create the sheets.")})");
        sb.AppendLine("(princ)");
        return sb.ToString();
    }

    private static string Entity(PaperEntity e) => e switch
    {
        LineEntity l => $"(sa:line {N(l.Start.X)} {N(l.Start.Y)} {N(l.End.X)} {N(l.End.Y)} {Str(l.Layer)})",
        PolylineEntity p => $"(sa:pline '({string.Join(" ", p.Points.Select(pt => $"({N(pt.X)} {N(pt.Y)})"))}) {(p.Closed ? "T" : "nil")} {Str(p.Layer)})",
        TextEntity t => $"(sa:text {Str(t.Text)} {N(t.Position.X)} {N(t.Position.Y)} {N(t.Height)} {(int)t.Align} {Str(t.Style)} {Str(t.Layer)})",
        _ => throw new NotSupportedException(e.GetType().Name),
    };

    /// <summary>Formats a number for LISP (invariant culture, always with a decimal point so it is a real).</summary>
    public static string N(double v)
    {
        var s = Math.Round(v, 6).ToString("0.0#####", CultureInfo.InvariantCulture);
        return s == "-0.0" ? "0.0" : s;
    }

    /// <summary>Quotes and escapes a string for AutoLISP.</summary>
    public static string Str(string? s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s ?? "")
        {
            sb.Append(c switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "",
                '\t' => " ",
                _ => c.ToString(),
            });
        }
        return sb.Append('"').ToString();
    }

    private static string OneLine(string s) => s.Replace('\r', ' ').Replace('\n', ' ');

    private const string Helpers = """
;;; ---------------------------------------------------------------- helpers
(defun sa:save (names) (mapcar '(lambda (n) (cons n (getvar n))) names))
(defun sa:restore (lst) (foreach p lst (if (cdr p) (setvar (car p) (cdr p)))))

(defun sa:ltype (name file)
  (if (not (tblsearch "LTYPE" name))
    (command "_.-LINETYPE" "_L" name file "")))

(defun sa:layer (name color ltype lw plot / base)
  (if (not (tblsearch "LTYPE" ltype)) (setq ltype "Continuous"))
  (if (not (tblsearch "LAYER" name))
    (entmake (list '(0 . "LAYER") '(100 . "AcDbSymbolTableRecord") '(100 . "AcDbLayerTableRecord")
                   (cons 2 name) '(70 . 0) (cons 62 color) (cons 6 ltype) (cons 290 plot) (cons 370 lw)))))

(defun sa:style (name font)
  (if (not (tblsearch "STYLE" name))
    (entmake (list '(0 . "STYLE") '(100 . "AcDbSymbolTableRecord") '(100 . "AcDbTextStyleTableRecord")
                   (cons 2 name) '(70 . 0) '(40 . 0.0) '(41 . 1.0) '(50 . 0.0) '(71 . 0) '(42 . 2.5)
                   (cons 3 font) '(4 . "")))))

;; Creates the layout and makes it current. Returns nil (and leaves it alone) if it already exists.
(defun sa:layout (name)
  (if (member (strcase name) (mapcar 'strcase (layoutlist)))
    nil
    (progn
      (command "_.LAYOUT" "_N" name)
      (setvar "CTAB" name)
      (command "_.PSPACE")
      T)))

;; Saves a page setup on the current layout via -PLOT without plotting.
(defun sa:pagesetup (device media units orient ctb)
  (command "_.-PLOT" "_Y" "" device media units orient "_N" "_L" "1:1" "0,0" "_Y" ctb "_Y" "_N" "_N" "_N" "" "_Y" "_N"))

(defun sa:common (layer)
  (list '(67 . 1) (cons 410 (getvar "CTAB")) (cons 8 layer)))

(defun sa:line (x1 y1 x2 y2 layer)
  (entmake (append (list '(0 . "LINE")) (sa:common layer)
                   (list (list 10 x1 y1 0.0) (list 11 x2 y2 0.0)))))

(defun sa:pline (pts closed layer)
  (entmake (append (list '(0 . "LWPOLYLINE") '(100 . "AcDbEntity")) (sa:common layer)
                   (list '(100 . "AcDbPolyline") (cons 90 (length pts)) (cons 70 (if closed 1 0)))
                   (mapcar '(lambda (p) (cons 10 p)) pts))))

;; just: 0 = baseline left, 1 = middle left, 2 = middle center
(defun sa:text (str x y h just style layer / jh jv)
  (if (> h 0.0)
    (progn
      (setq jh (nth just '(0 0 1)) jv (nth just '(0 2 2)))
      (if (not (tblsearch "STYLE" style)) (setq style "Standard"))
      (entmake (append (list '(0 . "TEXT")) (sa:common layer)
                       (list (list 10 x y 0.0) (cons 40 h) (cons 1 str) (cons 7 style)
                             (cons 72 jh) (list 11 x y 0.0) (cons 73 jv)))))))

(defun sa:vport (cx cy w h factor layer lock / old e ed)
  (setq old (getvar "CLAYER"))
  (setvar "CLAYER" layer)
  (command "_.MVIEW" (list (- cx (/ w 2.0)) (- cy (/ h 2.0))) (list (+ cx (/ w 2.0)) (+ cy (/ h 2.0))))
  (setq e (entlast))
  (command "_.MSPACE")
  (command "_.ZOOM" "_C" '(0.0 0.0) (strcat (rtos factor 2 10) "XP"))
  (command "_.PSPACE")
  (if lock
    (progn
      (setq ed (entget e))
      (entmod (subst (cons 90 (logior (cdr (assoc 90 ed)) 16384)) (assoc 90 ed) ed))))
  (setvar "CLAYER" old))

(defun sa:norm (s / i c r)
  (setq r "" i 1)
  (while (<= i (strlen s))
    (setq c (substr s i 1))
    (if (wcmatch c "[A-Za-z0-9]") (setq r (strcat r (strcase c))))
    (setq i (1+ i)))
  r)

(defun sa:insert (path bname x y layer attrs / old ins e ed v)
  (setq old (getvar "CLAYER"))
  (setvar "CLAYER" layer)
  (setvar "ATTREQ" 0)
  (command "_.-INSERT" (if (tblsearch "BLOCK" bname) bname (strcat bname "=" path)) "_S" 1.0 (list x y 0.0) 0.0)
  (setq ins (entlast) e ins)
  (while (and (setq e (entnext e)) (= (cdr (assoc 0 (setq ed (entget e)))) "ATTRIB"))
    (if (setq v (assoc (sa:norm (cdr (assoc 2 ed))) attrs))
      (entmod (subst (cons 1 (cdr v)) (assoc 1 ed) ed))))
  (entupd ins)
  (setvar "CLAYER" old))

(defun sa:deldefaults (keep)
  (foreach l (layoutlist)
    (if (and (member (strcase l) '("LAYOUT1" "LAYOUT2")) (not (member (strcase l) keep)))
      (command "_.LAYOUT" "_D" l))))

""";
}
