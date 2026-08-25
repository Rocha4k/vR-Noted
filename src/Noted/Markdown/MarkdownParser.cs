using System;
using System.Collections.Generic;
using System.Text;

namespace Noted.Markdown;

// parser de linha, subconjunto de markdown que faz sentido numa sticky note.
// trabalha sobre o texto original (nao normalizado) para que os offsets sirvam de caret
public static class MarkdownParser
{
    public static List<Block> Parse(string text)
    {
        var blocks = new List<Block>();
        var lines = SplitLines(text);

        for (int i = 0; i < lines.Count; i++)
        {
            var (start, length, content) = lines[i];

            // bloco de codigo cercado: consome ate ao fecho ou ao fim
            if (content.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                var lang = content.TrimStart()[3..].Trim();
                var code = new StringBuilder();
                int end = start + length;
                int j = i + 1;
                for (; j < lines.Count; j++)
                {
                    var (s2, l2, c2) = lines[j];
                    if (c2.TrimStart().StartsWith("```", StringComparison.Ordinal))
                    {
                        end = s2 + l2;
                        break;
                    }
                    if (code.Length > 0) code.Append('\n');
                    code.Append(c2);
                    end = s2 + l2;
                }
                blocks.Add(new CodeBlock
                {
                    Language = lang,
                    Code = code.ToString(),
                    SourceStart = start,
                    SourceLength = end - start
                });
                i = j;
                continue;
            }

            blocks.Add(ParseLine(content, start, length));
        }

        return blocks;
    }

    private static Block ParseLine(string line, int start, int length)
    {
        var trimmed = line.TrimEnd();

        if (trimmed.Length == 0)
            return new BlankBlock { SourceStart = start, SourceLength = length };

        // regra horizontal
        var compact = trimmed.Replace(" ", "");
        if (compact.Length >= 3 &&
            (AllSame(compact, '-') || AllSame(compact, '*') || AllSame(compact, '_')))
            return new RuleBlock { SourceStart = start, SourceLength = length };

        int indent = 0;
        while (indent < trimmed.Length && (trimmed[indent] == ' ' || trimmed[indent] == '\t')) indent++;
        var body = trimmed[indent..];

        // titulo
        if (body.StartsWith('#'))
        {
            int level = 0;
            while (level < body.Length && body[level] == '#') level++;
            if (level <= 6 && level < body.Length && body[level] == ' ')
                return new HeadingBlock
                {
                    Level = level,
                    Text = body[(level + 1)..].Trim(),
                    SourceStart = start,
                    SourceLength = length
                };
        }

        // citacao
        if (body.StartsWith('>'))
            return new QuoteBlock
            {
                Text = body[1..].TrimStart(),
                SourceStart = start,
                SourceLength = length
            };

        // checkbox: "- [ ] " ou "- [x] " (tambem sem texto nenhum a seguir)
        if (body.Length >= 5 && (body[0] == '-' || body[0] == '*' || body[0] == '+') &&
            body[1] == ' ' && body[2] == '[' && body[4] == ']' &&
            (body[3] == ' ' || body[3] == 'x' || body[3] == 'X'))
        {
            var rest = body.Length > 5 && body[5] == ' ' ? body[6..] : body[5..];
            return new TaskBlock
            {
                Indent = indent,
                Done = body[3] is 'x' or 'X',
                Text = rest,
                MarkOffset = start + indent + 3,
                SourceStart = start,
                SourceLength = length
            };
        }

        // lista com marcador
        if (body.Length >= 2 && (body[0] == '-' || body[0] == '*' || body[0] == '+') && body[1] == ' ')
            return new ListBlock
            {
                Indent = indent,
                Marker = "•",
                Text = body[2..],
                SourceStart = start,
                SourceLength = length
            };

        // lista numerada
        int digits = 0;
        while (digits < body.Length && char.IsAsciiDigit(body[digits])) digits++;
        if (digits > 0 && digits + 1 < body.Length &&
            (body[digits] == '.' || body[digits] == ')') && body[digits + 1] == ' ')
            return new ListBlock
            {
                Indent = indent,
                Marker = body[..(digits + 1)],
                Ordered = true,
                Text = body[(digits + 2)..],
                SourceStart = start,
                SourceLength = length
            };

        return new ParagraphBlock { Text = trimmed, SourceStart = start, SourceLength = length };

        static bool AllSame(string s, char c)
        {
            foreach (var ch in s) if (ch != c) return false;
            return true;
        }
    }

    // linhas com offset absoluto; o \r do CRLF fica fora do conteudo mas dentro do comprimento
    private static List<(int Start, int Length, string Content)> SplitLines(string text)
    {
        var result = new List<(int, int, string)>();
        int pos = 0;
        while (pos <= text.Length)
        {
            int nl = text.IndexOf('\n', pos);
            int end = nl < 0 ? text.Length : nl;
            int contentEnd = end > pos && text[end - 1] == '\r' ? end - 1 : end;
            result.Add((pos, contentEnd - pos, text[pos..contentEnd]));
            if (nl < 0) break;
            pos = nl + 1;
        }
        return result;
    }

    // --- inlines ---

    public static List<Span> ParseInline(string text)
    {
        var spans = new List<Span>();
        var buffer = new StringBuilder();
        bool bold = false, italic = false, strike = false;

        void Flush()
        {
            if (buffer.Length == 0) return;
            spans.Add(new TextSpan { Text = buffer.ToString(), Bold = bold, Italic = italic, Strike = strike });
            buffer.Clear();
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            // escape
            if (c == '\\' && i + 1 < text.Length)
            {
                buffer.Append(text[i + 1]);
                i++;
                continue;
            }

            // codigo inline: literal, nada e interpretado la dentro
            if (c == '`')
            {
                int close = text.IndexOf('`', i + 1);
                if (close > i)
                {
                    Flush();
                    spans.Add(new TextSpan { Text = text[(i + 1)..close], Code = true });
                    i = close;
                    continue;
                }
            }

            // link [texto](url)
            if (c == '[')
            {
                int closeBracket = text.IndexOf(']', i + 1);
                if (closeBracket > i && closeBracket + 1 < text.Length && text[closeBracket + 1] == '(')
                {
                    int closeParen = text.IndexOf(')', closeBracket + 2);
                    if (closeParen > closeBracket)
                    {
                        Flush();
                        spans.Add(new LinkSpan
                        {
                            Text = text[(i + 1)..closeBracket],
                            Url = text[(closeBracket + 2)..closeParen]
                        });
                        i = closeParen;
                        continue;
                    }
                }
            }

            if (c == '~' && i + 1 < text.Length && text[i + 1] == '~' && Emphasis(text, i, 2, strike))
            {
                Flush();
                strike = !strike;
                i++;
                continue;
            }

            if ((c == '*' || c == '_') && i + 1 < text.Length && text[i + 1] == c && Emphasis(text, i, 2, bold))
            {
                Flush();
                bold = !bold;
                i++;
                continue;
            }

            // um '*' seguido de outro pertence a um "**" que nao chegou a fechar:
            // tratado como italico comia os dois e "**todo" aparecia como "todo"
            bool partOfPair = i + 1 < text.Length && text[i + 1] == c;

            if ((c == '*' || c == '_') && !partOfPair && Emphasis(text, i, 1, italic))
            {
                Flush();
                italic = !italic;
                continue;
            }

            buffer.Append(c);
        }

        Flush();
        return spans;
    }

    // um marcador so vale se tiver par: sem isto um "2 * 3" ou um "*" solto punham o
    // resto da linha em italico, e "snake_case" saia com o meio sublinhado a italico
    private static bool Emphasis(string text, int i, int width, bool isOpen)
    {
        char c = text[i];

        // a fechar basta nao vir logo a seguir a um espaco
        if (isOpen) return i > 0 && !char.IsWhiteSpace(text[i - 1]);

        // '_' colado a uma palavra e parte da palavra, nao enfase
        if (c == '_' && i > 0 && (char.IsLetterOrDigit(text[i - 1]) || text[i - 1] == '_')) return false;
        if (i + width >= text.Length || char.IsWhiteSpace(text[i + width])) return false;

        for (int k = i + width; k + width <= text.Length; k++)
        {
            if (char.IsWhiteSpace(text[k - 1])) continue;
            bool match = true;
            for (int w = 0; w < width; w++) if (text[k + w] != c) { match = false; break; }
            if (match) return true;
        }
        return false;
    }
}
