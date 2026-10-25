using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dafeiyu
{
    internal static class LocalizedUi
    {
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement,object> tracked=new System.Runtime.CompilerServices.ConditionalWeakTable<FrameworkElement,object>();
        static void TrackLifetime(object target)
        {
            var element=target as FrameworkElement;if(element==null)return;object marker;if(tracked.TryGetValue(element,out marker))return;tracked.Add(element,new object());
            element.Loaded+=delegate{LiveTextBindings.Enable(element,true);};element.Unloaded+=delegate{LiveTextBindings.Enable(element,false);};
            var window=element as Window;if(window!=null)window.Closed+=delegate{LiveTextBindings.Enable(window,false);};
        }
        internal static T With<T>(T target,string property,Func<string> render) where T:class
        {var info=target.GetType().GetProperty(property);var label=info==null?null:info.GetValue(target,null) as TextBlock;if((property=="Content"||property=="Header")&&label!=null)Set(label,"Text",render);else Set(target,property,render);return target;}
        internal static void Automation(DependencyObject target,Func<string> render){TrackLifetime(target);LiveTextBindings.Bind(target,"AutomationName",render,value=>System.Windows.Automation.AutomationProperties.GetName((DependencyObject)value),(value,text)=>System.Windows.Automation.AutomationProperties.SetName((DependencyObject)value,(string)text));}
        internal static string Set(object target,string property,Func<string> render)
        {TrackLifetime(target);LiveTextBindings.Bind(target,property,render);return render();}
        internal static void Bind(DependencyObject target,DependencyProperty property,string key,Func<object[]> args=null)
        {TrackLifetime(target);LiveTextBindings.Bind(target,property.Name,()=>args==null?TextResources.CaptureLanguage().Get(key):TextResources.CaptureLanguage().Format(key,args()),value=>((DependencyObject)value).GetValue(property),(value,text)=>((DependencyObject)value).SetValue(property,text));}
        sealed class ChoiceConverter:System.Windows.Data.IMultiValueConverter
        {
            internal object[] Original;internal Func<System.Collections.Generic.IEnumerable<string>> Render;
            public object Convert(object[] values,Type targetType,object parameter,System.Globalization.CultureInfo culture){int index=Array.IndexOf(Original,values[0]);var labels=System.Linq.Enumerable.ToArray(Render());return index>=0&&index<labels.Length?labels[index]:values[0];}
            public object[] ConvertBack(object value,Type[] targets,object parameter,System.Globalization.CultureInfo culture){throw new NotSupportedException();}
        }
        internal static ComboBox SetOptions(ComboBox control,Func<System.Collections.Generic.IEnumerable<string>> render){control.ItemsSource=render();return Options(control,render);}
        internal static ComboBox Options(ComboBox control,Func<System.Collections.Generic.IEnumerable<string>> render)
        {
            var original=System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Cast<object>(control.ItemsSource));
            var text=new FrameworkElementFactory(typeof(TextBlock));var binding=new System.Windows.Data.MultiBinding{Converter=new ChoiceConverter{Original=original,Render=render}};
            binding.Bindings.Add(new System.Windows.Data.Binding());binding.Bindings.Add(new System.Windows.Data.Binding("Code"){Source=LiveTextBindings.Revision});text.SetBinding(TextBlock.TextProperty,binding);control.ItemTemplate=new DataTemplate{VisualTree=text};return control;
        }
        internal static TextBlock Text(Func<string> render,double size=13,Brush color=null){return With(Ui.Text(render(),size,color),"Text",render);}
        internal static TextBlock InlineText(Func<string> render,double size=13,Brush color=null){return With(Ui.InlineText(render(),size,color),"Text",render);}
        internal static Button Button(Func<string> render,bool primary=false){return With(Ui.Button(render(),primary),"Content",render);}
        internal static CheckBox Toggle(Func<string> render,bool value,Action<bool> changed){return With(Ui.Toggle(render(),value,changed),"Content",render);}
        internal static void Window(Window window,Func<string> title,double width,double height){Ui.Window(window,title(),width,height);Set(window,"Title",title);}
        internal static void Label(Panel panel,Func<string> caption,UIElement field){Ui.Label(panel,caption(),field);var label=panel.Children[panel.Children.Count-2] as TextBlock;if(label!=null)Set(label,"Text",caption);}
    }
}
