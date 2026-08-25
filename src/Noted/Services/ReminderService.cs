using System;
using System.Collections.Generic;
using System.Windows.Threading;
using Noted.Models;

namespace Noted.Services;

// um unico timer agendado para o proximo alerta -- nada de polling, 0% cpu em idle
public sealed class ReminderService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Func<IEnumerable<Note>> _source;

    public event Action<Note>? Fired;

    public ReminderService(Func<IEnumerable<Note>> source)
    {
        _source = source;
        _timer = new DispatcherTimer(DispatcherPriority.Background);
        _timer.Tick += OnTick;
    }

    // rechamar sempre que uma nota mude de hora de alerta
    public void Reschedule()
    {
        _timer.Stop();

        DateTime? next = null;
        foreach (var n in _source())
        {
            if (n.Remind is not DateTime r) continue;
            if (next is null || r < next) next = r;
        }
        if (next is null) return;

        var delay = next.Value - DateTime.Now;
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        // DispatcherTimer satura acima de ~24 dias; parte em fatias
        if (delay > TimeSpan.FromHours(12)) delay = TimeSpan.FromHours(12);

        _timer.Interval = delay;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _timer.Stop();
        var now = DateTime.Now;

        var due = new List<Note>();
        foreach (var n in _source())
            if (n.Remind is DateTime r && r <= now) due.Add(n);

        foreach (var n in due)
        {
            n.Remind = null; // alerta unico; recorrencia fica para v2
            Fired?.Invoke(n);
        }

        Reschedule();
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
    }
}
