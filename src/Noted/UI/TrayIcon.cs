using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;
using Noted.Services;

namespace Noted.UI;

// icone de bandeja: a app nao tem janela principal, vive aqui
public sealed class TrayIcon : IDisposable
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValue = "NOTED";

    // paleta escrita a mao: o resourcedictionary do wpf nao chega ao winforms
    private static readonly Color MenuBack = Color.FromArgb(0x23, 0x26, 0x2B);
    private static readonly Color MenuEdge = Color.FromArgb(0x3C, 0x42, 0x4B);
    private static readonly Color MenuText = Color.FromArgb(0xE8, 0xEA, 0xED);
    private static readonly Color MenuHover = Color.FromArgb(0x34, 0x3A, 0x42);
    // texto a 45% do caminho ate ao fundo: apagado sem deixar de ser legivel
    private static readonly Color MenuTextDim = Color.FromArgb(0x8F, 0x92, 0x96);
    private static readonly Color MenuCheck = Color.FromArgb(0xFF, 0xE4, 0x7A);

    private readonly NotifyIcon _icon;
    private readonly NoteManager _mgr;
    private readonly Icon _generated;

    public event Action? NewNoteRequested;
    public event Action? SearchRequested;
    public event Action? ExitRequested;

    public TrayIcon(NoteManager mgr)
    {
        _mgr = mgr;
        _generated = BuildIcon();

        var menu = new ContextMenuStrip
        {
            // a margem tem de ficar ligada: sem ela o windows nem chega a pedir o visto
            ShowImageMargin = true,
            ImageScalingSize = new Size(18, 18),
            BackColor = MenuBack,
            ForeColor = MenuText,
            Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
            Renderer = new DarkMenuRenderer(),
            Padding = new Padding(2, 4, 2, 4)
        };

        // atalho em campo proprio: o tab nao alinha com um renderer feito a mao
        menu.Items.Add(new ToolStripMenuItem("Nova nota", null, (_, _) => NewNoteRequested?.Invoke())
        {
            ShortcutKeyDisplayString = "Ctrl+Alt+N"
        });
        menu.Items.Add(new ToolStripMenuItem("Pesquisar", null, (_, _) => SearchRequested?.Invoke())
        {
            ShortcutKeyDisplayString = "Ctrl+Alt+F"
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Mostrar todas", null, (_, _) => ShowAll());
        menu.Items.Add("Esconder todas", null, (_, _) => HideAll());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Abrir pasta das notas", null, (_, _) => _mgr.RevealInExplorer());
        menu.Items.Add("Recarregar do disco", null, (_, _) => _mgr.ReloadFromDisk());

        var startup = new ToolStripMenuItem("Iniciar com o Windows")
        {
            CheckOnClick = true,
            Checked = IsStartupEnabled()
        };
        startup.CheckedChanged += (_, _) => SetStartup(startup.Checked);
        menu.Items.Add(startup);

        var taskbar = new ToolStripMenuItem("Mostrar na barra de tarefas")
        {
            CheckOnClick = true,
            Checked = Services.Settings.ShowInTaskbar,
            ToolTipText = "Poe cada nota na barra de tarefas e no alt-tab"
        };
        taskbar.CheckedChanged += (_, _) =>
        {
            Services.Settings.ShowInTaskbar = taskbar.Checked;
            // o estilo tool-window so se aplica quando a janela nasce: reabre-as
            _mgr.RefreshWindows();
        };
        menu.Items.Add(taskbar);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => ExitRequested?.Invoke());

        // linhas apertadas com o renderer proprio: o respiro poe-se item a item
        foreach (ToolStripItem item in menu.Items)
            if (item is ToolStripMenuItem entry) entry.Padding = new Padding(2, 4, 10, 4);

        _icon = new NotifyIcon
        {
            Icon = _generated,
            Text = "NOTED",
            Visible = true,
            ContextMenuStrip = menu
        };
        // o duplo clique disparava tambem os dois cliques simples: abria a pesquisa
        // e criava uma nota ao mesmo tempo. clique esquerdo pesquisa, do meio cria
        _icon.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) SearchRequested?.Invoke();
            else if (e.Button == MouseButtons.Middle) NewNoteRequested?.Invoke();
        };
    }

    // tabela de cores do menu de bandeja, a condizer com o tema escuro do wpf
    private sealed class DarkMenuColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected => MenuHover;
        public override Color MenuItemSelectedGradientBegin => MenuHover;
        public override Color MenuItemSelectedGradientEnd => MenuHover;
        // contorno igual ao realce: o preenchimento chapado ja marca a linha activa
        public override Color MenuItemBorder => MenuHover;
        public override Color MenuBorder => MenuEdge;
        public override Color MenuItemPressedGradientBegin => MenuHover;
        public override Color MenuItemPressedGradientMiddle => MenuHover;
        public override Color MenuItemPressedGradientEnd => MenuHover;
        public override Color ToolStripDropDownBackground => MenuBack;
        public override Color ImageMarginGradientBegin => MenuBack;
        public override Color ImageMarginGradientMiddle => MenuBack;
        public override Color ImageMarginGradientEnd => MenuBack;
        public override Color SeparatorDark => MenuEdge;
        public override Color SeparatorLight => MenuEdge;
        public override Color CheckBackground => MenuHover;
        public override Color CheckSelectedBackground => MenuEdge;
        public override Color CheckPressedBackground => MenuEdge;
    }

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer() : base(new DarkMenuColors())
        {
            // os cantos arredondados do tema classico destoam do resto da app
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            // o atalho vem pela mesma via com texto diferente do item: fica apagado
            var muted = !e.Item.Enabled || !string.Equals(e.Text, e.Item.Text, StringComparison.Ordinal);
            e.TextColor = muted ? MenuTextDim : MenuText;
            base.OnRenderItemText(e);
        }

        // o visto do windows e escuro e desaparece no fundo escuro: desenha-se a mao
        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var box = e.ImageRectangle;
            var g = e.Graphics;

            using (var chip = new SolidBrush(e.Item.Selected ? ColorTable.CheckSelectedBackground : ColorTable.CheckBackground))
                g.FillRectangle(chip, box);

            var mode = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(MenuCheck, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[]
                {
                    new PointF(box.Left + box.Width * 0.26f, box.Top + box.Height * 0.52f),
                    new PointF(box.Left + box.Width * 0.44f, box.Top + box.Height * 0.72f),
                    new PointF(box.Left + box.Width * 0.76f, box.Top + box.Height * 0.30f)
                });
            g.SmoothingMode = mode;
        }
    }

    private void ShowAll()
    {
        foreach (var n in _mgr.Notes.ToList()) _mgr.Show(n);
    }

    private void HideAll()
    {
        foreach (var n in _mgr.Notes.ToList()) _mgr.HideNote(n);
    }

    public void Notify(string title, string message) =>
        _icon.ShowBalloonTip(5000, title, message, ToolTipIcon.Info);

    // o .ico embebido, no tamanho que o sistema pede para a bandeja (16, 20 ou 24 conforme o dpi).
    // se falhar, cai no icone desenhado em runtime para a app nunca ficar sem bandeja
    private static Icon BuildIcon()
    {
        try
        {
            using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream("NOTED.AppIcon");
            if (stream is not null) return new Icon(stream, SystemInformation.SmallIconSize);
        }
        catch { /* recurso em falta ou corrompido: usa o desenhado */ }

        return DrawFallbackIcon();
    }

    private static Icon DrawFallbackIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            // (desenho de recurso; so corre se o .ico embebido faltar)
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var body = new SolidBrush(Color.FromArgb(255, 255, 214, 92));
            using var fold = new SolidBrush(Color.FromArgb(255, 214, 172, 48));
            using var line = new SolidBrush(Color.FromArgb(160, 70, 60, 20));

            g.FillRectangle(body, 4, 3, 24, 26);
            g.FillPolygon(fold, new[] { new Point(28, 21), new Point(28, 29), new Point(20, 29) });
            g.FillRectangle(line, 8, 9, 16, 2);
            g.FillRectangle(line, 8, 15, 16, 2);
            g.FillRectangle(line, 8, 21, 9, 2);
        }

        // Icon.FromHandle nao fica dono do HICON: sem o DestroyIcon fica pendurado
        var handle = bmp.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally { Noted.Interop.Native.DestroyIcon(handle); }
    }

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(RunValue) is not null;
    }

    private static void SetStartup(bool on)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key is null) return;
        if (on)
            key.SetValue(RunValue, $"\"{Environment.ProcessPath}\"");
        else
            key.DeleteValue(RunValue, throwOnMissingValue: false);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _generated.Dispose();
    }
}
