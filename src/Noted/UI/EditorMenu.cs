using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Noted.Markdown;

namespace Noted.UI;

// menu do botao direito dentro do editor: o mesmo que a barra flutuante, mas com nomes
internal static class EditorMenu
{
    public static ContextMenu Build(TextBox editor)
    {
        var menu = new ContextMenu();

        var format = new MenuItem { Header = "Formatar" };
        Add(format, "Negrito", "Ctrl+B", () => MarkdownFormat.ToggleInline(editor, MarkdownFormat.Bold));
        Add(format, "Italico", "Ctrl+I", () => MarkdownFormat.ToggleInline(editor, MarkdownFormat.Italic));
        Add(format, "Riscado", "Ctrl+Shift+X", () => MarkdownFormat.ToggleInline(editor, MarkdownFormat.Strike));
        format.Items.Add(new Separator());
        Add(format, "Codigo inline", "Ctrl+Shift+C", () => MarkdownFormat.ToggleInline(editor, MarkdownFormat.InlineCode));
        Add(format, "Bloco de codigo", "Ctrl+Shift+K", () => MarkdownFormat.ToggleCodeBlock(editor));
        Add(format, "Link", "Ctrl+K", () => MarkdownFormat.InsertLink(editor));
        format.Items.Add(new Separator());
        Add(format, "Limpar formatacao", "", () => MarkdownFormat.ClearInline(editor));
        menu.Items.Add(format);

        var block = new MenuItem { Header = "Paragrafo" };
        Add(block, "Titulo 1", "Ctrl+1", () => MarkdownFormat.ToggleHeading(editor, 1));
        Add(block, "Titulo 2", "Ctrl+2", () => MarkdownFormat.ToggleHeading(editor, 2));
        Add(block, "Titulo 3", "Ctrl+3", () => MarkdownFormat.ToggleHeading(editor, 3));
        block.Items.Add(new Separator());
        Add(block, "Lista", "Ctrl+Shift+L", () => MarkdownFormat.ToggleBullet(editor));
        Add(block, "Lista numerada", "Ctrl+Shift+O", () => MarkdownFormat.ToggleNumbered(editor));
        Add(block, "Checkbox", "Ctrl+Enter", () => MarkdownFormat.ToggleTask(editor));
        Add(block, "Citacao", "Ctrl+Shift+Q", () => MarkdownFormat.ToggleQuote(editor));
        menu.Items.Add(block);

        menu.Items.Add(new Separator());

        menu.Items.Add(Command("Anular", ApplicationCommands.Undo, editor));
        menu.Items.Add(Command("Refazer", ApplicationCommands.Redo, editor));
        menu.Items.Add(new Separator());
        menu.Items.Add(Command("Cortar", ApplicationCommands.Cut, editor));
        menu.Items.Add(Command("Copiar", ApplicationCommands.Copy, editor));
        menu.Items.Add(Command("Colar", ApplicationCommands.Paste, editor));
        menu.Items.Add(Command("Seleccionar tudo", ApplicationCommands.SelectAll, editor));

        return menu;

        void Add(MenuItem parent, string header, string gesture, Action action)
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture };
            // o menu rouba o foco ao editor: devolve-lho para o cursor nao se perder
            item.Click += (_, _) => { action(); editor.Focus(); };
            parent.Items.Add(item);
        }

        static MenuItem Command(string header, RoutedUICommand command, TextBox target) =>
            new() { Header = header, Command = command, CommandTarget = target };
    }
}
