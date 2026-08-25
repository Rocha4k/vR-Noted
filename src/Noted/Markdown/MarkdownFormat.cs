using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace Noted.Markdown;

// transformacoes de markdown sobre o texto cru da nota.
// tudo passa por Select + SelectedText: escrever em TextBox.Text limpa a pilha de undo
public static class MarkdownFormat
{
    public const string Bold = "**";
    public const string Italic = "*";
    public const string Strike = "~~";
    public const string InlineCode = "`";

    private static readonly Regex TaskMark = new(@"^[-*+] \[[ xX]\] ", RegexOptions.Compiled);
    private static readonly Regex HeadMark = new(@"^#{1,6} ", RegexOptions.Compiled);
    private static readonly Regex QuoteMark = new(@"^> ?", RegexOptions.Compiled);
    private static readonly Regex BulletMark = new(@"^[-*+] ", RegexOptions.Compiled);
    private static readonly Regex OrderedMark = new(@"^\d+[.)] ", RegexOptions.Compiled);
    private static readonly Regex Fence = new(@"^\s*```", RegexOptions.Compiled);
    private static readonly Regex InlineMarks = new(@"\*\*|~~|[*`]", RegexOptions.Compiled);

    // -------- inline --------

    public static void ToggleInline(TextBox box, string marker)
    {
        var text = box.Text;
        int start = box.SelectionStart;
        int len = box.SelectionLength;

        // sem seleccao aplica a palavra debaixo do cursor
        if (len == 0) (start, len) = WordAt(text, start);

        // marcadores colados a espacos nao contam como enfase em markdown
        while (len > 0 && char.IsWhiteSpace(text[start])) { start++; len--; }
        while (len > 0 && char.IsWhiteSpace(text[start + len - 1])) len--;

        if (len == 0)
        {
            // seleccao so de espacos: insere os marcadores sem lhe tocar. passar aqui o
            // SelectionLength original comia a seleccao, e com ela a quebra de linha
            int caret = box.SelectionStart;
            Replace(box, caret, 0, marker + marker, caret + marker.Length, 0);
            return;
        }

        var sel = text.Substring(start, len);
        int m = marker.Length;

        // marcadores dentro da seleccao
        if (IsWrapped(sel, marker))
        {
            var inner = sel[m..^m];
            Replace(box, start, len, inner, start, inner.Length);
            return;
        }

        // marcadores mesmo a colar por fora
        if (IsWrappedOutside(text, start, len, marker))
        {
            Replace(box, start - m, len + 2 * m, sel, start - m, len);
            return;
        }

        Replace(box, start, len, marker + sel + marker, start + m, len);
    }

    public static void ClearInline(TextBox box)
    {
        var text = box.Text;
        int start = box.SelectionStart;
        int len = box.SelectionLength;

        if (len == 0)
        {
            (start, len) = WordAt(text, start);
            // a palavra sozinha nao traz os marcadores: sem os apanhar tambem, limpar a
            // formatacao com o cursor dentro de "**palavra**" nao fazia rigorosamente nada
            while (start > 0 && IsInlineMark(text[start - 1])) { start--; len++; }
            while (start + len < text.Length && IsInlineMark(text[start + len])) len++;
        }
        if (len == 0) return;

        var clean = InlineMarks.Replace(text.Substring(start, len), "");
        Replace(box, start, len, clean, start, clean.Length);
    }

    private static bool IsInlineMark(char c) => c is '*' or '~' or '`';

    public static void InsertLink(TextBox box)
    {
        var text = box.Text;
        int start = box.SelectionStart;
        int len = box.SelectionLength;
        if (len == 0) (start, len) = WordAt(text, start);

        var sel = len > 0 ? text.Substring(start, len) : "";
        string label = sel, url;
        // seleccionar um endereco produz o link ao contrario: texto por preencher, url ja la
        if (LooksLikeUrl(sel)) { label = ""; url = sel; }
        else url = ClipboardUrl();
        if (url.Length == 0) url = "https://";

        var replacement = $"[{label}]({url})";
        // deixa seleccionada a parte que ainda falta preencher
        int selStart = label.Length == 0 ? start + 1 : start + label.Length + 3;
        int selLength = label.Length == 0 ? 0 : url.Length;
        Replace(box, start, len, replacement, selStart, selLength);
    }

    // -------- linha --------

    public static void ToggleHeading(TextBox box, int level)
    {
        var hashes = new string('#', level);
        ApplyLineMarker(box, m => m.TrimEnd() == hashes, hashes + " ");
    }

    public static void ToggleQuote(TextBox box) =>
        ApplyLineMarker(box, m => QuoteMark.IsMatch(m), "> ");

    public static void ToggleBullet(TextBox box) =>
        ApplyLineMarker(box, m => BulletMark.IsMatch(m) && !TaskMark.IsMatch(m), "- ");

    public static void ToggleNumbered(TextBox box)
    {
        Transform(box, lines =>
        {
            bool any = false, all = true;
            foreach (var l in lines)
            {
                if (l.Trim().Length == 0) continue;
                any = true;
                if (!OrderedMark.IsMatch(SplitLine(l).Marker)) { all = false; break; }
            }

            var result = new List<string>(lines.Count);
            int n = 1;
            foreach (var l in lines)
            {
                if (l.Trim().Length == 0 && lines.Count > 1) { result.Add(l); continue; }
                var (indent, _, rest) = SplitLine(l);
                result.Add(any && all ? indent + rest : indent + n++ + ". " + rest);
            }
            return result;
        });
    }

    // linhas que ja sao checkbox alternam feito/por fazer; as outras passam a checkbox
    public static void ToggleTask(TextBox box)
    {
        Transform(box, lines =>
        {
            bool any = false, allTasks = true, allDone = true;
            foreach (var l in lines)
            {
                if (l.Trim().Length == 0) continue;
                any = true;
                var mark = SplitLine(l).Marker;
                if (!TaskMark.IsMatch(mark)) { allTasks = false; continue; }
                if (mark.Contains("[ ]", StringComparison.Ordinal)) allDone = false;
            }

            var result = new List<string>(lines.Count);
            foreach (var l in lines)
            {
                if (l.Trim().Length == 0 && lines.Count > 1) { result.Add(l); continue; }
                var (indent, _, rest) = SplitLine(l);
                var mark = any && allTasks && !allDone ? "- [x] " : "- [ ] ";
                result.Add(indent + mark + rest);
            }
            return result;
        });
    }

    public static void ToggleCodeBlock(TextBox box)
    {
        var text = box.Text;
        var newline = Newline(text);
        var spans = LineSpans(text);
        var (from, to) = LineRange(text, box.SelectionStart, box.SelectionLength);

        // dentro de um bloco ja cercado? tira as cercas. procurar o par no documento
        // inteiro e nao so dentro da seleccao: com o cursor pousado la dentro e sem
        // seleccao nenhuma, a versao antiga cercava outra vez e ia aninhando
        var (open, close) = EnclosingFence(text, spans, from, to);
        if (open >= 0)
        {
            var inner = new List<string>(Math.Max(0, close - open - 1));
            for (int i = open + 1; i < close; i++) inner.Add(text[spans[i].Start..spans[i].End]);

            var undone = string.Join(newline, inner);
            int blockStart = spans[open].Start;
            int blockEnd = spans[close].End;

            // com seleccao fica o conteudo seleccionado; sem ela o cursor mantem-se onde
            // estava, so recuado o tanto que a cerca de abertura ocupava
            if (box.SelectionLength > 0)
            {
                Replace(box, blockStart, blockEnd - blockStart, undone, blockStart, undone.Length);
            }
            else
            {
                int shift = spans[Math.Min(open + 1, close)].Start - blockStart;
                int caret = Math.Clamp(box.SelectionStart - shift, blockStart, blockStart + undone.Length);
                Replace(box, blockStart, blockEnd - blockStart, undone, caret, 0);
            }
            return;
        }

        var wrapped = "```" + newline + text[from..to] + newline + "```";
        // cursor logo a seguir a cerca de abertura, pronto a escrever a linguagem
        Replace(box, from, to - from, wrapped, from + 3, 0);
    }

    // par de cercas que envolve o intervalo, ou (-1, -1)
    private static (int Open, int Close) EnclosingFence(
        string text, List<(int Start, int End)> spans, int from, int to)
    {
        int open = -1;
        for (int i = 0; i < spans.Count; i++)
        {
            if (!Fence.IsMatch(text[spans[i].Start..spans[i].End])) continue;
            if (open < 0) { open = i; continue; }
            if (spans[open].Start <= from && to <= spans[i].End) return (open, i);
            open = -1;
        }
        return (-1, -1);
    }

    // inicio e fim de cada linha, ja sem o \r do CRLF
    private static List<(int Start, int End)> LineSpans(string text)
    {
        var spans = new List<(int, int)>();
        int pos = 0;
        while (true)
        {
            int nl = text.IndexOf('\n', pos);
            int end = nl < 0 ? text.Length : nl;
            spans.Add((pos, end > pos && text[end - 1] == '\r' ? end - 1 : end));
            if (nl < 0) break;
            pos = nl + 1;
        }
        return spans;
    }

    // segue a convencao do documento; sem quebras nenhumas usa a do TextBox
    private static string Newline(string text)
    {
        int lf = text.IndexOf('\n');
        if (lf < 0) return "\r\n";
        return lf > 0 && text[lf - 1] == '\r' ? "\r\n" : "\n";
    }

    // enter continua a lista ou o checkbox da linha anterior. devolve true se tratou a tecla
    public static bool ContinueList(TextBox box)
    {
        var text = box.Text;
        var (from, to) = LineRange(text, box.SelectionStart, 0);
        var line = text[from..to];
        var (indent, marker, rest) = SplitLine(line);

        if (marker.Length == 0 || HeadMark.IsMatch(marker)) return false;

        // cursor ainda dentro do marcador: enter so parte a linha. sem isto, um enter no
        // inicio de "- item" devolvia "- - item"
        int caret = Math.Clamp(box.SelectionStart, from, to);
        if (caret < from + indent.Length + marker.Length) return false;

        string next;
        if (TaskMark.IsMatch(marker)) next = "- [ ] ";
        else if (OrderedMark.IsMatch(marker))
        {
            var digits = marker[..^2];
            next = int.TryParse(digits, out var n) && n < int.MaxValue
                ? (n + 1) + marker[^2..]
                : marker;
        }
        else next = marker;

        // linha so com o marcador: limpa-a em vez de continuar a lista
        if (rest.Trim().Length == 0)
        {
            Replace(box, from, to - from, indent, from + indent.Length, 0);
            return true;
        }

        var insert = Newline(text) + indent + next;
        Replace(box, caret, box.SelectionLength, insert, caret + insert.Length, 0);
        return true;
    }

    // -------- interno --------

    private static void Replace(TextBox box, int start, int length, string text, int selStart, int selLength)
    {
        start = Math.Clamp(start, 0, box.Text.Length);
        length = Math.Clamp(length, 0, box.Text.Length - start);
        box.Select(start, length);
        box.SelectedText = text;

        selStart = Math.Clamp(selStart, 0, box.Text.Length);
        selLength = Math.Clamp(selLength, 0, box.Text.Length - selStart);
        box.Select(selStart, selLength);
    }

    private static void Transform(TextBox box, Func<IReadOnlyList<string>, IReadOnlyList<string>> map)
    {
        var text = box.Text;
        int selStart = box.SelectionStart;
        int selLen = box.SelectionLength;
        var (from, to) = LineRange(text, selStart, selLen);

        var input = text[from..to].Split('\n');
        var carriage = new bool[input.Length];
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i].EndsWith('\r')) { carriage[i] = true; input[i] = input[i][..^1]; }
        }

        var output = map(input);
        var sb = new StringBuilder();
        for (int i = 0; i < output.Count; i++)
        {
            sb.Append(output[i]);
            if (i < carriage.Length && carriage[i]) sb.Append('\r');
            if (i < output.Count - 1) sb.Append('\n');
        }

        var replacement = sb.ToString();
        if (selLen == 0)
        {
            int caret = selStart + (replacement.Length - (to - from));
            Replace(box, from, to - from, replacement, Math.Max(from, caret), 0);
        }
        else
        {
            Replace(box, from, to - from, replacement, from, replacement.Length);
        }
    }

    private static void ApplyLineMarker(TextBox box, Func<string, bool> has, string marker)
    {
        Transform(box, lines =>
        {
            bool any = false, all = true;
            foreach (var l in lines)
            {
                if (l.Trim().Length == 0) continue;
                any = true;
                if (!has(SplitLine(l).Marker)) { all = false; break; }
            }

            var result = new List<string>(lines.Count);
            foreach (var l in lines)
            {
                if (l.Trim().Length == 0 && lines.Count > 1) { result.Add(l); continue; }
                var (indent, _, rest) = SplitLine(l);
                result.Add(any && all ? indent + rest : indent + marker + rest);
            }
            return result;
        });
    }

    // linhas inteiras que a seleccao toca; uma seleccao que acaba no inicio da linha
    // seguinte nao arrasta essa linha para dentro
    private static (int From, int To) LineRange(string text, int selStart, int selLen)
    {
        selStart = Math.Clamp(selStart, 0, text.Length);
        int selEnd = Math.Clamp(selStart + selLen, selStart, text.Length);
        if (selEnd > selStart && text[selEnd - 1] == '\n') selEnd--;
        if (selEnd > selStart && text[selEnd - 1] == '\r') selEnd--;

        int from = text.LastIndexOf('\n', Math.Max(0, selStart - 1)) + 1;
        int to = text.IndexOf('\n', selEnd);
        if (to < 0) to = text.Length;
        if (to > from && text[to - 1] == '\r') to--;
        return (from, Math.Max(from, to));
    }

    // parte a linha em indentacao + marcador de inicio + resto
    private static (string Indent, string Marker, string Tail) SplitLine(string line)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        var indent = line[..i];
        var rest = line[i..];

        foreach (var rx in new[] { TaskMark, HeadMark, BulletMark, OrderedMark, QuoteMark })
        {
            var m = rx.Match(rest);
            if (m.Success && m.Length > 0) return (indent, m.Value, rest[m.Length..]);
        }
        return (indent, "", rest);
    }

    private static (int Start, int Length) WordAt(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        int s = caret, e = caret;
        while (s > 0 && IsWordChar(text[s - 1])) s--;
        while (e < text.Length && IsWordChar(text[e])) e++;
        return (s, e - s);
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    // "*" nao pode apanhar metade do "**" do negrito, por isso o marcador tem de
    // ter exactamente este comprimento de cada lado
    // o mesmo, mas olhando um caracter para la de cada marcador no texto completo:
    // sem isso, pedir italico dentro de "**negrito**" roubava-lhe um asterisco de cada lado
    private static bool IsWrappedOutside(string text, int start, int len, string marker)
    {
        int m = marker.Length;
        if (start < m || start + len + m > text.Length) return false;
        if (string.CompareOrdinal(text, start - m, marker, 0, m) != 0) return false;
        if (string.CompareOrdinal(text, start + len, marker, 0, m) != 0) return false;

        char c = marker[0];
        if (start - m - 1 >= 0 && text[start - m - 1] == c) return false;
        if (start + len + m < text.Length && text[start + len + m] == c) return false;
        return true;
    }

    private static bool IsWrapped(string s, string marker)
    {
        int m = marker.Length;
        if (s.Length < 2 * m) return false;
        if (string.CompareOrdinal(s, 0, marker, 0, m) != 0) return false;
        if (string.CompareOrdinal(s, s.Length - m, marker, 0, m) != 0) return false;
        if (s.Length > 2 * m && (s[m] == marker[0] || s[^(m + 1)] == marker[0])) return false;
        return true;
    }

    private static bool LooksLikeUrl(string s) =>
        s.Length > 0 && !s.Contains(' ') &&
        (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
         s.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
         s.StartsWith("www.", StringComparison.OrdinalIgnoreCase));

    private static string ClipboardUrl()
    {
        // a area de transferencia pode estar tomada por outra app: nunca rebentar por isso
        try
        {
            if (!Clipboard.ContainsText()) return "";
            var t = Clipboard.GetText().Trim();
            return LooksLikeUrl(t) ? t : "";
        }
        catch { return ""; }
    }
}
