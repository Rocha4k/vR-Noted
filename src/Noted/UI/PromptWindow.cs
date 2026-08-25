using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;

namespace Noted.UI;

// dialogos escuros da app, construidos em codigo para reaproveitar o Theme.xaml sem mais um ficheiro xaml
internal sealed class PromptWindow : Window
{
    private readonly TextBox? _input;

    private PromptWindow(string title, string hint, string? initial, string message, string okText, bool danger, bool showCancel)
    {
        Title = "NOTED";
        Width = 380;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        FontFamily = new FontFamily("Segoe UI");
        Background = Res<Brush>("MenuBg");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        // sem isto o dwm desenha a sua moldura por cima: com WindowStyle=None sobrava uma faixa branca no topo
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 0,
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            ResizeBorderThickness = new Thickness(0),
            UseAeroCaptionButtons = false
        });

        var body = new StackPanel();

        body.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = Res<Brush>("MenuFg"),
            TextWrapping = TextWrapping.Wrap
        });

        if (!string.IsNullOrWhiteSpace(hint))
            body.Children.Add(new TextBlock
            {
                Text = hint,
                FontSize = 11.5,
                Opacity = 0.5,
                Margin = new Thickness(0, 3, 0, 0),
                Foreground = Res<Brush>("MenuFg"),
                TextWrapping = TextWrapping.Wrap
            });

        if (initial is not null)
        {
            _input = new TextBox
            {
                Text = initial,
                Style = Res<Style>("DialogInput"),
                Margin = new Thickness(0, 12, 0, 0)
            };
            body.Children.Add(_input);
        }
        else if (!string.IsNullOrWhiteSpace(message))
        {
            body.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 12.5,
                Opacity = 0.85,
                LineHeight = 18,
                Margin = new Thickness(0, 12, 0, 0),
                Foreground = Res<Brush>("MenuFg"),
                TextWrapping = TextWrapping.Wrap
            });
        }

        var ok = new Button { Content = okText, IsDefault = true, Style = Res<Style>("DialogPrimary") };
        ok.Click += (_, _) => DialogResult = true;
        if (danger) PaintDanger(ok);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };

        if (showCancel)
            row.Children.Add(new Button
            {
                Content = "Cancelar",
                IsCancel = true,
                Style = Res<Style>("DialogButton"),
                Margin = new Thickness(0, 0, 8, 0)
            });
        else
            ok.IsCancel = true; // sem botao de cancelar, o esc tem de fechar pelo proprio ok

        row.Children.Add(ok);
        body.Children.Add(row);

        Content = new Border
        {
            Background = Res<Brush>("MenuBg"),
            BorderBrush = Res<Brush>("MenuEdge"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
            Child = body
        };

        Loaded += (_, _) =>
        {
            if (_input is not null) { _input.Focus(); _input.SelectAll(); }
            else ok.Focus();
        };

        // sem barra de titulo, o corpo e que arrasta a janela
        MouseLeftButtonDown += (_, _) =>
        {
            // dragmove rebenta se o botao ja foi largado antes do evento chegar
            try { DragMove(); } catch (InvalidOperationException) { }
        };
    }

    public static string? Ask(Window owner, string title, string initial = "", string hint = "")
    {
        var w = new PromptWindow(title, hint, initial, string.Empty, "OK", false, true) { Owner = owner };
        return w.ShowDialog() == true ? w._input!.Text.Trim() : null;
    }

    public static bool Confirm(Window owner, string title, string message, string okText = "OK", bool danger = false)
    {
        var w = new PromptWindow(title, string.Empty, null, message, okText, danger, true) { Owner = owner };
        return w.ShowDialog() == true;
    }

    public static void Alert(Window owner, string title, string message)
    {
        var w = new PromptWindow(title, string.Empty, null, message, "OK", false, false) { Owner = owner };
        w.ShowDialog();
    }

    // o DialogPrimary fixa o ambar em StaticResource, por isso a variante destrutiva precisa de template proprio
    private static void PaintDanger(Button b)
    {
        var chrome = new FrameworkElementFactory(typeof(Border), "Chrome");
        chrome.SetValue(Border.BackgroundProperty, Res<Brush>("Danger"));
        chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        chrome.SetValue(Border.BorderThicknessProperty, new Thickness(0));

        var content = new FrameworkElementFactory(typeof(ContentPresenter));
        content.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        content.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        content.SetValue(FrameworkElement.MarginProperty, new Thickness(14, 0, 14, 0));
        chrome.AppendChild(content);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = chrome };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.86, "Chrome"));
        template.Triggers.Add(hover);

        b.Template = template;
        b.Foreground = Res<Brush>("DangerInk");
    }

    private static T Res<T>(string key) => (T)Application.Current.FindResource(key);
}
