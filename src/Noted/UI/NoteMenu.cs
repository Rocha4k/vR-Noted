using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Noted.Models;
using Noted.Services;

namespace Noted.UI;

// menu de opcoes da nota, construido em codigo para nao inchar o xaml
internal static class NoteMenu
{
    public static ContextMenu Build(NoteWindow win, NoteManager mgr)
    {
        var note = win.Note;
        var menu = new ContextMenu();

        // nome da nota: aparece na barra do titulo e na pesquisa
        var rename = new MenuItem { Header = "Nome..." };
        rename.Click += (_, _) =>
        {
            var input = PromptWindow.Ask(win, "Nome da nota", note.Name, "vazio usa a primeira linha do texto");
            if (input is null) return;
            note.Name = input;
            win.UpdateTitleLabel();
            mgr.MarkDirty(note);
        };
        menu.Items.Add(rename);

        menu.Items.Add(new Separator());

        // cores
        var colors = new MenuItem { Header = "Cor" };
        foreach (var name in Palette.Colors.Keys)
        {
            var (bg, bar, _) = Palette.Get(name);
            var swatch = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(3),
                Background = Freeze(bg),
                BorderBrush = Freeze(bar),
                BorderThickness = new Thickness(1)
            };
            // a amostra e que assinala a cor activa: um visto no texto ficava a lutar
            // com o icone pela mesma coluna do menu
            bool current = note.Color == name;
            if (current)
            {
                swatch.BorderBrush = (Brush)Application.Current.FindResource("Accent");
                swatch.BorderThickness = new Thickness(2);
                swatch.Width = 16;
                swatch.Height = 16;
            }

            var item = new MenuItem
            {
                Header = name,
                Icon = swatch,
                FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal
            };
            item.Click += (_, _) => { note.Color = name; win.ApplyColor(); mgr.MarkDirty(note); };
            colors.Items.Add(item);
        }
        menu.Items.Add(colors);

        // opacidade
        var opacity = new MenuItem { Header = "Opacidade" };
        foreach (var v in new[] { 1.0, 0.9, 0.75, 0.6, 0.45 })
        {
            var item = new MenuItem
            {
                Header = ((int)(v * 100)).ToString(CultureInfo.InvariantCulture) + "%",
                IsChecked = Math.Abs(note.Opacity - v) < 0.01
            };
            item.Click += (_, _) => win.SetOpacityState(v);
            opacity.Items.Add(item);
        }
        menu.Items.Add(opacity);

        var pin = new MenuItem { Header = "Sempre por cima", IsCheckable = true, IsChecked = note.Topmost };
        pin.Click += (_, _) => win.SetTopmostState(pin.IsChecked);
        menu.Items.Add(pin);

        menu.Items.Add(new Separator());

        // alertas
        var remind = new MenuItem { Header = "Alerta" };
        AddPreset(remind, "daqui a 5 min", TimeSpan.FromMinutes(5));
        AddPreset(remind, "daqui a 15 min", TimeSpan.FromMinutes(15));
        AddPreset(remind, "daqui a 1 hora", TimeSpan.FromHours(1));
        AddPreset(remind, "daqui a 3 horas", TimeSpan.FromHours(3));

        var tomorrow = new MenuItem { Header = "amanha as 09:00" };
        tomorrow.Click += (_, _) => SetRemind(DateTime.Today.AddDays(1).AddHours(9));
        remind.Items.Add(tomorrow);

        var custom = new MenuItem { Header = "data/hora..." };
        custom.Click += (_, _) =>
        {
            var suggested = (note.Remind ?? DateTime.Now.AddHours(1))
                .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            var input = PromptWindow.Ask(win, "Alerta", suggested, "aaaa-mm-dd hh:mm");
            if (input is null) return;
            if (DateTime.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                SetRemind(dt);
            else
                PromptWindow.Alert(win, "Data invalida", "Usa o formato aaaa-mm-dd hh:mm.");
        };
        remind.Items.Add(custom);

        if (note.Remind is not null)
        {
            remind.Items.Add(new Separator());
            var clear = new MenuItem { Header = "remover alerta" };
            clear.Click += (_, _) => SetRemind(null);
            remind.Items.Add(clear);
        }
        menu.Items.Add(remind);

        // tags
        var tags = new MenuItem { Header = "Tags..." };
        tags.Click += (_, _) =>
        {
            var input = PromptWindow.Ask(win, "Tags", string.Join(", ", note.Tags), "separadas por virgula");
            if (input is null) return;
            note.Tags.Clear();
            foreach (var t in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                note.Tags.Add(t.TrimStart('#'));
            win.UpdateTagsLabel();
            mgr.MarkDirty(note);
        };
        menu.Items.Add(tags);

        menu.Items.Add(new Separator());

        var copy = new MenuItem { Header = "Copiar texto" };
        copy.Click += (_, _) =>
        {
            // a area de transferencia pode estar tomada por outra app
            try { Clipboard.SetText(note.Body); } catch { }
        };
        menu.Items.Add(copy);

        var duplicate = new MenuItem { Header = "Duplicar" };
        duplicate.Click += (_, _) =>
        {
            var clone = mgr.CreateNote(note.Color, note.Body);
            clone.Note.Tags.AddRange(note.Tags);
            clone.UpdateTagsLabel();
            mgr.MarkDirty(clone.Note);
        };
        menu.Items.Add(duplicate);

        var reveal = new MenuItem { Header = "Abrir pasta das notas" };
        reveal.Click += (_, _) => mgr.RevealInExplorer(note);
        menu.Items.Add(reveal);

        var delete = new MenuItem { Header = "Apagar nota" };
        delete.Click += (_, _) =>
        {
            if (PromptWindow.Confirm(win, "Apagar nota", "Esta nota vai ser apagada definitivamente.", "Apagar", danger: true))
                mgr.DeleteNote(note);
        };
        menu.Items.Add(delete);

        return menu;

        void AddPreset(MenuItem parent, string label, TimeSpan delta)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => SetRemind(DateTime.Now + delta);
            parent.Items.Add(item);
        }

        void SetRemind(DateTime? when)
        {
            note.Remind = when;
            win.UpdateRemindBar();
            mgr.MarkDirty(note);
            mgr.RescheduleReminders();
        }

        static SolidColorBrush Freeze(string hex)
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
            b.Freeze();
            return b;
        }
    }
}
