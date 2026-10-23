using Avalonia;
using Avalonia.Controls;
using Dafeiyu;

namespace DeskPet.Desktop;

internal sealed class GuideWindow : Window
{
    private readonly DesktopController owner;
    private readonly TextBlock title = Ui.Text("", 22), detail = Ui.Text("");
    private readonly Button open;
    private readonly Button back, next;
    private int step;
    private bool finished;
    private static readonly (string Title, string Detail, string Section)[] Steps =
    {
        (TextResources.Get("guideWindow.steps.steps"), TextResources.Get("guideWindow.steps.steps.alternativeA"), "partner"),
        (TextResources.Get("guideWindow.steps.steps.alternativeB"), TextResources.Get("guideWindow.steps.steps.alternativeC"), "partner"),
        (TextResources.Get("guideWindow.steps.steps.alternativeD"), TextResources.Get("guideWindow.steps.steps.alternativeE"), "behavior"),
        (TextResources.Get("guideWindow.steps.steps.alternativeF"), TextResources.Get("guideWindow.steps.steps.alternativeG"), "behavior"),
        (TextResources.Get("guideWindow.steps.steps.alternativeH"), TextResources.Get("guideWindow.steps.steps.alternativeI"), "text"),
        (TextResources.Get("guideWindow.steps.steps.alternativeJ"), TextResources.Get("guideWindow.steps.steps.alternativeK"), "text"),
        (TextResources.Get("guideWindow.steps.steps.alternativeL"), TextResources.Get("guideWindow.steps.steps.alternativeM"), "partner"),
        (TextResources.Get("guideWindow.steps.steps.alternativeN"), TextResources.Get("guideWindow.steps.steps.alternativeO"), "chat"),
        (TextResources.Get("guideWindow.steps.steps.alternativeP"), TextResources.Get("guideWindow.steps.steps.alternativeQ"), "fullchat"),
        (TextResources.Get("guideWindow.steps.steps.alternativeR"), TextResources.Get("guideWindow.steps.steps.alternativeS"), "asr"),
        (TextResources.Get("guideWindow.steps.steps.alternativeT"), TextResources.Get("guideWindow.steps.steps.alternativeU"), "speech"),
        (TextResources.Get("guideWindow.steps.steps.alternativeV"), TextResources.Get("guideWindow.steps.steps.alternativeW"), "live"),
        (TextResources.Get("guideWindow.steps.steps.alternativeX"), TextResources.Get("guideWindow.steps.steps.alternativeY"), "behavior"),
        (TextResources.Get("guideWindow.steps.steps.alternativeZ"), TextResources.Get("guideWindow.steps.steps.alternativeAA"), "storage"),
        (TextResources.Get("guideWindow.steps.steps.alternativeAB"), TextResources.Get("guideWindow.steps.steps.alternativeAC"), "chat"),
        (TextResources.Get("guideWindow.steps.steps.alternativeAD"), TextResources.Get("guideWindow.steps.steps.alternativeAE"), "work"),
        (TextResources.Get("guideWindow.steps.steps.alternativeAF"), TextResources.Get("guideWindow.steps.steps.alternativeAG"), "about")
    };
    public GuideWindow(DesktopController controller)
    {
        owner = controller; Title = TextResources.Get("guideWindow.guideWindow.title"); Width = 610; Height = 480; MinWidth = 420; MinHeight = 320; Topmost = true;
        step = owner.Settings.OnboardingState is "completed" or "skipped" ? 0 : Math.Clamp(owner.Settings.OnboardingStep, 0, Steps.Length - 1);
        owner.Settings.OnboardingState = "deferred";
        open = Ui.Button(TextResources.Get("guideWindow.guideWindow.text"), OpenStep);
        back = Ui.Button(TextResources.Get("guideWindow.guideWindow.text.alternativeA"), () => { if (step > 0) { step--; Persist(); Refresh(); } });
        next = Ui.Button(TextResources.Get("guideWindow.guideWindow.text.alternativeB"), () => { if (step == Steps.Length - 1) Finish("completed"); else { step++; Persist(); Refresh(); } });
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 14 };
        layout.Children.Add(title);
        var body = new ScrollViewer { Content = detail, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(body, 1); layout.Children.Add(body);
        var exit = Ui.Button(TextResources.Get("guideWindow.guideWindow.text.alternativeC"), () => Finish("skipped"));
        exit.Name = "GuideSkip"; exit.FontSize = 14; exit.FontWeight = Avalonia.Media.FontWeight.SemiBold; exit.Foreground = DesktopTheme.Brush("#203754");
        var footer = Ui.Column(open, Ui.Row(back, next), Ui.Row(Ui.Button(TextResources.Get("guideWindow.guideWindow.text.alternativeD"), () => Finish("deferred")), exit));
        Grid.SetRow(footer, 2); layout.Children.Add(footer);
        Content = new Border { Padding = new Thickness(24), Child = layout };
        Closing += (_, _) => { if (!finished) { owner.Settings.OnboardingState = "deferred"; Persist(); } };
        Persist(); Refresh();
    }
    private void Persist() { owner.Settings.OnboardingStep = step; owner.Save(); }
    private void Refresh() { var item = Steps[step]; title.Text = $"{step + 1} / {Steps.Length} · {item.Title}"; detail.Text = item.Detail; back.IsEnabled = step > 0; next.Content = step == Steps.Length - 1 ? TextResources.Get("guideWindow.refresh.stepStepsLength") : TextResources.Get("guideWindow.refresh.stepStepsLength.alternativeA"); }
    private void OpenStep()
    {
        var section = Steps[step].Section;
        if (section == "chat") owner.ShowChat(true); else if (section == "fullchat") owner.ShowChat(false); else if (section == "about") owner.ShowAbout(); else owner.ShowSettingsSection(section);
    }
    private void Finish(string state) { owner.Settings.OnboardingState = state; Persist(); finished = true; Close(); }
}
