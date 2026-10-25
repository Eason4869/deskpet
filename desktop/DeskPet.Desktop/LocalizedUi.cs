using Avalonia;
using Avalonia.Controls;
using Dafeiyu;
namespace DeskPet.Desktop;
internal static class LocalizedUi
{
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control,object> tracked=new();
    static void TrackLifetime(object target)
    {
        if(target is not Control control||tracked.TryGetValue(control,out _))return;tracked.Add(control,new object());
        control.AttachedToVisualTree+=(_,_)=>LiveTextBindings.Enable(control,true);
        control.DetachedFromVisualTree+=(_,_)=>LiveTextBindings.Enable(control,false);
        if(control is Window window)window.Closed+=(_,_)=>LiveTextBindings.Enable(window,false);
    }
    internal static T With<T>(T target,string property,Func<string> render) where T:class {var info=target.GetType().GetProperty(property);if((property=="Content"||property=="Header")&&info?.GetValue(target) is TextBlock label)Set(label,"Text",render);else Set(target,property,render);return target;}
    internal static void Tooltip(Control target,Func<string> render){TrackLifetime(target);if(ToolTip.GetTip(target) is ToolTip tip)With(tip,"Content",render);else LiveTextBindings.Bind(target,"ToolTip",render,value=>ToolTip.GetTip((Control)value),(value,text)=>ToolTip.SetTip((Control)value,text));}
    internal static Button Caption(Button button,Func<string> render){if(ToolTip.GetTip(button)!=null)Tooltip(button,render);Automation(button,render);return button;}
        internal static void Automation(StyledElement target,Func<string> render){TrackLifetime(target);LiveTextBindings.Bind(target,"AutomationName",render,value=>Avalonia.Automation.AutomationProperties.GetName((StyledElement)value),(value,text)=>Avalonia.Automation.AutomationProperties.SetName((StyledElement)value,(string)text));}
    internal static string Set(object target,string property,Func<string> render){TrackLifetime(target);LiveTextBindings.Bind(target,property,render);return render();}
    internal static void Bind(AvaloniaObject target,AvaloniaProperty property,string key,Func<object[]>? args=null)
    {TrackLifetime(target);LiveTextBindings.Bind(target,property.Name,()=>args==null?TextResources.CaptureLanguage().Get(key):TextResources.CaptureLanguage().Format(key,args()),value=>((AvaloniaObject)value).GetValue(property),(value,text)=>((AvaloniaObject)value).SetValue(property,text));}
    internal static ComboBox SetOptions(ComboBox control,Func<IEnumerable<string>> render){control.ItemsSource=render();return Options(control,render);}
    internal static ComboBox Options(ComboBox control,Func<IEnumerable<string>> render)
    {
        var original=control.ItemsSource?.Cast<object>().ToArray()??Array.Empty<object>();
        control.ItemTemplate=new Avalonia.Controls.Templates.FuncDataTemplate<object>((value,_)=>With(new TextBlock(),"Text",()=>{int index=Array.IndexOf(original,value);var labels=render().ToArray();return index>=0&&index<labels.Length?labels[index]:value?.ToString()??"";}));return control;
    }
    internal static TextBox Input(string value,Func<string> hint)=>With(Ui.Input(value,hint()),"PlaceholderText",hint);
    internal static TextBlock Text(Func<string> render,double size=14)=>With(Ui.Text(render(),size),"Text",render);
    internal static Button Button(Func<string> render,Action action)=>With(Ui.Button(render(),action),"Content",render);
    internal static Button AsyncButton(Func<string> render,Func<Task> action,Action<Exception> error)=>With(Ui.AsyncButton(render(),action,error),"Content",render);
    internal static CheckBox Check(Func<string> render,bool value,Action<bool> changed)=>With(Ui.Check(render(),value,changed),"Content",render);
    internal static Window Window(Func<string> title,Control content,double width=650,double height=620)=>With(Ui.Window(title(),content,width,height),"Title",title);
    internal static Control Field(Func<string> label,Control input)=>Ui.Column(Text(label),input);
}
