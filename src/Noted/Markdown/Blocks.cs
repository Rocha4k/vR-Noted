using System.Collections.Generic;

namespace Noted.Markdown;

// bloco de markdown com a posicao exacta no texto original.
// SourceStart/SourceLength permitem voltar do render para o caret certo no editor
public abstract class Block
{
    public int SourceStart;
    public int SourceLength;
}

public sealed class HeadingBlock : Block
{
    public int Level;
    public string Text = "";
}

public sealed class ParagraphBlock : Block
{
    public string Text = "";
}

public sealed class TaskBlock : Block
{
    public int Indent;
    public bool Done;
    public string Text = "";
    // posicao do caracter dentro dos parenteses rectos, para alternar sem reparsear
    public int MarkOffset;
}

public sealed class ListBlock : Block
{
    public int Indent;
    public string Marker = "-";
    public bool Ordered;
    public string Text = "";
}

public sealed class QuoteBlock : Block
{
    public string Text = "";
}

public sealed class CodeBlock : Block
{
    public string Language = "";
    public string Code = "";
}

public sealed class RuleBlock : Block;

public sealed class BlankBlock : Block;

// --- inlines ---

public abstract class Span;

public sealed class TextSpan : Span
{
    public string Text = "";
    public bool Bold;
    public bool Italic;
    public bool Strike;
    public bool Code;
}

public sealed class LinkSpan : Span
{
    public string Text = "";
    public string Url = "";
}

public static class BlockExtensions
{
    public static bool IsListLike(this Block b) => b is TaskBlock or ListBlock;

    public static IEnumerable<T> OfKind<T>(this IEnumerable<Block> blocks) where T : Block
    {
        foreach (var b in blocks)
            if (b is T t) yield return t;
    }
}
