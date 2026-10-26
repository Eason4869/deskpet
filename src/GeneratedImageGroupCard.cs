using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dafeiyu
{
    internal sealed class GeneratedImageGroupCard : Grid
    {
        sealed class Slot
        {
            internal readonly Button Button;
            internal string AssetId;
            internal bool Initialized;
            internal Slot(Button button) { Button = button; }
        }
        readonly PetWindow pet;
        readonly Window host;
        readonly GeneratedImageGroupState state = new GeneratedImageGroupState();
        readonly ContentControl main = new ContentControl();
        readonly StackPanel rail = new StackPanel();
        readonly ScrollViewer scroll;
        readonly Dictionary<string, Slot> slots = new Dictionary<string, Slot>();
        ChatMessage message;
        bool attached;
        string displayedId, displayedAssetId;
        bool displayedHasTask;
        internal string SelectedId { get { return state.SelectedId; } }

        internal GeneratedImageGroupCard(PetWindow owner, Window window)
        {
            pet = owner; host = window; Name = "GeneratedImageGroupCard";
            HorizontalAlignment = HorizontalAlignment.Stretch;
            ColumnDefinitions.Add(new ColumnDefinition());
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition());
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            scroll = new ScrollViewer { Content = rail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, MaxHeight = 360 };
            Children.Add(main); Children.Add(scroll);
            SizeChanged += delegate { Layout(); };
            Loaded += delegate { Attach(); };
            Unloaded += delegate { Detach(); };
        }

        void Attach()
        {
            if (attached) return;
            attached = true;
            pet.ImageTasks.Changed += Changed;
            Refresh();
        }

        void Detach()
        {
            if (attached) pet.ImageTasks.Changed -= Changed;
            attached = false;
            main.Content = null;
            rail.Children.Clear();
            foreach (var slot in slots.Values) slot.Button.Content = null;
            slots.Clear();
            displayedId = null; displayedAssetId = null; displayedHasTask = false;
        }

        void Changed(ImageGenerationTaskSnapshot task)
        {
            if (message != null && (message.ImageGenerationTaskIds ?? new List<string>()).Contains(task.Id))
                Dispatcher.BeginInvoke(new Action(delegate { if (attached) Refresh(); }));
        }

        internal void Update(ChatMessage value) { message = value; Refresh(); }

        void Refresh()
        {
            if (message == null) return;
            state.Update(message.ImageGenerationTaskIds, message.GeneratedImages ?? new List<GeneratedImage>(), id =>
            {
                var saved = pet.ImageStore.FindByJobId(id);
                var task = pet.ImageTasks.Get(id);
                return saved ?? (task == null || String.IsNullOrEmpty(task.ResultImageId) ? null : pet.ImageStore.Get(task.ResultImageId));
            });
            foreach (var id in slots.Keys.Where(id => !state.Keys.Contains(id)).ToArray())
            {
                var slot = slots[id];
                rail.Children.Remove(slot.Button);
                slot.Button.Content = null;
                slots.Remove(id);
            }
            for (int index = 0; index < state.Keys.Count; index++)
            {
                string id = state.Keys[index];
                Slot slot;
                if (!slots.TryGetValue(id, out slot))
                {
                    var button = new Button { Name = "GeneratedImageGroupSlot", Tag = id, Width = 64, Height = 64,
                        Margin = new Thickness(4), Padding = new Thickness(2), Background = ThemeManager.Surface };
                    button.Click += delegate { state.Select(id); ShowSelected(); UpdateSelection(); };
                    slot = new Slot(button); slots.Add(id, slot);
                }
                if (index >= rail.Children.Count || !Object.ReferenceEquals(rail.Children[index], slot.Button))
                {
                    rail.Children.Remove(slot.Button);
                    rail.Children.Insert(index, slot.Button);
                }
                UpdateSlot(slot, state.ImageFor(id), index);
            }
            UpdateSelection(); ShowSelected();
            Visibility = state.Keys.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            Layout();
        }

        void UpdateSlot(Slot slot, GeneratedImage asset, int index)
        {
            string assetId = asset == null ? null : asset.Id;
            if (!slot.Initialized || slot.AssetId != assetId)
            {
                slot.Initialized = true; slot.AssetId = assetId;
                slot.Button.Content = Ui.Text((index + 1).ToString(), 12, Ui.Muted);
                if (asset != null)
                {
                    try
                    {
                        int width, height; bool limited;
                        slot.Button.Content = new Image { Source = ChatImagePreview.DecodeGeneratedImage(pet.ImageStore.Read(asset.Id), 80, out width, out height, out limited), Stretch = Stretch.UniformToFill };
                    }
                    catch (Exception error)
                    {
                        if (!(error is System.IO.IOException || error is UnauthorizedAccessException || error is ArgumentException || error is NotSupportedException || error is FormatException)) throw;
                    }
                }
            }
            var text = slot.Button.Content as TextBlock;
            if (text != null) text.Text = (index + 1).ToString();
            slot.Button.ToolTip = asset != null ? TextResources.Format("generatedImageGroupCard.updateSlot.toolTipAssetNull",(index + 1)) : ImageGenerationMotion.Label(pet.ImageTasks.Get((string)slot.Button.Tag));
        }

        void UpdateSelection()
        {
            foreach (var pair in slots)
            {
                bool active = pair.Key == state.SelectedId;
                pair.Value.Button.BorderThickness = new Thickness(active ? 2 : 1);
                pair.Value.Button.BorderBrush = active ? Ui.Blue : Ui.Line;
            }
        }

        void ShowSelected()
        {
            var asset = state.SelectedImage;
            string assetId = asset == null ? null : asset.Id;
            bool hasTask = asset == null && state.SelectedId != null && pet.ImageTasks.Get(state.SelectedId) != null;
            // Pending controls own snapshot updates; unrelated task states leave this visual intact.
            if (displayedId == state.SelectedId && displayedAssetId == assetId && displayedHasTask == hasTask) return;
            displayedId = state.SelectedId; displayedAssetId = assetId; displayedHasTask = hasTask;
            if (asset != null)
                main.Content = new GeneratedImageCard(pet, host, asset, selected =>
                {
                    var chat = host as ChatWindow;
                    if (chat != null) chat.ShowImagesPage(selected); else pet.OpenImageWorkspace(selected);
                });
            else if (state.SelectedId != null)
                main.Content = hasTask ? (FrameworkElement)ImageGenerationPendingCard.Create(pet, host, state.SelectedId) : LocalizedUi.Text(()=>(TextResources.Get("generatedImageGroupCard.showSelected.text")), 12, Ui.Muted);
            else main.Content = null;
        }

        void Layout()
        {
            bool wide = ActualWidth >= 480;
            Grid.SetColumn(scroll, wide ? 1 : 0); Grid.SetRow(scroll, wide ? 0 : 1);
            rail.Orientation = wide ? Orientation.Vertical : Orientation.Horizontal;
            scroll.Visibility = state.Keys.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            scroll.HorizontalScrollBarVisibility = wide ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            scroll.VerticalScrollBarVisibility = wide ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        }
    }
}
