using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Noted.Markdown;

namespace Noted.UI;

// barra que aparece por cima do texto seleccionado, com as accoes de formatacao.
// vive num Popup para poder sair dos limites da nota sem lhe mexer no layout
internal sealed class FormatBar : IDisposable
{
    private const string GlyphFonts = "Segoe UI, Segoe UI Symbol";

    private static readonly Brush Chrome = Frozen(Color.FromRgb(0x25, 0x28, 0x2D));
    private static readonly Brush Edge = Frozen(Color.FromRgb(0x44, 0x4A, 0x53));
    private static readonly Brush Ink = Frozen(Color.FromRgb(0xE6, 0xE8, 0xEB));
    private static readonly Brush Hover = Frozen(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));

    private readonly TextBox _editor;
    private readonly Popup _popup;
    private readonly Border _shell;

    private bool _enabled;
    private bool _retrying;

    // com o rato na barra o editor perde o foco de teclado, mas nao pode sair de escrita
    public bool IsInteracting { get; private set; }

    public FormatBar(TextBox editor)
    {
        _editor = editor;

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Make("B", "Negrito  (Ctrl+B)", () => MarkdownFormat.ToggleInline(_editor, MarkdownFormat.Bold),
            t => t.FontWeight = FontWeights.Bold));
        row.Children.Add(Make("I", "Italico  (Ctrl+I)", () => MarkdownFormat.ToggleInline(_editor, MarkdownFormat.Italic),
            t => { t.FontStyle = FontStyles.Italic; t.FontFamily = new FontFamily("Georgia, Segoe UI"); }));
        row.Children.Add(Make("S", "Riscado  (Ctrl+Shift+X)", () => MarkdownFormat.ToggleInline(_editor, MarkdownFormat.Strike),
            t => t.TextDecorations = TextDecorations.Strikethrough));
        row.Children.Add(Make("</>", "Codigo inline  (Ctrl+Shift+C)", () => MarkdownFormat.ToggleInline(_editor, MarkdownFormat.InlineCode),
            t => { t.FontFamily = new FontFamily("Cascadia Mono, Consolas"); t.FontSize = 10.5; }));
        row.Children.Add(Make("▤", "Bloco de codigo  (Ctrl+Shift+K)", () => MarkdownFormat.ToggleCodeBlock(_editor)));
        row.Children.Add(Make("↗", "Link  (Ctrl+K)", () => MarkdownFormat.InsertLink(_editor)));

        row.Children.Add(Divider());

        row.Children.Add(Make("H", "Titulo  (Ctrl+1 a Ctrl+3)", () => MarkdownFormat.ToggleHeading(_editor, 1),
            t => t.FontWeight = FontWeights.Bold));
        row.Children.Add(Make("•", "Lista  (Ctrl+Shift+L)", () => MarkdownFormat.ToggleBullet(_editor),
            t => t.FontSize = 15));
        row.Children.Add(Make("1.", "Lista numerada  (Ctrl+Shift+O)", () => MarkdownFormat.ToggleNumbered(_editor),
            t => t.FontSize = 11));
        row.Children.Add(Make("☑", "Checkbox  (Ctrl+Enter)", () => MarkdownFormat.ToggleTask(_editor),
            t => t.FontSize = 14));
        row.Children.Add(Make("”", "Citacao  (Ctrl+Shift+Q)", () => MarkdownFormat.ToggleQuote(_editor),
            t => { t.FontSize = 17; t.Margin = new Thickness(0, -5, 0, 0); }));

        row.Children.Add(Divider());

        row.Children.Add(Make("✕", "Limpar formatacao", () => MarkdownFormat.ClearInline(_editor),
            t => t.FontSize = 11));

        _shell = new Border
        {
            Background = Chrome,
            BorderBrush = Edge,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(3),
            Child = row
        };
        _shell.PreviewMouseDown += (_, _) => IsInteracting = true;
        _shell.PreviewMouseUp += (_, _) =>
            _shell.Dispatcher.BeginInvoke(DispatcherPriority.Background, () => IsInteracting = false);

        _popup = new Popup
        {
            Child = _shell,
            AllowsTransparency = true,
            Placement = PlacementMode.Relative,
            PlacementTarget = _editor,
            StaysOpen = true,
            Focusable = false
        };

        _editor.SelectionChanged += (_, _) => Refresh();
        // durante o arrasto da seleccao a barra so estorvava: aparece quando o rato larga
        _editor.PreviewMouseLeftButtonUp += (_, _) =>
            _editor.Dispatcher.BeginInvoke(DispatcherPriority.Background, Refresh);
    }

    public void Enable(bool on)
    {
        _enabled = on;
        if (!on) Hide();
        else Refresh();
    }

    public void Hide()
    {
        IsInteracting = false;
        _popup.IsOpen = false;
    }

    public void Refresh()
    {
        if (!_enabled || _editor.SelectionLength == 0) { Hide(); return; }
        if (Mouse.LeftButton == MouseButtonState.Pressed && !IsInteracting) { Hide(); return; }

        if (!Place())
        {
            Hide();
            // entrar em escrita e seleccionar no mesmo instante nao da tempo ao editor de
            // se medir, e sem medidas nao ha onde por a barra: tenta de novo apos o render.
            // uma tentativa so -- limpar a marca antes do Refresh punha isto a reagendar-se
            // para sempre sempre que o editor nao tem medidas, e la se ia o idle a 0%
            if (_retrying) return;
            _retrying = true;
            _editor.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
            {
                Refresh();
                _retrying = false;
            });
            return;
        }

        _popup.IsOpen = true;
    }

    private bool Place()
    {
        var a = _editor.GetRectFromCharacterIndex(_editor.SelectionStart);
        var b = _editor.GetRectFromCharacterIndex(_editor.SelectionStart + _editor.SelectionLength, true);
        if (!Usable(a)) return false;
        if (!Usable(b)) b = a;

        _shell.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = _shell.DesiredSize;
        if (size.Width <= 0 || size.Height <= 0) return false;

        // seleccao numa linha so: centra sobre ela. em varias linhas: alinha pelo inicio
        bool oneLine = Math.Abs(a.Top - b.Top) < 0.5;
        double anchor = oneLine ? (a.Left + b.Left) / 2 : a.Left;

        double maxX = Math.Max(2, _editor.ActualWidth - size.Width - 2);
        double x = Math.Clamp(anchor - size.Width / 2, 2, maxX);

        double y = Math.Min(a.Top, b.Top) - size.Height - 4;
        // sem espaco por cima, passa para debaixo da seleccao
        if (y < 2) y = Math.Max(a.Bottom, b.Bottom) + 4;

        _popup.HorizontalOffset = x;
        _popup.VerticalOffset = y;
        return true;

        static bool Usable(Rect r) =>
            !r.IsEmpty && !double.IsNaN(r.Top) && !double.IsInfinity(r.Top) &&
            !double.IsNaN(r.Left) && !double.IsInfinity(r.Left);
    }

    private FrameworkElement Make(string glyph, string tip, Action action, Action<TextBlock>? style = null)
    {
        var label = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily(GlyphFonts),
            FontSize = 12.5,
            Foreground = Ink,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        style?.Invoke(label);

        var host = new Border
        {
            Width = 26,
            Height = 24,
            CornerRadius = new CornerRadius(4),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = tip,
            Child = label
        };
        host.MouseEnter += (_, _) => host.Background = Hover;
        host.MouseLeave += (_, _) => host.Background = Brushes.Transparent;
        host.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            action();
            _editor.Focus();
            IsInteracting = false;
            Refresh();
        };
        return host;
    }

    private static FrameworkElement Divider() => new Border
    {
        Width = 1,
        Margin = new Thickness(4, 4, 4, 4),
        Background = Edge
    };

    private static Brush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    public void Dispose() => Hide();
}
