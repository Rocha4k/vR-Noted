using System;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using Noted.Interop;
using Noted.Services;
using Noted.Storage;
using Noted.UI;

namespace Noted;

public partial class App : Application
{
    private static Mutex? _single;

    private NoteManager? _mgr;
    private TrayIcon? _tray;
    private HotKeyManager? _hotkeys;
    private SearchWindow? _search;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // instancia unica: uma segunda execucao sai em silencio
        _single = new Mutex(true, @"Local\NOTED.SingleInstance", out bool first);
        if (!first) { Shutdown(); return; }

        // uma excepcao num handler nao pode matar a app e levar notas por gravar
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            _mgr?.FlushDirty();
            LogCrash(args.Exception);
        };

        // logout ou encerramento do windows nao esperam pelo debounce de 700 ms
        SessionEnding += (_, _) => _mgr?.FlushDirty();

        _mgr = new NoteManager(new NoteStore());

        _tray = new TrayIcon(_mgr);
        _tray.NewNoteRequested += () => _mgr.CreateNote();
        _tray.SearchRequested += OpenSearch;
        _tray.ExitRequested += Shutdown;

        _hotkeys = new HotKeyManager();
        var taken = new System.Collections.Generic.List<string>();
        if (!_hotkeys.Register(Native.MOD_CONTROL | Native.MOD_ALT, Key.N, () => _mgr.CreateNote()))
            taken.Add("Ctrl+Alt+N");
        if (!_hotkeys.Register(Native.MOD_CONTROL | Native.MOD_ALT, Key.F, OpenSearch))
            taken.Add("Ctrl+Alt+F");

        _mgr.RestoreSession();

        if (_mgr.Notes.Count == 0) _mgr.CreateNote("amber", Welcome);
        // com todas as notas fechadas a app arrancava sem nada no ecra e obrigava a ir
        // buscar uma a bandeja para se perceber que tinha sequer aberto
        else if (NothingOnScreen()) _mgr.ShowMostRecent();

        // um atalho ja tomado por outra app falhava em silencio e parecia app avariada
        if (taken.Count > 0)
            _tray.Notify("NOTED", string.Join(" e ", taken) +
                " ja esta a ser usado por outra aplicacao. Usa o menu da bandeja.");
    }

    private bool NothingOnScreen()
    {
        if (_mgr is null) return false;
        foreach (var n in _mgr.Notes)
            if (!n.Collapsed) return false;
        return true;
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NOTED");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "crash.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\n{ex}\n\n");
        }
        catch { /* se nem isto der, nao ha nada a fazer */ }
    }

    private void OpenSearch()
    {
        if (_mgr is null) return;
        if (_search is { IsLoaded: true }) { _search.Activate(); return; }
        _search = new SearchWindow(_mgr);
        _search.Closed += (_, _) => _search = null;
        _search.Show();
        _search.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mgr?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _single?.Dispose();
        base.OnExit(e);
    }

    private const string Welcome = """
        # NOTED

        Pica estas checkboxes com o rato. Clica no texto para editar, **Esc** para voltar.

        - [ ] Ctrl+Alt+N cria uma nota em qualquer lado
        - [ ] Ctrl+Alt+F pesquisa em todas as notas
        - [ ] Ctrl+Enter transforma a linha em checkbox
        - [ ] Ctrl+T fixa a nota por cima das outras
        - [ ] Ctrl+E alterna leitura e escrita

        Selecciona texto em modo de escrita: aparece uma barra com **negrito**,
        *italico*, `codigo`, listas e links. O botao direito abre o mesmo menu.

        O menu `...` tem cor, opacidade, alerta e tags.

        As notas sao ficheiros .md em %APPDATA%\NOTED\notes
        """;
}
