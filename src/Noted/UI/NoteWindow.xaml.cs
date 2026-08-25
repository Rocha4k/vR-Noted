using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Noted.Interop;
using Noted.Markdown;
using Noted.Models;
using Noted.Services;

namespace Noted.UI;

public partial class NoteWindow : Window
{
    private const double ExpandedMinHeight = 60;

    private readonly NoteManager _mgr;
    private readonly FormatBar _format;
    private IntPtr _hwnd;
    private bool _loading = true;
    private bool _editing;
    private DateTime _lastTopmostAssert = DateTime.MinValue;

    private static readonly Brush FlashBrush = Frozen(Color.FromRgb(0xFF, 0x8A, 0x9A));
    private System.Windows.Threading.DispatcherTimer? _flashTimer;
    private Brush? _barBeforeFlash;
    private int _flashTicks;

    public Note Note { get; }

    public NoteWindow(Note note, NoteManager mgr)
    {
        Note = note;
        _mgr = mgr;
        InitializeComponent();

        Width = Note.W;
        Height = Note.H;
        if (!double.IsNaN(Note.X) && !double.IsNaN(Note.Y))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = Note.X;
            Top = Note.Y;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        Topmost = Note.Topmost;
        ShowInTaskbar = Settings.ShowInTaskbar;
        Editor.Text = Note.Body;

        _format = new FormatBar(Editor);
        var menu = EditorMenu.Build(Editor);
        // o menu de contexto tira o foco ao editor tal como o menu de opcoes da barra
        menu.Opened += (_, _) => { IsMenuOpen = true; _format.Hide(); };
        menu.Closed += (_, _) => { IsMenuOpen = false; if (_editing) Editor.Focus(); };
        Editor.ContextMenu = menu;

        Preview.EditRequested += EnterEdit;
        Preview.TaskToggled += ToggleTaskFromPreview;

        ApplyColor();
        UpdateRemindBar();
        UpdateTagsLabel();
        UpdateTitleLabel();
        UpdatePinGlyph();

        LocationChanged += (_, _) => Persist();
        SizeChanged += (_, _) => Persist();
        Deactivated += (_, _) => { if (!_format.IsInteracting) _format.Hide(); ReassertTopmost(); };
        Loaded += (_, _) => { if (Note.Rolled) ApplyRolled(true, remember: false); };
        Closed += (_, _) => { StopFlash(); _format.Dispose(); };

        _loading = false;

        // nota vazia abre logo em escrita; nota com conteudo abre em leitura
        if (string.IsNullOrWhiteSpace(Note.Body)) EnterEdit();
        else ExitEdit();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        // tool-window tira a nota do alt-tab e da barra de tarefas ao mesmo tempo:
        // com a opcao ligada tem de ficar de fora
        if (!Settings.ShowInTaskbar) Native.ApplyToolWindow(_hwnd);
        Native.SetTopmost(_hwnd, Note.Topmost);
        Native.SetOpacity(_hwnd, Note.Opacity);
        ClampToWorkArea();
        if (_editing) Editor.Focus();
    }

    // desligar um monitor deixava a nota numa coordenada que ja nao existe e sem
    // forma de lhe chegar com o rato
    private void ClampToWorkArea()
    {
        if (_hwnd == IntPtr.Zero || double.IsNaN(Left) || double.IsNaN(Top)) return;

        var monitor = Native.MonitorFromWindow(_hwnd, Native.MONITOR_DEFAULTTONEAREST);
        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        if (!Native.GetMonitorInfo(monitor, ref info)) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        if (dpi.DpiScaleX <= 0 || dpi.DpiScaleY <= 0) return;

        double left = info.rcWork.Left / dpi.DpiScaleX;
        double top = info.rcWork.Top / dpi.DpiScaleY;
        double right = info.rcWork.Right / dpi.DpiScaleX;
        double bottom = info.rcWork.Bottom / dpi.DpiScaleY;

        bool wasLoading = _loading;
        _loading = true;
        try
        {
            if (Width > right - left) Width = right - left;
            if (Height > bottom - top) Height = bottom - top;
            if (Left + Width > right) Left = right - Width;
            if (Top + Height > bottom) Top = bottom - Height;
            if (Left < left) Left = left;
            if (Top < top) Top = top;
        }
        finally { _loading = wasLoading; }
    }

    // -------- leitura / escrita --------

    // caret < 0 coloca o cursor no fim do texto
    public void EnterEdit(int caret = -1)
    {
        _editing = true;
        Preview.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Visible;
        Editor.Focus();
        Editor.CaretIndex = caret < 0
            ? Editor.Text.Length
            : Math.Clamp(caret, 0, Editor.Text.Length);
        // sem isto, clicar no fim de uma nota longa entrava em escrita com o texto no topo
        ScrollCaretIntoView();
        _format.Enable(true);
    }

    public void ExitEdit()
    {
        _editing = false;
        _format.Enable(false);
        Note.Body = Editor.Text;
        RenderPreview();
        Editor.Visibility = Visibility.Collapsed;
        Preview.Visibility = Visibility.Visible;
    }

    private void ScrollCaretIntoView()
    {
        try { Editor.ScrollToLine(Editor.GetLineIndexFromCharacterIndex(Editor.CaretIndex)); }
        catch { /* o layout ainda pode nao existir; nao vale um crash */ }
    }

    private void RenderPreview() => Preview.Render(Note.Body);

    // sair da nota fecha a edicao, mas menus e barra de formatacao tiram o foco de teclado
    // por um instante sem que se tenha saido de lado nenhum. a decisao fica para depois de
    // o wpf assentar o foco, senao abrir o menu do botao direito atirava a nota para leitura
    private void Editor_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_editing) return;

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
        {
            if (!_editing) return;
            if (IsMenuOpen || _format.IsInteracting) return;
            if (Editor.ContextMenu is { IsOpen: true }) return;
            if (Editor.IsKeyboardFocusWithin) return;
            ExitEdit();
        });
    }

    private bool IsMenuOpen { get; set; }

    // alterna a checkbox mexendo num unico caracter do texto de origem
    private void ToggleTaskFromPreview(Markdown.TaskBlock task)
    {
        var text = Note.Body;
        if (task.MarkOffset < 0 || task.MarkOffset >= text.Length) return;

        var mark = text[task.MarkOffset] == ' ' ? 'x' : ' ';
        Note.Body = string.Concat(text.AsSpan(0, task.MarkOffset), mark.ToString(),
            text.AsSpan(task.MarkOffset + 1));

        // mexe no editor pela seleccao para nao deitar fora o historico de undo
        Editor.Select(task.MarkOffset, 1);
        Editor.SelectedText = mark.ToString();
        Editor.Select(task.MarkOffset + 1, 0);

        RenderPreview();
        _mgr.MarkDirty(Note);
    }

    // -------- estado / persistencia --------

    private void Persist()
    {
        if (_loading) return;
        Note.X = Left;
        Note.Y = Top;
        Note.W = Width;
        // enrolada, a altura no ecra e a da barra: gravar isso apagava a altura real
        if (!Note.Rolled) Note.H = Height;
        Note.Body = Editor.Text;
        UpdateTitleLabel();
        _mgr.MarkDirty(Note);
    }

    public void ApplyColor()
    {
        var (bgHex, barHex, fgHex) = Palette.Get(Note.Color);
        var bgColor = Parse(bgHex);
        var fgColor = Parse(fgHex);

        Background = Brush(bgColor);

        // a piscar, a cor nova fica de parte ate o alerta acabar; escreve-la agora
        // era apaga-la no tick seguinte
        var barBrush = Brush(Parse(barHex));
        if (_flashTimer is null) Bar.Background = barBrush;
        else _barBeforeFlash = barBrush;

        Editor.Foreground = Brush(fgColor);
        Editor.CaretBrush = Brush(fgColor);
        Editor.SelectionBrush = Brush(Color.FromArgb(70, fgColor.R, fgColor.G, fgColor.B));
        RemindLabel.Foreground = Brush(fgColor);
        TagsLabel.Foreground = Brush(fgColor);

        Preview.Theme = Markdown.MarkdownTheme.From(fgColor, bgColor);
        if (!_editing) RenderPreview();

        static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex)!;

        static SolidColorBrush Brush(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }

    public void UpdateRemindBar()
    {
        if (Note.Remind is DateTime r)
        {
            RemindBar.Visibility = Visibility.Visible;
            var when = r.Date == DateTime.Today
                ? "hoje as " + r.ToString("HH:mm", CultureInfo.InvariantCulture)
                : r.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
            RemindLabel.Text = "alerta " + when;
        }
        else
        {
            RemindBar.Visibility = Visibility.Collapsed;
        }
    }

    public void UpdateTagsLabel() =>
        TagsLabel.Text = Note.Tags.Count == 0 ? "" : "#" + string.Join("  #", Note.Tags);

    // o nome escrito pelo utilizador, ou a primeira linha do texto quando nao ha nome
    public void UpdateTitleLabel()
    {
        var shown = Note.Display;
        TitleLabel.Text = shown == "(vazia)" ? "" : shown;
        TitleLabel.Opacity = Note.Name.Length > 0 ? 0.8 : 0.55;
        Title = shown == "(vazia)" ? "NOTED" : shown;   // nome na barra de tarefas e no alt-tab
    }

    private void UpdatePinGlyph()
    {
        PinBtn.Content = Note.Topmost ? "▲" : "△";
        PinBtn.Opacity = Note.Topmost ? 1.0 : 0.55;
    }

    // reafirma a banda topmost quando outra janela ganha foco (apps fullscreen roubam-na).
    // com travao: sem ele, SetWindowPos pode gerar nova desactivacao e realimentar o ciclo
    private void ReassertTopmost()
    {
        if (!Note.Topmost || _hwnd == IntPtr.Zero) return;
        var now = DateTime.UtcNow;
        if ((now - _lastTopmostAssert).TotalSeconds < 2) return;
        _lastTopmostAssert = now;
        Native.SetTopmost(_hwnd, true);
    }

    public void SetTopmostState(bool on)
    {
        Note.Topmost = on;
        Topmost = on;
        if (_hwnd != IntPtr.Zero) Native.SetTopmost(_hwnd, on);
        UpdatePinGlyph();
        _mgr.MarkDirty(Note);
    }

    public void SetOpacityState(double value)
    {
        Note.Opacity = value;
        if (_hwnd != IntPtr.Zero) Native.SetOpacity(_hwnd, value);
        _mgr.MarkDirty(Note);
    }

    // pisca a barra sem tocar na cor nem no topmost gravados: a versao antiga punha a
    // nota a "rose" e o debounce de gravacao apanhava-a assim, para sempre.
    // o estado a repor vive em campos: capturado numa variavel local, um segundo alerta
    // durante o primeiro guardava a barra ja pintada e ela ficava rosa para sempre
    public void FlashAttention()
    {
        Show();
        Activate();

        if (_flashTimer is null)
        {
            _barBeforeFlash = Bar.Background;
            _flashTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(300)
            };
            _flashTimer.Tick += FlashTick;
        }

        Topmost = true;
        if (_hwnd != IntPtr.Zero) Native.SetTopmost(_hwnd, true);

        _flashTicks = 0;
        Bar.Background = FlashBrush;
        _flashTimer.Start();
    }

    private void FlashTick(object? sender, EventArgs e)
    {
        _flashTicks++;
        Bar.Background = _flashTicks % 2 == 0 ? FlashBrush : _barBeforeFlash;
        if (_flashTicks < 7) return;
        StopFlash();
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }

    private void StopFlash()
    {
        if (_flashTimer is null) return;
        _flashTimer.Stop();
        _flashTimer.Tick -= FlashTick;
        _flashTimer = null;

        Bar.Background = _barBeforeFlash;
        // repor a partir do que esta gravado, nao de uma copia: o utilizador pode ter
        // carregado no pin a meio do piscar
        Topmost = Note.Topmost;
        if (_hwnd != IntPtr.Zero) Native.SetTopmost(_hwnd, Note.Topmost);
    }

    // -------- interaccao --------

    private void Bar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { ApplyRolled(!Note.Rolled, remember: true); return; }
        if (e.ButtonState != MouseButtonState.Pressed) return;
        // o botao pode ja ter sido largado quando isto corre, e ai DragMove lanca
        try { DragMove(); }
        catch (InvalidOperationException) { }
    }

    // enrolar ate a barra. a altura real fica guardada em Note.H para o desenrolar
    private void ApplyRolled(bool on, bool remember)
    {
        bool wasLoading = _loading;
        _loading = true;
        try
        {
            if (on)
            {
                double keep = Height;
                Note.Rolled = true;
                // a moldura de redimensionar tem de sair primeiro: e ela que conta para a
                // altura, e medi-la antes deixava uma tira de nota vazia debaixo da barra.
                // o MinHeight tambem tem de descer, senao a janela ficava presa nos 60px
                ResizeMode = ResizeMode.NoResize;
                MinHeight = 24;
                UpdateLayout();

                double bar = RolledHeight();
                if (remember && keep > bar + 4) Note.H = keep;
                MinHeight = bar;
                Height = bar;
            }
            else
            {
                double target = Note.H < ExpandedMinHeight ? 280 : Note.H;
                Note.Rolled = false;
                MinHeight = ExpandedMinHeight;
                ResizeMode = ResizeMode.CanResizeWithGrip;
                Height = target;
            }
        }
        finally { _loading = wasLoading; }

        if (remember) Persist();
    }

    private double RolledHeight()
    {
        double chrome = Math.Max(0, ActualHeight - Root.ActualHeight);
        double bar = Bar.ActualHeight > 0 ? Bar.ActualHeight : 26;
        return Math.Max(24, chrome + bar + Root.BorderThickness.Top + Root.BorderThickness.Bottom);
    }

    private void Pin_Click(object sender, RoutedEventArgs e) => SetTopmostState(!Note.Topmost);

    private void New_Click(object sender, RoutedEventArgs e) => _mgr.CreateNote(Note.Color);

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Persist();
        _mgr.HideNote(Note);
    }

    private void Editor_TextChanged(object sender, TextChangedEventArgs e) => Persist();

    // escrever em modo leitura entra em escrita e aproveita a tecla, em vez de a perder
    protected override void OnTextInput(TextCompositionEventArgs e)
    {
        base.OnTextInput(e);
        if (_editing || e.Handled) return;

        var typed = e.Text;
        if (string.IsNullOrEmpty(typed) || char.IsControl(typed[0])) return;

        EnterEdit();
        int at = Editor.CaretIndex;
        Editor.Select(at, 0);
        Editor.SelectedText = typed;
        Editor.Select(at + typed.Length, 0);
        e.Handled = true;
    }

    // atalhos da nota, validos em leitura e em escrita
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (ctrl && !shift && e.Key == Key.T) { SetTopmostState(!Note.Topmost); e.Handled = true; return; }
        if (ctrl && !shift && e.Key == Key.W) { Persist(); _mgr.HideNote(Note); e.Handled = true; return; }
        if (ctrl && !shift && e.Key == Key.S) { Persist(); _mgr.FlushDirty(); e.Handled = true; return; }
        if (ctrl && shift && e.Key == Key.N) { _mgr.CreateNote(Note.Color); e.Handled = true; return; }
        if (ctrl && !shift && e.Key == Key.E)
        {
            if (_editing) ExitEdit(); else EnterEdit();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape && _editing) { ExitEdit(); e.Handled = true; return; }

        if (!_editing && !ctrl && (e.Key == Key.Enter || e.Key == Key.Back))
        {
            EnterEdit();
            e.Handled = true;
        }
    }

    // atalhos so do editor: formatacao e continuacao de listas
    private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (ctrl && e.Key == Key.Enter) { Apply(() => MarkdownFormat.ToggleTask(Editor)); return; }

        if (ctrl && !shift)
        {
            switch (e.Key)
            {
                case Key.B: Apply(() => MarkdownFormat.ToggleInline(Editor, MarkdownFormat.Bold)); return;
                case Key.I: Apply(() => MarkdownFormat.ToggleInline(Editor, MarkdownFormat.Italic)); return;
                case Key.K: Apply(() => MarkdownFormat.InsertLink(Editor)); return;
                case Key.D1 or Key.NumPad1: Apply(() => MarkdownFormat.ToggleHeading(Editor, 1)); return;
                case Key.D2 or Key.NumPad2: Apply(() => MarkdownFormat.ToggleHeading(Editor, 2)); return;
                case Key.D3 or Key.NumPad3: Apply(() => MarkdownFormat.ToggleHeading(Editor, 3)); return;
            }
        }

        if (ctrl && shift)
        {
            switch (e.Key)
            {
                case Key.X: Apply(() => MarkdownFormat.ToggleInline(Editor, MarkdownFormat.Strike)); return;
                case Key.C: Apply(() => MarkdownFormat.ToggleInline(Editor, MarkdownFormat.InlineCode)); return;
                case Key.K: Apply(() => MarkdownFormat.ToggleCodeBlock(Editor)); return;
                case Key.L: Apply(() => MarkdownFormat.ToggleBullet(Editor)); return;
                case Key.O: Apply(() => MarkdownFormat.ToggleNumbered(Editor)); return;
                case Key.Q: Apply(() => MarkdownFormat.ToggleQuote(Editor)); return;
            }
        }

        if (e.Key == Key.Enter && !ctrl && !shift && MarkdownFormat.ContinueList(Editor))
            e.Handled = true;

        void Apply(Action action)
        {
            action();
            e.Handled = true;
            _format.Refresh();
        }
    }

    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        var menu = NoteMenu.Build(this, _mgr);
        menu.PlacementTarget = (UIElement)sender;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        // abrir o menu tira o foco ao editor; sem esta marca sairiamos do modo de escrita
        IsMenuOpen = true;
        _format.Hide();
        // devolver o foco ao editor, como no menu do botao direito
        menu.Closed += (_, _) => { IsMenuOpen = false; if (_editing) Editor.Focus(); };
        menu.IsOpen = true;
    }
}
