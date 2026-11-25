using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Dafeiyu
{
    internal sealed class ThemeTitleSwitch : Grid
    {
        const double ChoiceSize = 26;
        static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(190);
        static readonly ControlTemplate ChoiceTemplate = (ControlTemplate)ThemeManager.Parse(@"
<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'>
  <Border x:Name='face' CornerRadius='13' BorderThickness='1' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'>
    <Viewbox Width='16' Height='16' HorizontalAlignment='Center' VerticalAlignment='Center' IsHitTestVisible='False'>
      <Canvas Width='24' Height='24'>
        <Path Data='{Binding Content, RelativeSource={RelativeSource TemplatedParent}}' Stroke='{TemplateBinding Foreground}' StrokeThickness='1.65' StrokeLineJoin='Round' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>
      </Canvas>
    </Viewbox>
  </Border>
  <ControlTemplate.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='face' Property='BorderBrush' Value='#A6B6CD'/></Trigger>
    <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='face' Property='BorderBrush' Value='#456DE6'/></Trigger>
    <Trigger Property='IsPressed' Value='True'><Setter TargetName='face' Property='Opacity' Value='.7'/></Trigger>
    <Trigger Property='IsEnabled' Value='False'><Setter TargetName='face' Property='Opacity' Value='.5'/></Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>");

        readonly Button system;
        readonly Button light;
        readonly Button dark;
        readonly TranslateTransform thumbOffset = new TranslateTransform();
        string selectedMode;
        bool effectiveDark;

        internal event Action<string, Button> Selected;
        internal Button SystemButton { get { return system; } }
        internal Border Capsule { get; private set; }

        internal ThemeTitleSwitch()
        {
            Name = "AppThemeMode";
            VerticalAlignment = VerticalAlignment.Center;
            Margin = new Thickness(1, 0, 4, 0);
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            LocalizedUi.Automation(this, () => TextResources.Get("quietPanel.quietPanel.text"));

            system = CreateButton("system", "AppThemeSystem");
            system.Margin = new Thickness(0, 0, 4, 0);
            Children.Add(system);

            var choices = new Grid { Width = ChoiceSize * 2, Height = ChoiceSize };
            choices.ColumnDefinitions.Add(new ColumnDefinition());
            choices.ColumnDefinitions.Add(new ColumnDefinition());
            choices.Children.Add(new Border
            {
                Name = "AppThemeThumb", Width = ChoiceSize, Height = ChoiceSize,
                CornerRadius = new CornerRadius(ChoiceSize / 2),
                Background = ThemeManager.Brush("#FFFFFF", "#DCE5F4"),
                IsHitTestVisible = false, RenderTransform = thumbOffset
            });
            light = CreateButton("light", "AppThemeLight");
            choices.Children.Add(light);
            dark = CreateButton("dark", "AppThemeDark");
            SetColumn(dark, 1);
            choices.Children.Add(dark);

            Capsule = new Border
            {
                Child = choices, Padding = new Thickness(1), CornerRadius = new CornerRadius(15),
                Background = ThemeManager.Brush("#E1E8EE", "#202733"),
                BorderBrush = ThemeManager.CardBorder, BorderThickness = new Thickness(1)
            };
            SetColumn(Capsule, 1);
            Children.Add(Capsule);
            Refresh(ThemeManager.Mode, ThemeManager.IsDark, false);
        }

        Button CreateButton(string mode, string name)
        {
            var button = new Button
            {
                Name = name, Width = ChoiceSize, Height = ChoiceSize, Padding = new Thickness(0),
                Background = Brushes.Transparent, BorderBrush = Brushes.Transparent,
                Foreground = AgentUi.Muted, Content = Geometry.Parse(ThemeSwitch.Icon(mode)),
                Template = ChoiceTemplate, FocusVisualStyle = null, Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            BindCaption(button, mode);
            button.Click += delegate
            {
                var handler = Selected;
                if (handler != null) handler(mode, button);
            };
            return button;
        }

        void BindCaption(Button button, string mode)
        {
            Func<string> caption = () => mode == "system" && selectedMode == "system"
                ? ThemeSwitch.Label(mode) + " · " + ThemeSwitch.Label(effectiveDark ? "dark" : "light")
                : ThemeSwitch.Label(mode);
            LocalizedUi.Set(button, "ToolTip", caption);
            LocalizedUi.Automation(button, caption);
        }

        internal void Refresh(string mode, bool dark, bool animate)
        {
            double target = dark ? ChoiceSize : 0;
            if (!animate)
            {
                thumbOffset.BeginAnimation(TranslateTransform.XProperty, null);
                thumbOffset.X = target;
            }
            else if ((double)thumbOffset.GetAnimationBaseValue(TranslateTransform.XProperty) != target)
            {
                double start = thumbOffset.X;
                thumbOffset.X = target;
                thumbOffset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(start, target, SlideDuration)
                {
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut },
                    FillBehavior = FillBehavior.Stop
                });
            }

            mode = ThemeColors.Normalize(mode);
            if (mode == selectedMode && dark == effectiveDark) return;
            selectedMode = mode;
            effectiveDark = dark;
            bool followsSystem = selectedMode == "system";
            system.Background = followsSystem ? ThemeManager.SelectedSurface : Brushes.Transparent;
            system.BorderBrush = followsSystem ? ThemeManager.Brush("#A6B6CD", "#6385B1") : Brushes.Transparent;
            system.Foreground = followsSystem ? Ui.Ink : AgentUi.Muted;
            var selectedInk = ThemeManager.Brush("#203754", "#203754");
            light.Foreground = dark ? AgentUi.Muted : selectedInk;
            this.dark.Foreground = dark ? selectedInk : AgentUi.Muted;

            BindCaption(system, "system");
            SetSelectionStatus(system, followsSystem);
            SetSelectionStatus(light, selectedMode == "light");
            SetSelectionStatus(this.dark, selectedMode == "dark");
        }

        static void SetSelectionStatus(Button button, bool selected)
        {
            LiveTextBindings.Bind(button, "ThemeItemStatus", () => selected
                ? TextResources.Get("themeModePicker.value.automationAutomationPropertiesSetItemStatus")
                : TextResources.Get("themeModePicker.value.automationAutomationPropertiesSetItemStatus.alternativeA"),
                value => AutomationProperties.GetItemStatus((DependencyObject)value),
                (value, status) => AutomationProperties.SetItemStatus((DependencyObject)value, (string)status));
        }
    }
}
