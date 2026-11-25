#if NET10_0
#pragma warning disable 8600, 8601, 8602, 8603, 8604, 8618, 8622, 8625
#endif
using System;
using System.Globalization;
using System.Linq;
using Dafeiyu;
#if NET10_0
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Element = Avalonia.Controls.Control;
namespace DeskPet.Desktop
#else
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Element = System.Windows.FrameworkElement;
namespace Dafeiyu
#endif
{
    internal sealed partial class ScheduledTasksPage
    {
        void Edit(ScheduledTaskDefinition original)
        {
            if (editor != null) { editor.Activate(); return; }
            var definition = original == null ? new ScheduledTaskDefinition() : original.Copy();
            var dialog = MakeWindow(() => TextResources.Get("scheduledTasks.editor"), "ScheduledTaskEditor", 650, 630);
            var form = new StackPanel();
            var name = Input(definition.Title, () => TextResources.Get("scheduledTasks.name"), "ScheduledTaskName");
            name.MaxLength = 160;
            var prompt = Input(definition.Content, () => TextResources.Get("scheduledTasks.content"), "ScheduledTaskContent", true);
            prompt.MinHeight = 97;
            prompt.MaxHeight = 180;
            prompt.MaxLength = 16000;
            var kind = Choices("ScheduledTaskKind", () => new[] { TextResources.Get("scheduledTasks.reminder"), TextResources.Get("scheduledTasks.ai") }, (int)definition.Kind);
            var identity = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
            identity.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            identity.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            identity.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var nameLabel = Caption(() => TextResources.Get("scheduledTasks.name"), 12, true);
            var kindLabel = Caption(() => TextResources.Get("scheduledTasks.type"), 12, true);
            identity.Children.Add(nameLabel);
            Grid.SetColumn(kindLabel, 1);
            identity.Children.Add(kindLabel);
            name.Margin = new Thickness(0, 0, 12, 0);
            name.VerticalAlignment = kind.VerticalAlignment = VerticalAlignment.Stretch;
            Grid.SetRow(name, 1);
            identity.Children.Add(name);
            Grid.SetRow(kind, 1);
            Grid.SetColumn(kind, 1);
            identity.Children.Add(kind);
            LocalizedUi.Automation(kind, () => TextResources.Get("scheduledTasks.type"));
            form.Children.Add(identity);
            form.Children.Add(Field(() => TextResources.Get("scheduledTasks.content"), prompt));
            var repeat = Choices("ScheduledTaskFrequency", () => new[] {
                TextResources.Get("scheduledTasks.once"), TextResources.Get("scheduledTasks.daily"),
                TextResources.Get("scheduledTasks.weekly"), TextResources.Get("scheduledTasks.monthly") }, (int)definition.Rule.Frequency);
            var date = new DatePicker { Name = "ScheduledTaskDate", Margin = new Thickness(0, 0, 0, 5) };
            SetDate(date, definition.Rule.OnceDateTicks == 0 ? DateTime.Today : new DateTime(definition.Rule.OnceDateTicks));
            var dateRow = InlineField(() => TextResources.Get("scheduledTasks.date"), date);
            var hour = NumberChoices("ScheduledTaskHour", 24, 0, definition.Rule.MinuteOfDay / 60, "00");
            var minute = NumberChoices("ScheduledTaskMinute", 60, 0, definition.Rule.MinuteOfDay % 60, "00");
            LocalizedUi.Automation(hour, () => TextResources.Get("scheduledTasks.time"));
            LocalizedUi.Automation(minute, () => TextResources.Get("scheduledTasks.time"));
            var day = NumberChoices("ScheduledTaskMonthDay", 31, 1, Math.Max(0, definition.Rule.DayOfMonth - 1), "0");
            var monthRow = InlineField(() => TextResources.Get("scheduledTasks.date"), day);
            var days = new WrapPanel();
            var weekday = new CheckBox[7];
            for (int i = 0; i < 7; i++)
            {
                weekday[i] = new CheckBox
                {
                    Content = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName((DayOfWeek)i),
                    IsChecked = (definition.Rule.Weekdays & 1 << i) != 0,
                    Margin = new Thickness(0, 0, 10, 3)
                };
                days.Children.Add(weekday[i]);
            }
            var local = Caption(() => TextResources.Format("scheduledTasks.localZone", TimeZoneInfo.Local.DisplayName), 12);
            var next = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 9) };
            form.Children.Add(Card(Column(InlineField(() => TextResources.Get("scheduledTasks.frequency"), repeat), dateRow, days, monthRow,
                InlineField(() => TextResources.Get("scheduledTasks.time"), Actions(hour, minute)), local, next)));

            var modelChoices = runtime.Models().ToList();
            int modelIndex = modelChoices.FindIndex(choice => choice.ProviderId == definition.ProviderId && choice.Model == definition.Model);
            if (modelIndex < 0 && !String.IsNullOrEmpty(definition.Model))
            {
                modelIndex = modelChoices.Count;
                modelChoices.Add(new ScheduledModelChoice { ProviderId = definition.ProviderId, Model = definition.Model,
                    Label = TextResources.Format("scheduledTasks.modelUnavailable", definition.Model) });
            }
            var model = new ComboBox { Name = "ScheduledTaskModel", ItemsSource = modelChoices, SelectedIndex = Math.Max(0, modelIndex), MinWidth = 170 };
            var notify = new ComboBox { Name = "ScheduledTaskNotifications", MinWidth = 150 };
            var web = new CheckBox { Name = "ScheduledTaskWebSearch", IsChecked = definition.WebSearch };
            LocalizedUi.Set(web, "Content", () => TextResources.Get("scheduledTasks.webSearch"));
            var modelFields = Column(Field(() => TextResources.Get("scheduledTasks.model"), model), web);
            var advancedBody = Card(Column(Caption(() => TextResources.Get("scheduledTasks.runLocal")),
                Field(() => TextResources.Get("scheduledTasks.notify"), notify), modelFields));
            var advanced = new Expander { Name = "ScheduledTaskAdvanced", Content = advancedBody, IsExpanded = false };
#if NET10_0
            SettingsVisual.Disclosure(advanced);
#else
            SettingsSurface.StyleExpander(advanced);
#endif
            LocalizedUi.Set(advanced, "Header", () => TextResources.Get("scheduledTasks.advanced"));
            form.Children.Add(advanced);
            var cost = Caption(() => TextResources.Get("scheduledTasks.cost"), 12);
            form.Children.Add(cost);

            var failure = new TextBlock { TextWrapping = TextWrapping.Wrap };
            var cancel = ButtonFor(() => TextResources.Get("common.cancel"), "CancelScheduledTask", dialog.Close);
            Func<ScheduledTaskRule> readRule = null;
            var save = ButtonFor(definition.Id == null ? (Func<string>)(() => TextResources.Get("scheduledTasks.create")) : () => TextResources.Get("common.save"),
                "SaveScheduledTask", () => TryAction(() =>
                {
                    definition.Title = name.Text;
                    definition.Content = prompt.Text;
                    definition.Kind = (ScheduledTaskKind)kind.SelectedIndex;
                    definition.Rule = readRule();
                    definition.Notification = kind.SelectedIndex == 0 && notify.SelectedIndex == 1
                        ? ScheduledNotification.None : (ScheduledNotification)notify.SelectedIndex;
                    var choice = model.SelectedItem as ScheduledModelChoice;
                    definition.ProviderId = definition.Kind == ScheduledTaskKind.Ai && choice != null ? choice.ProviderId : null;
                    definition.Model = definition.Kind == ScheduledTaskKind.Ai && choice != null ? choice.Model : null;
                    definition.WebSearch = definition.Kind == ScheduledTaskKind.Ai && web.IsChecked == true;
                    string id = runtime.Save(definition);
                    if (original == null || original.Id == null)
                    {
                        turns.Clear();
                        draftInput = "";
                        prepared = null;
                    }
                    ShowTask(id);
                    dialog.Close();
                }, failure), true);

            readRule = () => new ScheduledTaskRule
            {
                Frequency = (ScheduledFrequency)repeat.SelectedIndex, MinuteOfDay = hour.SelectedIndex * 60 + minute.SelectedIndex,
                OnceDateTicks = GetDate(date).GetValueOrDefault().Date.Ticks,
                Weekdays = Enumerable.Range(0, 7).Where(i => weekday[i].IsChecked == true).Sum(i => 1 << i),
                DayOfMonth = day.SelectedIndex + 1
            };
            Action refresh = () =>
            {
                var rule = readRule();
                SetVisible(dateRow, rule.Frequency == ScheduledFrequency.Once);
                SetVisible(days, rule.Frequency == ScheduledFrequency.Weekly);
                SetVisible(monthRow, rule.Frequency == ScheduledFrequency.Monthly);
                var preview = rule.Next(DateTimeOffset.UtcNow, TimeZoneInfo.Local);
                next.Text = NextText(preview);
                save.IsEnabled = !String.IsNullOrWhiteSpace(name.Text) && !String.IsNullOrWhiteSpace(prompt.Text)
                    && rule.IsValid && (preview.HasValue || definition.Id != null);
            };
            Action<bool> refreshKind = first =>
            {
                bool isAi = kind.SelectedIndex == (int)ScheduledTaskKind.Ai;
                SetVisible(modelFields, isAi);
                SetVisible(cost, isAi);
                LocalizedUi.SetOptions(notify, () => isAi
                    ? new[] { TextResources.Get("scheduledTasks.notifyAll"), TextResources.Get("scheduledTasks.notifyFailures"), TextResources.Get("scheduledTasks.notifyNone") }
                    : new[] { TextResources.Get("scheduledTasks.notifyReminder"), TextResources.Get("scheduledTasks.notifyNone") });
                notify.SelectedIndex = !first ? 0 : isAi ? (int)definition.Notification : definition.Notification == ScheduledNotification.None ? 1 : 0;
            };
            kind.SelectionChanged += delegate { refreshKind(false); };
            repeat.SelectionChanged += delegate { refresh(); };
            hour.SelectionChanged += delegate { refresh(); };
            minute.SelectionChanged += delegate { refresh(); };
            day.SelectionChanged += delegate { refresh(); };
            date.SelectedDateChanged += delegate { refresh(); };
            name.TextChanged += delegate { refresh(); };
            prompt.TextChanged += delegate { refresh(); };
            foreach (var check in weekday)
            {
#if NET10_0
                check.IsCheckedChanged += delegate { refresh(); };
#else
                check.Checked += delegate { refresh(); };
                check.Unchecked += delegate { refresh(); };
#endif
            }
            refreshKind(true);
            refresh();
            var body = new Grid();
            Row(body, Scroll(form), new GridLength(1, GridUnitType.Star));
            Row(body, failure, GridLength.Auto);
            Row(body, Footer(cancel, save), GridLength.Auto);
            DialogUi.ApplyTitled(dialog, body);
            editor = dialog;
            dialog.Closed += delegate { editor = null; };
            ShowWindow(dialog, FindWindow(this), true);
        }

        static ComboBox Choices(string name, Func<string[]> labels, int selected)
        {
            var box = new ComboBox { Name = name, MinWidth = 116, MinHeight = 36 };
            LocalizedUi.SetOptions(box, labels);
            box.SelectedIndex = selected;
            return box;
        }

        static ComboBox NumberChoices(string name, int count, int start, int selected, string format)
        {
            return new ComboBox { Name = name, ItemsSource = Enumerable.Range(start, count).Select(n => n.ToString(format, CultureInfo.InvariantCulture)).ToArray(),
                SelectedIndex = selected, MinWidth = 66, MinHeight = 36, MaxDropDownHeight = 280 };
        }

        static void SetDate(DatePicker picker, DateTime date)
        {
#if NET10_0
            picker.SelectedDate = new DateTimeOffset(date);
#else
            picker.SelectedDate = date;
#endif
        }

        static DateTime? GetDate(DatePicker picker)
        {
#if NET10_0
            return picker.SelectedDate.HasValue ? picker.SelectedDate.Value.Date : (DateTime?)null;
#else
            return picker.SelectedDate;
#endif
        }

        static Element Field(Func<string> label, Element input)
        {
            LocalizedUi.Automation(input, label);
            return Column(Caption(label, 12, true), input);
        }

        static Element InlineField(Func<string> label, Element input)
        {
            LocalizedUi.Automation(input, label);
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 7) };
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var caption = Caption(label, 13, true);
            caption.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(caption);
            Grid.SetColumn(input, 1);
            grid.Children.Add(input);
            return grid;
        }

        static void Row(Grid grid, Element element, GridLength height)
        {
            Grid.SetRow(element, grid.RowDefinitions.Count);
            grid.RowDefinitions.Add(new RowDefinition { Height = height });
            grid.Children.Add(element);
        }

        static StackPanel Column(params Element[] children)
        {
            var stack = new StackPanel();
            foreach (var child in children) stack.Children.Add(child);
            return stack;
        }

        static WrapPanel Actions(params Element[] children)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 5) };
            foreach (var child in children)
            {
                child.Margin = new Thickness(0, 0, 8, 5);
                row.Children.Add(child);
            }
            return row;
        }

        static Element Footer(params Element[] children)
        {
            var row = Actions(children);
            row.Margin = new Thickness(0, 10, 0, 0);
            row.HorizontalAlignment = HorizontalAlignment.Right;
            return row;
        }

        static Border Card(Element content)
        {
#if NET10_0
            var background = DesktopTheme.CardSurface;
            var stroke = DesktopTheme.CardBorder;
#else
            var background = ThemeManager.CardSurface;
            var stroke = ThemeManager.CardBorder;
#endif
            return new Border { Child = content, Margin = new Thickness(0, 3, 0, 9), Padding = new Thickness(12),
                CornerRadius = new CornerRadius(12), Background = background, BorderBrush = stroke, BorderThickness = new Thickness(1) };
        }

#if NET10_0
        static IBrush SubtleSurface() { return DesktopTheme.Brush("#EDF3F8"); }
#else
        static Brush SubtleSurface() { return ThemeManager.Brush("#F4F7FA", "#202733"); }
#endif

        static TextBlock Caption(Func<string> label, double size = 14, bool strong = false)
        {
#if NET10_0
            var result = SettingsVisual.Text(label, size);
#else
            var result = Ui.Text(label, size, Ui.Ink);
#endif
            result.TextWrapping = TextWrapping.Wrap;
            result.Margin = new Thickness(0, 0, 0, 7);
#if NET10_0
            if (strong) result.FontWeight = FontWeight.SemiBold;
#else
            if (strong) result.FontWeight = FontWeights.SemiBold;
#endif
            return result;
        }

        static Button ButtonFor(Func<string> label, string name, Action action, bool primary = false)
        {
#if NET10_0
            var button = SettingsVisual.Button(label, action);
            if (primary)
            {
                button.Background = DesktopTheme.Brush("#EDF3FF");
                button.BorderBrush = SettingsVisual.Accent;
                var caption = button.Content as TextBlock;
                if (caption != null) caption.FontWeight = FontWeight.SemiBold;
            }
#else
            var button = Ui.Button(label, primary);
            button.Click += delegate { action(); };
#endif
            if (name != null) button.Name = name;
            LocalizedUi.Automation(button, label);
            return button;
        }

        static TextBox Input(string value, Func<string> label, string name, bool multiline = false)
        {
#if NET10_0
            var input = Ui.Input(value ?? "", label());
            LocalizedUi.Set(input, "PlaceholderText", label);
            input.MinWidth = 0;
            if (multiline) { input.AcceptsReturn = true; input.TextWrapping = TextWrapping.Wrap; }
#else
            var input = Ui.Field(value ?? "", multiline);
            LocalizedUi.Tooltip(input, label);
#endif
            input.Name = name;
            LocalizedUi.Automation(input, label);
            input.Margin = new Thickness(0, 0, 0, 8);
            return input;
        }

        static Element ResultView(string result)
        {
#if NET10_0
            var view = new MarkdownView();
            view.Update(result);
            return view;
#else
            return new MarkdownView { Text = result };
#endif
        }

        async void CopyToClipboard(string text)
        {
#if NET10_0
            var top = TopLevel.GetTopLevel(this);
            if (top == null || top.Clipboard == null) return;
            try { await top.Clipboard.SetTextAsync(text); }
            catch (InvalidOperationException) { }
#else
            try { Clipboard.SetText(text); }
            catch (System.Runtime.InteropServices.COMException) { }
            await System.Threading.Tasks.Task.FromResult(0);
#endif
        }

        static ScrollViewer Scroll(Element element)
        {
            return new ScrollViewer { Content = element, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        static void SetVisible(Element element, bool visible)
        {
#if NET10_0
            element.IsVisible = visible;
#else
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
#endif
        }

        static Window FindWindow(Element element)
        {
#if NET10_0
            return TopLevel.GetTopLevel(element) as Window;
#else
            return Window.GetWindow(element);
#endif
        }

        static Window MakeWindow(Func<string> title, string name, double width, double height)
        {
            var window = new Window { Name = name };
#if NET10_0
            window.Width = width;
            window.Height = height;
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            LocalizedUi.Set(window, "Title", title);
#else
            LocalizedUi.Window(window, title, width, height);
#endif
            return window;
        }

        static void ShowWindow(Window window, Window owner, bool modal)
        {
            bool owned = owner != null && owner.IsVisible && owner.WindowState != WindowState.Minimized;
#if NET10_0
            if (owned)
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                if (modal) { _ = window.ShowDialog(owner); }
                else window.Show(owner);
            }
            else window.Show();
#else
            window.Owner = owned ? owner : null;
            window.WindowStartupLocation = owned ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            if (modal) window.ShowDialog();
            else window.Show();
#endif
        }
    }
}
