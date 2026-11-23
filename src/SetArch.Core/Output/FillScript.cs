using System.Globalization;
using System.Text;
using SetArch.Core.Planning;

namespace SetArch.Core.Output;

/// <summary>
/// Builds the AutoLISP that replaces placeholders in a drawing. It uses only core AutoLISP
/// (entget/entmod/ssget/tblnext, no vla-* ActiveX), so it runs in accoreconsole.exe and in AutoCAD LT 2024+.
///
/// What it changes, everywhere in the drawing (model space, every layout, and inside block definitions such as
/// a title block): TEXT, MTEXT, attribute values and attribute-definition defaults containing a placeholder;
/// and attributes whose TAG matches a field key (PROJECT_NAME, Project-Name ...) get that field's value.
/// </summary>
public static class FillScript
{
    public const string ResultMarker = "SETARCH-RESULT";
    public const string LtCommand = "SETARCHFILL";

    /// <summary>Script for accoreconsole: fills the drawing, saves it and prints the result marker.</summary>
    public static string ConsoleScript(IReadOnlyDictionary<string, string> values, string prefix, string suffix)
    {
        var sb = new StringBuilder();
        // Scripts treat a blank line as Enter, so the LISP is written without blank lines.
        foreach (var line in Lisp(values, prefix, suffix).Split('\n'))
            if (line.Trim().Length > 0 && !line.TrimStart().StartsWith(';')) sb.Append(line.TrimEnd('\r')).Append('\n');
        sb.Append("(setarch:fill)\n");
        sb.Append("_.QSAVE\n");
        return sb.ToString();
    }

    /// <summary>A .lsp defining the SETARCHFILL command, for AutoCAD LT or for running by hand.</summary>
    public static string LtLisp(IReadOnlyDictionary<string, string> values, string prefix, string suffix) =>
        ";;; SetArch - fills project placeholders in the current drawing.\n" +
        ";;; APPLOAD this file (or drag it into the drawing), then type " + LtCommand + " in each drawing and save.\n" +
        Lisp(values, prefix, suffix) +
        $"(defun c:{LtCommand} () (setarch:fill) (princ))\n" +
        $"(princ \"\\nSetArch loaded. Type {LtCommand} to fill in the project details.\")\n(princ)\n";

    public static string Lisp(IReadOnlyDictionary<string, string> values, string prefix, string suffix)
    {
        var sb = new StringBuilder();
        sb.Append(";;; values: (token plain mtext)\n");
        sb.Append("(setq setarch:tokens (list\n");
        foreach (var (key, value) in values.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var token = (prefix + key + suffix).ToUpperInvariant();
            sb.Append($"  (list {Str(token)} {Str(Unicode(Plain(value)))} {Str(Unicode(MText(value)))})\n");
        }
        sb.Append("))\n");
        sb.Append("(setq setarch:tags (list\n");
        foreach (var (key, value) in values.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            sb.Append($"  (cons {Str(Tokens.NormalizeTag(key))} {Str(Unicode(Plain(value)))})\n");
        sb.Append("))\n");
        sb.Append($"(setq setarch:prefix {Str(prefix.ToUpperInvariant())})\n");
        sb.Append(Helpers);
        return sb.ToString();
    }

    /// <summary>TEXT/attributes are single-line.</summary>
    public static string Plain(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", ", ");

    /// <summary>MTEXT treats \ { } as formatting, and uses \P for a new line.</summary>
    public static string MText(string s) =>
        s.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}")
         .Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\\P");

    /// <summary>Non-ASCII characters as AutoCAD \U+XXXX codes, so the script is pure ASCII whatever the code page.</summary>
    public static string Unicode(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            sb.Append(c is >= ' ' and <= '~' ? c.ToString() : c < ' ' ? " " : "\\U+" + ((int)c).ToString("X4", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>Quotes and escapes a string for AutoLISP source.</summary>
    public static string Str(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in s)
            sb.Append(c switch { '\\' => "\\\\", '"' => "\\\"", _ => c.ToString() });
        return sb.Append('"').ToString();
    }

    private const string Helpers = """
(defun setarch:find (str pat / i n lp)
  (setq i 1 n (strlen str) lp (strlen pat) pat (strcase pat))
  (while (and (<= (+ i lp -1) n) (/= (strcase (substr str i lp)) pat)) (setq i (1+ i)))
  (if (<= (+ i lp -1) n) i))
(defun setarch:subst1 (str old new / i r)
  (setq r "")
  (while (setq i (setarch:find str old))
    (setq r (strcat r (substr str 1 (1- i)) new) str (substr str (+ i (strlen old)))))
  (strcat r str))
(defun setarch:apply (str mtext / tk)
  (if (and str (setarch:find str setarch:prefix))
    (foreach tk setarch:tokens
      (setq str (setarch:subst1 str (car tk) (if mtext (caddr tk) (cadr tk))))))
  str)
(defun setarch:mtext-get (ed / s)
  (setq s "")
  (foreach p ed (if (= (car p) 3) (setq s (strcat s (cdr p)))))
  (strcat s (cdr (assoc 1 ed))))
(defun setarch:mtext-chunks (s / out)
  (while (> (strlen s) 250)
    (setq out (cons (cons 3 (substr s 1 250)) out) s (substr s 251)))
  (reverse (cons (cons 1 s) out)))
(defun setarch:mtext-put (ed s / out done)
  (foreach p ed
    (if (member (car p) '(1 3))
      (if (not done) (setq out (append (reverse (setarch:mtext-chunks s)) out) done T))
      (setq out (cons p out))))
  (entmod (reverse out)))
(defun setarch:do (e / ed typ old new tag v)
  (setq ed (entget e) typ (cdr (assoc 0 ed)))
  (cond
    ((= typ "MTEXT")
      (setq old (setarch:mtext-get ed) new (setarch:apply old T))
      (if (/= old new) (progn (setarch:mtext-put ed new) (setq setarch:count (1+ setarch:count)))))
    ((member typ '("TEXT" "ATTRIB" "ATTDEF"))
      (setq old (cdr (assoc 1 ed)) new (setarch:apply old nil))
      (if (and (= typ "ATTRIB") (setq v (assoc (setarch:norm (cdr (assoc 2 ed))) setarch:tags)))
        (setq new (cdr v)))
      (if (/= old new)
        (progn (entmod (subst (cons 1 new) (assoc 1 ed) ed)) (setq setarch:count (1+ setarch:count)))))))
(defun setarch:norm (s / i c r)
  (setq r "" i 1)
  (while (<= i (strlen s))
    (setq c (substr s i 1))
    (if (wcmatch c "[A-Za-z0-9]") (setq r (strcat r (strcase c))))
    (setq i (1+ i)))
  r)
(defun setarch:fill ( / ss i e b)
  (setq setarch:count 0)
  (if (setq ss (ssget "_X" '((0 . "TEXT,MTEXT,INSERT"))))
    (repeat (setq i (sslength ss))
      (setq e (ssname ss (setq i (1- i))))
      (if (= (cdr (assoc 0 (entget e))) "INSERT")
        (progn
          (setq b e)
          (while (and (setq b (entnext b)) (= (cdr (assoc 0 (entget b))) "ATTRIB")) (setarch:do b))
          (entupd e))
        (setarch:do e))))
  (setq b (tblnext "BLOCK" T))
  (while b
    (if (and (= 0 (logand (cdr (assoc 70 b)) 4)) (setq e (cdr (assoc -2 b))))
      (while e (setarch:do e) (setq e (entnext e))))
    (setq b (tblnext "BLOCK")))
  (if (> setarch:count 0) (command "_.REGENALL"))
  (princ (strcat "\nSETARCH-RESULT " (itoa setarch:count) "\n"))
  (princ))
""";
}
