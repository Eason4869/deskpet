using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace Dafeiyu
{
    // Bound brush colors remain live even when WPF seals a style or template.
    internal static class ThemeManager
    {
        sealed class ThemeColor : INotifyPropertyChanged
        {
            internal readonly string Light,Dark;
            internal readonly SolidColorBrush Brush;
            public Color Color { get { return (Color)ColorConverter.ConvertFromString((IsDark&&Dark!=null?Dark:ThemeColors.Resolve(Light, IsDark))); } }
            public event PropertyChangedEventHandler PropertyChanged;
            internal ThemeColor(string light,string dark)
            {
                Light=light;Dark=dark;Brush=new SolidColorBrush();
                BindingOperations.SetBinding(Brush,SolidColorBrush.ColorProperty,new Binding("Color"){Source=this,Mode=BindingMode.OneWay});
            }
            internal void Refresh(){if(PropertyChanged!=null)PropertyChanged(this,new PropertyChangedEventArgs("Color"));}
        }
        static readonly Dictionary<string,ThemeColor> colors=new Dictionary<string,ThemeColor>(StringComparer.OrdinalIgnoreCase);
        static string mode="system";
        static bool listening;
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
        internal static void TrackWindow(Window window)
        {
            window.SourceInitialized+=delegate{ApplyWindowChrome(window);};
        }
        static void ApplyWindowChrome(Window window)
        {
            var handle=new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if(handle==IntPtr.Zero)return;
            int dark=IsDark?1:0;
            DwmSetWindowAttribute(handle,20,ref dark,sizeof(int));
        }
        internal static bool IsDark {get;private set;}
        internal static Brush Surface {get{return Brush("#FFFFFF");}}
        internal static SolidColorBrush CardSurface {get{return Brush("#FFFFFF","#2B3545");}}
        internal static SolidColorBrush CardBorder {get{return Brush("#DCE5F4","#52637C");}}
        internal static SolidColorBrush FieldSurface {get{return Brush("#FFFFFF","#161E2B");}}
        internal static SolidColorBrush SelectedSurface {get{return Brush("#E8EBF0","#334E73");}}
        internal static SolidColorBrush SelectedBorder {get{return Brush("#E8EBF0","#6385B1");}}
        internal static SolidColorBrush ToggleOffSurface {get{return Brush("#CBD5E7","#63738B");}}
        internal static SolidColorBrush ActionSurface {get{return Brush("#202329","#42516B");}}
        internal static SolidColorBrush Caret {get{return Brush("#203754","#FFFFFF");}}
        internal static SolidColorBrush Brush(string light,string dark=null)
        {
            ThemeColor entry;string identity=dark==null?light:light+"."+dark;
            if(!colors.TryGetValue(identity,out entry)){entry=new ThemeColor(light,dark);colors.Add(identity,entry);}
            if(Application.Current!=null)
            {
                string key="DeskPet.Color."+identity.TrimStart('#').ToUpperInvariant();
                if(!Object.ReferenceEquals(Application.Current.Resources[key],entry.Brush))Application.Current.Resources[key]=entry.Brush;
            }
            return entry.Brush;
        }
        internal static void Apply(string preference)
        {
            mode=ThemeColors.Normalize(preference);
            if(!listening&&Application.Current!=null)
            {
                SystemEvents.UserPreferenceChanged+=OnSystemPreferenceChanged;
                Application.Current.Exit+=delegate{SystemEvents.UserPreferenceChanged-=OnSystemPreferenceChanged;listening=false;};listening=true;
            }
            RefreshSystemTheme();
        }
        static void OnSystemPreferenceChanged(object sender,UserPreferenceChangedEventArgs args)
        {
            var app=Application.Current;
            if(mode=="system"&&app!=null&&!app.Dispatcher.HasShutdownStarted)app.Dispatcher.BeginInvoke(new Action(RefreshSystemTheme));
        }
        internal static void RefreshSystemTheme()
        {
            bool systemDark=false;
            if(mode=="system")try
            {
                using(var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    systemDark=key!=null&&Convert.ToInt32(key.GetValue("AppsUseLightTheme",1))==0;
            }
            catch(System.Security.SecurityException){}catch(UnauthorizedAccessException){}catch(System.IO.IOException){}
            SetSystemTheme(systemDark);
        }
        // Separated from the OS query so system changes and explicit overrides are testable.
        internal static void SetSystemTheme(bool systemDark)
        {
            IsDark=mode=="dark"||(mode=="system"&&systemDark);
            foreach(var entry in colors.Values)entry.Refresh();
            if(Application.Current!=null)foreach(Window window in Application.Current.Windows)ApplyWindowChrome(window);
        }
        internal static object Parse(string xaml)
        {
            // Only brush attributes/setters are themed; artwork, white accent text and geometry stay literal.
            xaml=System.Text.RegularExpressions.Regex.Replace(xaml,@"(?<prefix>(?:Background|BorderBrush|Foreground|Fill|Stroke)\s*=\s*')(?<color>#[0-9A-Fa-f]{6,8}|White|Black)(?<suffix>')",ReplaceColor);
            xaml=System.Text.RegularExpressions.Regex.Replace(xaml,@"(?<prefix>Property='(?:Background|BorderBrush|Foreground|Fill|Stroke)'\s+Value=')(?<color>#[0-9A-Fa-f]{6,8}|White|Black)(?<suffix>')",ReplaceColor);
            return XamlReader.Parse(xaml);
        }
        static string ReplaceColor(System.Text.RegularExpressions.Match match)
        {
            string color=match.Groups["color"].Value;
            // White foregrounds are used on accent buttons and tooltips.
            if(color=="White"&&(match.Groups["prefix"].Value.Contains("Foreground")||match.Groups["prefix"].Value.Contains("Fill")))return match.Value;
            if(color=="White")color="#FFFFFF";else if(color=="Black")color="#000000";
            Brush(color);
            return match.Groups["prefix"].Value+"{DynamicResource DeskPet.Color."+color.Substring(1).ToUpperInvariant()+"}"+match.Groups["suffix"].Value;
        }
    }
}
