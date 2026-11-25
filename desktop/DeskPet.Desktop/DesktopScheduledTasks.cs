using Avalonia.Controls;
using Avalonia.Threading;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed partial class DesktopController
{
    private const string ScheduledNoticeName = "ScheduledTaskNotification";
    private readonly DispatcherTimer scheduledTaskTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Queue<(ScheduledTaskWork Work, ScheduledRunState State, string? Result)> scheduledNotices = new();
    private DateTimeOffset lastScheduledTaskTick;
    private ulong? scheduledPowerGeneration;
    private Window? scheduledMissed;

    internal ScheduledTaskRuntime ScheduledTasks { get; }

    private void StartScheduledTasks()
    {
        if (scheduledTaskTimer.IsEnabled || exiting) return;
        ScheduledTasks.Completed += ScheduledTaskCompleted;
        ScheduledTasks.Missed += ScheduledTasksMissed;
        scheduledTaskTimer.Tick += TickScheduledTasks;
        scheduledTaskTimer.Start();
        TickScheduledTasks(this, EventArgs.Empty);
    }

    private void TickScheduledTasks(object? sender, EventArgs args)
    {
        if (exiting) return;
        var generation = runtime.SystemPowerGeneration;
        var now = DateTimeOffset.UtcNow;
        // With an older native bridge, hold potentially missed work for review after a timer gap.
        bool fallbackRecovery = generation == null && lastScheduledTaskTick != default
            && now - lastScheduledTaskTick > TimeSpan.FromSeconds(5);
        if (generation != scheduledPowerGeneration || fallbackRecovery)
        {
            if ((generation.GetValueOrDefault() & 1UL) != 0) ScheduledTasks.PowerSuspended();
            else ScheduledTasks.PowerResumed();
            scheduledPowerGeneration = generation;
        }
        lastScheduledTaskTick = now;
        ScheduledTasks.Tick();
    }

    private void ScheduledTasksMissed() => Dispatcher.UIThread.Post(ShowScheduledMissed);

    internal void ShowScheduledMissed()
    {
        if (exiting || ScheduledTasks.Pending().Count == 0) return;
        if (scheduledMissed is { } existing)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        var anchor = fullChat is { IsVisible: true, WindowState: not WindowState.Minimized } ? (Window)fullChat : Pet;
        var window = ScheduledTasksPage.ShowMissed(anchor, ScheduledTasks);
        scheduledMissed = window;
        window.Closed += (_, _) => scheduledMissed = null;
    }

    private void ScheduledTaskCompleted(ScheduledTaskWork work, ScheduledRunState state, string? result)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (exiting || Settings.QuietMode) return;
            scheduledNotices.Enqueue((work, state, result));
            ShowNextScheduledNotice();
        });
    }

    private void ShowNextScheduledNotice()
    {
        if (exiting || Settings.QuietMode)
        {
            scheduledNotices.Clear();
            return;
        }
        if (scheduledNotices.Count == 0) return;
        if (trayNotification is { } active)
        {
            active.Closed -= ScheduledNoticeClosed;
            active.Closed += ScheduledNoticeClosed;
            return;
        }
        var (work, state, result) = scheduledNotices.Dequeue();
        bool reminder = work.Definition.Kind == ScheduledTaskKind.Reminder;
        string status = state switch
        {
            ScheduledRunState.Succeeded => "scheduledTasks.succeeded",
            ScheduledRunState.Interrupted => "scheduledTasks.interrupted",
            _ => "scheduledTasks.failed"
        };
        var notice = ShowTrayNotification(
            () => TextResources.Format("scheduledTasks.notificationResult", work.Definition.Title),
            () => reminder ? result ?? work.Definition.Content : TextResources.Get(status),
            () => TextResources.Get("scheduledTasks.openResult"),
            () => OpenScheduledTaskResult(work.Run.TaskId, work.Run.Id),
            ScheduledNoticeName);
        if (notice != null) notice.Closed += ScheduledNoticeClosed;
        PlayNotificationCue(state == ScheduledRunState.Succeeded ? NotificationCue.Complete : NotificationCue.Failure,
            "scheduled-task:" + work.Run.Id);
    }

    private void ScheduledNoticeClosed(object? sender, EventArgs args) => Dispatcher.UIThread.Post(ShowNextScheduledNotice);

    private void OpenScheduledTaskResult(string taskId, string runId)
    {
        if (exiting) return;
        ShowChat(false);
        if (fullChat!.WindowState == WindowState.Minimized)
        {
            fullChat.WindowState = WindowState.Normal;
            fullChat.Activate();
        }
        fullChat.ShowScheduledTask(taskId, runId);
    }

    private async Task StopScheduledTasksAsync()
    {
        scheduledTaskTimer.Stop();
        scheduledTaskTimer.Tick -= TickScheduledTasks;
        ScheduledTasks.Completed -= ScheduledTaskCompleted;
        ScheduledTasks.Missed -= ScheduledTasksMissed;
        scheduledMissed?.Close();
        scheduledNotices.Clear();
        if (trayNotification is { } notice)
        {
            notice.Closed -= ScheduledNoticeClosed;
            if (notice.Name == ScheduledNoticeName) notice.Close();
        }
        try { await ScheduledTasks.StopAsync(); }
        catch (Exception error) { System.Diagnostics.Trace.TraceError(error.GetType().Name); }
    }
}
