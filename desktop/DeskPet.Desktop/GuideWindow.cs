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
        ("欢迎使用 DeskPet", "接下来会打开实际的 macOS 设置控件。每步保存后可继续；关闭引导可在下次从菜单栏恢复。", "partner"),
        ("选择桌面伙伴", "在伙伴标签选择四个内置伙伴，或导入自己的 pet.json 外观。切换时会停止旧伙伴正在进行的对话。", "partner"),
        ("大小、置顶与位置", "在通用标签调整大小与置顶。直接拖动伙伴可移动位置；锁定位置后不再移动。", "behavior"),
        ("散步与鼠标跟随", "偶尔散步每隔 90～180 秒尝试一次；互动后重新计时。鼠标跟随、小憩与落地踉跄可以分别开关。", "behavior"),
        ("模型配置", "模型配置标签可以新增厂商。每个厂商独立保存地址、密钥和代理；选择预设后仍可修改。密钥在 Mac 使用 Keychain 支撑的加密。", "text"),
        ("模型列表与显示名称", "点击获取模型列表后多选添加，也可手填模型 ID 与显示名称。点击保存并启用此厂商 / 模型后生效。", "text"),
        ("人格", "伙伴标签中的人格对当前伙伴单独保存；修改后点击保存人格。可以随时恢复原始设定。", "partner"),
        ("快速对话", "双击伙伴或点击菜单栏的快速对话。输入文字后点击发送 / 排队；⌘ + Enter 也可发送。", "chat"),
        ("完整聊天与队列", "完整聊天右侧支持队列编辑、删除、上下移动和立即引导。停止会取消当前请求并暂停队列。快聊与完整聊天共用同一会话。", "fullchat"),
        ("云端听写", "听写标签独立填写识别服务。点击聊天窗口的听写 / 完成录音才申请麦克风权限；再次点击会识别并填入草稿，确认后发送。", "asr"),
        ("系统与云端朗读", "云朗读标签配置独立服务；语音标签选择系统或云端朗读，并保存当前伙伴的音色。试听会实际调用所选服务。", "speech"),
        ("实时语音 Live", "Live 标签配置 Gemini 原生实时服务，语音标签保存音色。在聊天窗口点击 Live 后开始，关闭窗口会释放音频设备。", "live"),
        ("免打扰与快捷键", "通用标签可以关闭朗读、设置穿透和全局快捷键。开启穿透后从菜单栏重新进入设置。快捷键冲突会显示错误。", "behavior"),
        ("数据与历史", "数据保存在 Application Support / DeskPet；应用升级保留此目录。完整聊天可导出记录。Windows 密钥需在 Mac 重新录入。", "storage"),
        ("对话模式与附件", "快聊与完整聊天共用会话。粘贴、拖放或 + 可添加图片与文件；发送前可移除。文件夹附件只引用路径，不读取目录，也不改变权限。图片识别需要模型支持。\n\n回复支持 Markdown；思考/处理时间持续更新，正文出现后过程收起，可从回复更多菜单查看。\n\nWindows 版可在输入框左下角或用 Tab 切换对话/工作模式，+ 可附加最近应用窗口的快照。macOS 目前提供纯聊天，暂不支持文件 Agent 与应用快照。", "chat"),
        ("工作设置", "工作设置集中管理文件访问范围、默认目录、计划模式、持续目标、备份、轮数与时限。更改用于下一次请求，保存不会执行任务。\n\n文件 Agent 目前仅 Windows 版支持，可读取、搜索、创建与修改文件，创建文件夹并打开目录。默认完整访问沿用当前账户已有文件权限；仅工作目录可限制范围，计划模式只读。停止后保留已完成的修改，多轮请求可能消耗模型额度。\n\nmacOS 的工作设置可查看，相关控件暂不可用。", "work"),
        ("更新与开始使用", "登录启动仅在通用标签主动开启。关于窗口可以检查版本并打开下载页；macOS 需手动下载并替换应用。\n\n右键伙伴可打开菜单，在四格轮盘喂零食或小憩；以后可从菜单栏重开引导。完成引导不表示跳过的服务已配置或通过连接验证。", "about")
    };
    public GuideWindow(DesktopController controller)
    {
        owner = controller; Title = "DeskPet · 使用引导"; Width = 610; Height = 480; MinWidth = 420; MinHeight = 320; Topmost = true;
        step = owner.Settings.OnboardingState is "completed" or "skipped" ? 0 : Math.Clamp(owner.Settings.OnboardingStep, 0, Steps.Length - 1);
        owner.Settings.OnboardingState = "deferred";
        open = Ui.Button("打开此步实际界面", OpenStep);
        back = Ui.Button("上一步", () => { if (step > 0) { step--; Persist(); Refresh(); } });
        next = Ui.Button("下一步", () => { if (step == Steps.Length - 1) Finish("completed"); else { step++; Persist(); Refresh(); } });
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 14 };
        layout.Children.Add(title);
        var body = new ScrollViewer { Content = detail, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(body, 1); layout.Children.Add(body);
        var exit = Ui.Button("跳过全部引导", () => Finish("skipped"));
        exit.Name = "GuideSkip"; exit.FontSize = 14; exit.FontWeight = Avalonia.Media.FontWeight.SemiBold; exit.Foreground = DesktopTheme.Brush("#203754");
        var footer = Ui.Column(open, Ui.Row(back, next), Ui.Row(Ui.Button("稍后继续", () => Finish("deferred")), exit));
        Grid.SetRow(footer, 2); layout.Children.Add(footer);
        Content = new Border { Padding = new Thickness(24), Child = layout };
        Closing += (_, _) => { if (!finished) { owner.Settings.OnboardingState = "deferred"; Persist(); } };
        Persist(); Refresh();
    }
    private void Persist() { owner.Settings.OnboardingStep = step; owner.Save(); }
    private void Refresh() { var item = Steps[step]; title.Text = $"{step + 1} / {Steps.Length} · {item.Title}"; detail.Text = item.Detail; back.IsEnabled = step > 0; next.Content = step == Steps.Length - 1 ? "完成引导" : "下一步"; }
    private void OpenStep()
    {
        var section = Steps[step].Section;
        if (section == "chat") owner.ShowChat(true); else if (section == "fullchat") owner.ShowChat(false); else if (section == "about") owner.ShowAbout(); else owner.ShowSettingsSection(section);
    }
    private void Finish(string state) { owner.Settings.OnboardingState = state; Persist(); finished = true; Close(); }
}
