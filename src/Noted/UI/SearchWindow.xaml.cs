using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Noted.Interop;
using Noted.Models;
using Noted.Services;

namespace Noted.UI;

public partial class SearchWindow : Window
{
    public sealed record Hit(Note Note, string Title, string Meta);

    private readonly NoteManager _mgr;
    private readonly ObservableCollection<Hit> _hits = new();

    public SearchWindow(NoteManager mgr)
    {
        _mgr = mgr;
        InitializeComponent();
        Results.ItemsSource = _hits;
        Loaded += (_, _) => { Query.Focus(); Refresh(); };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // a paleta nao tem que aparecer no alt-tab
        Native.ApplyToolWindow(new WindowInteropHelper(this).Handle);
    }

    private void Query_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        var (tags, words) = SplitQuery(Query.Text);
        _hits.Clear();

        foreach (var n in _mgr.Notes)
        {
            if (tags.Count > 0 && !tags.All(t => n.Tags.Any(nt => nt.Contains(t, StringComparison.OrdinalIgnoreCase))))
                continue;
            if (words.Count > 0 && !words.All(w => n.Body.Contains(w, StringComparison.OrdinalIgnoreCase)))
                continue;

            _hits.Add(new Hit(n, n.Display, Meta(n)));
        }

        if (_hits.Count > 0) Results.SelectedIndex = 0;

        Hint.Visibility = Query.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Empty.Visibility = _hits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Empty.Text = _mgr.Notes.Count == 0
            ? "Ainda nao ha notas. Ctrl+Enter cria a primeira."
            : "Nenhuma nota corresponde. Ctrl+Enter cria uma com este texto.";

        string Meta(Note n)
        {
            var parts = new List<string>();
            if (n.Tags.Count > 0) parts.Add("#" + string.Join(" #", n.Tags));
            if (n.Remind is DateTime r)
                parts.Add("alerta " + r.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture));

            int open = n.Body.Split("- [ ]").Length - 1;
            int done = n.Body.Split("- [x]").Length - 1;
            if (open + done > 0) parts.Add($"{done}/{open + done} feitas");

            parts.Add(_mgr.IsOpen(n) ? "no ecra" : "fechada");
            parts.Add(n.Color);
            return string.Join("  ·  ", parts);
        }
    }

    private static (List<string> Tags, List<string> Words) SplitQuery(string query)
    {
        var tags = new List<string>();
        var words = new List<string>();
        foreach (var token in query.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith('#') && token.Length > 1) tags.Add(token[1..]);
            else words.Add(token);
        }
        return (tags, words);
    }

    private void Query_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;

        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;

            case Key.Down:
                Move(1);
                e.Handled = true;
                break;

            case Key.Up:
                Move(-1);
                e.Handled = true;
                break;

            case Key.Enter when ctrl:
                CreateFromQuery();
                e.Handled = true;
                break;

            case Key.Enter:
                OpenSelected();
                e.Handled = true;
                break;
        }

        void Move(int delta)
        {
            if (_hits.Count == 0) return;
            int i = (Results.SelectedIndex + delta + _hits.Count) % _hits.Count;
            Results.SelectedIndex = i;
            Results.ScrollIntoView(_hits[i]);
        }
    }

    // os #tag escritos na pesquisa entram como tags da nota nova em vez de irem para o corpo
    private void CreateFromQuery()
    {
        var (tags, words) = SplitQuery(Query.Text);
        var win = _mgr.CreateNote("amber", string.Join(" ", words));
        if (tags.Count > 0)
        {
            win.Note.Tags.AddRange(tags);
            win.UpdateTagsLabel();
            _mgr.MarkDirty(win.Note);
        }
        Close();
    }

    private void Results_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelected();

    private void OpenSelected()
    {
        if (Results.SelectedItem is not Hit hit) return;
        _mgr.Show(hit.Note);
        Close();
    }

    private void Window_Deactivated(object sender, EventArgs e) => Close();
}
