using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Threading;
using Noted.Models;
using Noted.Storage;
using Noted.UI;

namespace Noted.Services;

// dono das notas e das janelas: cria, esconde, apaga e grava com debounce
public sealed class NoteManager : IDisposable
{
    private readonly NoteStore _store;
    private readonly List<Note> _notes;
    private readonly Dictionary<string, NoteWindow> _windows = new();
    private readonly HashSet<Note> _dirty = new();
    private readonly DispatcherTimer _saveTimer;
    private readonly ReminderService _reminders;
    private int _cascade;

    public IReadOnlyList<Note> Notes => _notes;
    public string NotesFolder => _store.Root;

    public bool IsOpen(Note note) => _windows.ContainsKey(note.Id);

    public NoteManager(NoteStore store)
    {
        _store = store;
        _notes = _store.LoadAll();

        _saveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(700) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); FlushDirty(); };

        _reminders = new ReminderService(() => _notes);
        _reminders.Fired += OnReminderFired;
        _reminders.Reschedule();
    }

    // abre as notas que estavam visiveis na ultima sessao
    public void RestoreSession()
    {
        foreach (var n in _notes.Where(n => !n.Collapsed))
            Show(n);
    }

    public NoteWindow Show(Note note)
    {
        if (_windows.TryGetValue(note.Id, out var existing))
        {
            existing.Show();
            existing.Activate();
            return existing;
        }

        var win = new NoteWindow(note, this);
        win.Closed += (_, _) => _windows.Remove(note.Id);
        _windows[note.Id] = win;
        note.Collapsed = false;
        MarkDirty(note);
        win.Show();
        return win;
    }

    public NoteWindow CreateNote(string color = "amber", string body = "")
    {
        var note = new Note { Color = color, Body = body };
        PlaceNew(note);
        _notes.Insert(0, note);
        _store.Save(note);
        return Show(note);
    }

    // sem isto, todas as notas novas nasciam exactamente no centro do ecra, umas por
    // cima das outras, e so a de cima e que se via
    private void PlaceNew(Note note)
    {
        var area = System.Windows.SystemParameters.WorkArea;
        // a primeira nasce ao centro e as seguintes abrem em leque a volta dela
        int n = _cascade++ % 8;
        int step = n % 2 == 0 ? n / 2 : -((n + 1) / 2);
        note.X = Math.Clamp(area.Left + (area.Width - note.W) / 2 + step * 26,
            area.Left, Math.Max(area.Left, area.Right - note.W));
        note.Y = Math.Clamp(area.Top + (area.Height - note.H) / 2 + step * 26,
            area.Top, Math.Max(area.Top, area.Bottom - note.H));
    }

    // fechar a janela nao apaga: a nota continua no disco, so deixa de estar no ecra
    public void HideNote(Note note)
    {
        note.Collapsed = true;
        MarkDirty(note);
        if (_windows.Remove(note.Id, out var win)) win.Close();
    }

    public void DeleteNote(Note note)
    {
        if (_windows.Remove(note.Id, out var win)) win.Close();
        _notes.Remove(note);
        _dirty.Remove(note);
        _store.Delete(note);
        _reminders.Reschedule();
    }

    public void MarkDirty(Note note)
    {
        _dirty.Add(note);
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void FlushDirty()
    {
        if (_dirty.Count == 0) return;

        var pending = new List<Note>(_dirty);
        _dirty.Clear();
        foreach (var n in pending)
        {
            // uma nota que falhe (disco cheio, ficheiro trancado) nao pode levar as
            // outras atras; fica marcada para a proxima tentativa
            try { _store.Save(n); }
            catch { _dirty.Add(n); }
        }
    }

    public void RescheduleReminders() => _reminders.Reschedule();

    public void RevealInExplorer(Note? note = null)
    {
        // gravar primeiro: abrir a pasta e ver a nota sem as ultimas alteracoes confunde
        FlushDirty();

        var target = note?.Path is { } p && System.IO.File.Exists(p)
            ? $"/select,\"{p}\""
            : $"\"{_store.Root}\"";
        try { Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true }); }
        catch { /* sem explorer disponivel nao ha nada a fazer */ }
    }

    // arrancar com todas as notas fechadas dava um ecra vazio e parecia app avariada
    public void ShowMostRecent()
    {
        if (_notes.Count > 0) Show(_notes[0]);
    }

    // reabre as janelas visiveis para apanharem definicoes que so se aplicam na criacao
    public void RefreshWindows()
    {
        FlushDirty();

        var visible = new List<Note>();
        foreach (var n in _notes)
            if (_windows.ContainsKey(n.Id)) visible.Add(n);

        foreach (var win in _windows.Values.ToList()) win.Close();
        _windows.Clear();

        foreach (var n in visible) Show(n);
    }

    // recarrega do disco notas editadas por fora (git pull, onedrive, editor de texto)
    public void ReloadFromDisk()
    {
        FlushDirty();
        var fresh = _store.LoadAll();
        foreach (var win in _windows.Values.ToList()) win.Close();
        _windows.Clear();
        _notes.Clear();
        _notes.AddRange(fresh);
        RestoreSession();
        _reminders.Reschedule();
    }

    private void OnReminderFired(Note note)
    {
        MarkDirty(note);
        var win = Show(note);
        win.UpdateRemindBar();
        win.FlashAttention();
    }

    public void Dispose()
    {
        FlushDirty();
        _saveTimer.Stop();
        _reminders.Dispose();
    }
}
