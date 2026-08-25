using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Noted.Markdown;

public sealed record MarkdownTheme(Brush Foreground, Brush Muted, Brush CodeBackground, Brush Rule, Brush Link)
{
    // deriva a paleta do render a partir da cor de texto e de fundo da nota
    public static MarkdownTheme From(Color foreground, Color background)
    {
        bool dark = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) < 128;

        var fg = Freeze(new SolidColorBrush(foreground));
        var muted = Freeze(new SolidColorBrush(Color.FromArgb(150, foreground.R, foreground.G, foreground.B)));
        var codeBg = Freeze(new SolidColorBrush(dark
            ? Color.FromArgb(38, 255, 255, 255)
            : Color.FromArgb(20, 0, 0, 0)));
        var rule = Freeze(new SolidColorBrush(Color.FromArgb(60, foreground.R, foreground.G, foreground.B)));
        // links tinham a cor do texto normal e ficavam indistinguiveis
        var link = Freeze(new SolidColorBrush(dark
            ? Color.FromRgb(0x8A, 0xC0, 0xFF)
            : Color.FromRgb(0x0B, 0x4F, 0xB8)));
        return new MarkdownTheme(fg, muted, codeBg, rule, link);

        static Brush Freeze(Brush b) { b.Freeze(); return b; }
    }
}

// render de markdown para elementos wpf. um StackPanel de TextBlocks e CheckBoxes:
// mais leve que FlowDocument e da checkboxes mesmo clicaveis
public sealed class MarkdownView : ScrollViewer
{
    private const string MonoFonts = "Cascadia Mono, Consolas, Courier New";

    private readonly StackPanel _panel;

    // -1 significa "fim do texto"
    public event Action<int>? EditRequested;
    public event Action<TaskBlock>? TaskToggled;

    public MarkdownTheme Theme { get; set; } =
        MarkdownTheme.From(Colors.Black, Colors.White);

    public double BodyFontSize { get; set; } = 13.5;

    public MarkdownView()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Background = Brushes.Transparent;
        BorderThickness = new Thickness(0);
        Padding = new Thickness(10, 8, 10, 8);
        Focusable = false;

        // fundo transparente e nao nulo: sem ele o painel nao recebe cliques em zona vazia
        _panel = new StackPanel { Background = Brushes.Transparent };
        Content = _panel;

        // borbulhagem, nao tunelamento: o CheckBox tem de ver o rato primeiro e marcar
        // o evento como tratado, senao o handler exterior rouba sempre o clique
        _panel.MouseLeftButtonUp += OnPanelClick;
        MouseLeftButtonUp += (_, e) =>
        {
            if (e.Handled) return;
            e.Handled = true;
            EditRequested?.Invoke(-1);
        };
    }

    public void Render(string text)
    {
        _panel.Children.Clear();
        var blocks = MarkdownParser.Parse(text);

        for (int i = 0; i < blocks.Count; i++)
        {
            var element = Build(blocks[i], i > 0 ? blocks[i - 1] : null);
            if (element is null) continue;
            element.Tag = blocks[i];
            _panel.Children.Add(element);
        }
    }

    private FrameworkElement? Build(Block block, Block? previous)
    {
        switch (block)
        {
            case BlankBlock:
                // linhas em branco seguidas colapsam num unico espaco
                if (previous is BlankBlock or null) return null;
                return new Border { Height = 6 };

            case RuleBlock:
                return new Border
                {
                    Height = 1,
                    Background = Theme.Rule,
                    Margin = new Thickness(0, 6, 0, 6)
                };

            case HeadingBlock h:
            {
                var tb = NewTextBlock(h.Text);
                tb.FontSize = h.Level switch
                {
                    1 => BodyFontSize + 5.5,
                    2 => BodyFontSize + 3,
                    3 => BodyFontSize + 1.5,
                    _ => BodyFontSize
                };
                tb.FontWeight = FontWeights.SemiBold;
                tb.Margin = new Thickness(0, previous is null ? 0 : 6, 0, 2);
                return tb;
            }

            case QuoteBlock q:
            {
                var tb = NewTextBlock(q.Text);
                tb.FontStyle = FontStyles.Italic;
                tb.Foreground = Theme.Muted;
                return new Border
                {
                    BorderBrush = Theme.Rule,
                    BorderThickness = new Thickness(2, 0, 0, 0),
                    Padding = new Thickness(8, 1, 0, 1),
                    Margin = new Thickness(0, 1, 0, 1),
                    Child = tb
                };
            }

            case CodeBlock c:
            {
                var code = new TextBlock
                {
                    Text = c.Code,
                    FontFamily = new FontFamily(MonoFonts),
                    FontSize = BodyFontSize - 1.5,
                    Foreground = Theme.Foreground,
                    TextWrapping = TextWrapping.NoWrap
                };
                var scroller = new ScrollViewer
                {
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = code,
                    Focusable = false
                };
                var panel = new StackPanel();
                if (c.Language.Length > 0)
                    panel.Children.Add(new TextBlock
                    {
                        Text = c.Language,
                        FontSize = BodyFontSize - 4,
                        Foreground = Theme.Muted,
                        Margin = new Thickness(0, 0, 0, 3)
                    });
                panel.Children.Add(scroller);

                return new Border
                {
                    Background = Theme.CodeBackground,
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 3, 0, 3),
                    Child = panel
                };
            }

            case TaskBlock t:
            {
                var box = new CheckBox
                {
                    IsChecked = t.Done,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 1.5, 6, 0),
                    Foreground = Theme.Foreground,
                    Focusable = false
                };
                // o clique na caixa alterna e nao entra em modo de edicao
                box.Click += (_, e) =>
                {
                    e.Handled = true;
                    TaskToggled?.Invoke(t);
                };

                var label = NewTextBlock(t.Text);
                if (t.Done)
                {
                    label.Foreground = Theme.Muted;
                    label.TextDecorations = TextDecorations.Strikethrough;
                }

                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(t.Indent * 8, 1, 0, 1)
                };
                row.Children.Add(box);
                row.Children.Add(label);
                return row;
            }

            case ListBlock l:
            {
                var marker = new TextBlock
                {
                    Text = l.Marker,
                    Foreground = Theme.Muted,
                    FontSize = BodyFontSize,
                    Margin = new Thickness(0, 0, 6, 0),
                    MinWidth = l.Ordered ? 16 : 8,
                    VerticalAlignment = VerticalAlignment.Top
                };
                var row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(l.Indent * 8 + 2, 1, 0, 1)
                };
                row.Children.Add(marker);
                row.Children.Add(NewTextBlock(l.Text));
                return row;
            }

            case ParagraphBlock p:
                return NewTextBlock(p.Text);

            default:
                return null;
        }
    }

    private TextBlock NewTextBlock(string markdown)
    {
        var tb = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = Theme.Foreground,
            FontSize = BodyFontSize,
            FontFamily = new FontFamily("Segoe UI")
        };

        foreach (var span in MarkdownParser.ParseInline(markdown))
        {
            switch (span)
            {
                case TextSpan t when t.Code:
                    tb.Inlines.Add(new Run(t.Text)
                    {
                        FontFamily = new FontFamily(MonoFonts),
                        FontSize = BodyFontSize - 1,
                        Background = Theme.CodeBackground
                    });
                    break;

                case TextSpan t:
                {
                    var run = new Run(t.Text);
                    if (t.Bold) run.FontWeight = FontWeights.SemiBold;
                    if (t.Italic) run.FontStyle = FontStyles.Italic;
                    if (t.Strike) run.TextDecorations = TextDecorations.Strikethrough;
                    tb.Inlines.Add(run);
                    break;
                }

                case LinkSpan link:
                {
                    var hyper = new Hyperlink(new Run(link.Text))
                    {
                        Foreground = Theme.Link,
                        TextDecorations = TextDecorations.Underline,
                        Cursor = Cursors.Hand,
                        ToolTip = link.Url
                    };
                    hyper.Click += (_, e) =>
                    {
                        e.Handled = true;
                        OpenLink(link.Url);
                    };
                    tb.Inlines.Add(hyper);
                    break;
                }
            }
        }

        return tb;
    }

    private void OnPanelClick(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled) return;

        // sobe a arvore ate encontrar o elemento que carrega o bloco
        for (var node = e.OriginalSource as DependencyObject; node is not null; node = ParentOf(node))
        {
            if (node is FrameworkElement { Tag: Block block })
            {
                e.Handled = true;
                EditRequested?.Invoke(block.SourceStart + block.SourceLength);
                return;
            }
            if (ReferenceEquals(node, _panel)) break;
        }
    }

    // clicar em texto da um Run como origem, e um Run e FrameworkContentElement, nao Visual:
    // passar isso a VisualTreeHelper.GetParent lanca excepcao
    private static DependencyObject? ParentOf(DependencyObject node) => node switch
    {
        FrameworkContentElement fce => (DependencyObject?)fce.Parent ?? ContentOperations.GetParent(fce as ContentElement),
        ContentElement ce => ContentOperations.GetParent(ce),
        Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(node),
        _ => null
    };

    private static void OpenLink(string url)
    {
        // so http/https: nao abrir file:// nem esquemas arbitrarios vindos do texto
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch { /* sem browser associado: ignora */ }
    }
}
