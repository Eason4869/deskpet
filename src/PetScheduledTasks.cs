using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace Dafeiyu
{
    public sealed partial class PetWindow
    {
        ApiClient scheduledTaskApi;
        DispatcherTimer scheduledTaskTimer;
        Window scheduledMissedDialog;
        readonly Queue<Action> scheduledTaskNotifications = new Queue<Action>();
        TrayNotificationWindow scheduledNotificationBlocker;

        internal ScheduledTaskRuntime ScheduledTasks { get; private set; }

        void InitializeScheduledTasks()
        {
            scheduledTaskApi = new ApiClient();
            ScheduledTasks = new ScheduledTaskRuntime(Store.DirectoryPath, DataMaintenance, scheduledTaskApi, () => Settings);
            ScheduledTasks.Missed += ShowRecoveredScheduledTasks;
            ScheduledTasks.Completed += ScheduledTaskCompleted;
            scheduledTaskTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromSeconds(1) };
            scheduledTaskTimer.Tick += ScheduledTaskTick;
            scheduledTaskTimer.Start();
        }

        void ScheduledTaskTick(object sender, EventArgs args) { TickScheduledTasks(); }

        void TickScheduledTasks()
        {
            if (!closing) ScheduledTasks.Tick();
        }

        void ShowRecoveredScheduledTasks() { ShowScheduledMissed(chat); }

        internal void ShowScheduledMissed(Window owner)
        {
            if (closing || ScheduledTasks.Pending().Count == 0) return;
            if (scheduledMissedDialog != null)
            {
                if (scheduledMissedDialog.WindowState == WindowState.Minimized)
                    scheduledMissedDialog.WindowState = WindowState.Normal;
                scheduledMissedDialog.Activate();
                return;
            }
            var dialog = ScheduledTasksPage.ShowMissed(owner, ScheduledTasks);
            scheduledMissedDialog = dialog;
            dialog.Closed += delegate { scheduledMissedDialog = null; };
        }

        void ScheduledTaskCompleted(ScheduledTaskWork work, ScheduledRunState state, string result)
        {
            if (closing || Settings.QuietMode) return;
            var status = state == ScheduledRunState.Succeeded ? "scheduledTasks.succeeded"
                : state == ScheduledRunState.Interrupted ? "scheduledTasks.interrupted" : "scheduledTasks.failed";
            var reminder = work.Definition.Kind == ScheduledTaskKind.Reminder ? result : null;
            scheduledTaskNotifications.Enqueue(() =>
            {
                ShowTrayNotification(
                    () => TextResources.Format("scheduledTasks.notificationResult", work.Definition.Title),
                    () => reminder ?? TextResources.Get(status),
                    () => TextResources.Get("scheduledTasks.openResult"),
                    () => OpenScheduledTaskResult(work.Run.TaskId, work.Run.Id));
                PlayNotificationCue(state == ScheduledRunState.Succeeded ? NotificationCue.Complete : NotificationCue.Failure,
                    "scheduled-task:" + work.Run.Id);
            });
            ShowNextScheduledTaskNotification();
        }

        void ShowNextScheduledTaskNotification()
        {
            if (closing || Settings.QuietMode || scheduledTaskNotifications.Count == 0 || scheduledNotificationBlocker != null) return;
            if (trayNotification == null) scheduledTaskNotifications.Dequeue()();
            if (scheduledTaskNotifications.Count == 0 || trayNotification == null) return;
            scheduledNotificationBlocker = trayNotification;
            scheduledNotificationBlocker.Closed += ScheduledNotificationBlockerClosed;
        }

        void ScheduledNotificationBlockerClosed(object sender, EventArgs args)
        {
            var notice = (TrayNotificationWindow)sender;
            notice.Closed -= ScheduledNotificationBlockerClosed;
            if (!ReferenceEquals(scheduledNotificationBlocker, notice)) return;
            scheduledNotificationBlocker = null;
            // Let the caller finish updating the shared tray notification first.
            Dispatcher.BeginInvoke(new Action(ShowNextScheduledTaskNotification));
        }

        void DiscardScheduledTaskNotifications()
        {
            scheduledTaskNotifications.Clear();
            if (scheduledNotificationBlocker == null) return;
            scheduledNotificationBlocker.Closed -= ScheduledNotificationBlockerClosed;
            scheduledNotificationBlocker = null;
        }

        void OpenScheduledTaskResult(string taskId, string runId)
        {
            if (closing || IsUpdating) return;
            var window = EnsureAppWindow();
            window.ShowScheduledTasksPage(taskId, runId);
            ShowAppWindow(window);
        }

        async Task StopScheduledTasksAsync()
        {
            scheduledTaskTimer.Stop();
            scheduledTaskTimer.Tick -= ScheduledTaskTick;
            ScheduledTasks.Missed -= ShowRecoveredScheduledTasks;
            ScheduledTasks.Completed -= ScheduledTaskCompleted;
            DiscardScheduledTaskNotifications();
            if (scheduledMissedDialog != null) scheduledMissedDialog.Close();
            try { await ScheduledTasks.StopAsync(); }
            catch (Exception error) { RuntimeDiagnostics.Record(error); }
            finally
            {
                ScheduledTasks.Dispose();
                scheduledTaskApi.Dispose();
            }
        }
    }
}
