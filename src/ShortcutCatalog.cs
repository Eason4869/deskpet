using System;
using System.Linq;
using System.Windows.Input;
namespace Dafeiyu
{
    internal static partial class ShortcutCatalog
    {
        internal static string Normalize(string value){HotkeyCombination combo;if(!HotkeyCombination.TryParse(value,out combo))return "";return ((combo.Modifiers&2)!=0?"Ctrl+":"")+((combo.Modifiers&1)!=0?"Alt+":"")+((combo.Modifiers&4)!=0?"Shift+":"")+((combo.Modifiers&8)!=0?"Win+":"")+(combo.Key==188?"OemComma":combo.Key==191?"OemQuestion":combo.Key==192?"Oem3":((System.Windows.Forms.Keys)combo.Key).ToString());}
        internal static bool Equal(string left,string right){HotkeyCombination a,b;return HotkeyCombination.TryParse(left,out a)&&HotkeyCombination.TryParse(right,out b)&&a.Key==b.Key&&a.Modifiers==b.Modifiers;}
        internal static bool Matches(string value,KeyEventArgs e){return IsMatch(value,e.Key==Key.System?e.SystemKey:e.Key,Keyboard.Modifiers);}
        internal static bool IsMatch(string value,Key key,ModifierKeys modifiers)
        {
            HotkeyCombination combo;if(!HotkeyCombination.TryParse(value,out combo))return false;
            uint flags=((modifiers&ModifierKeys.Control)!=0?2u:0u)|((modifiers&ModifierKeys.Alt)!=0?1u:0u)|((modifiers&ModifierKeys.Shift)!=0?4u:0u)|((modifiers&ModifierKeys.Windows)!=0?8u:0u);
            return (uint)KeyInterop.VirtualKeyFromKey(key)==combo.Key&&flags==combo.Modifiers;
        }
        internal static bool Validate(PetSettings settings,string id,string value,out string error)
        {
            error="";var definition=All.FirstOrDefault(d=>d.Id==id);if(definition==null){error=TextResources.Get("shortcutCatalog.validate.error");return false;}
            if(String.IsNullOrWhiteSpace(value))return true;
            HotkeyCombination combo;if(!HotkeyCombination.TryParse(value,out combo)){error=TextResources.Get("shortcutCatalog.validate.error.alternativeA");return false;}
            bool functionKey=combo.Key>=112&&combo.Key<=135;
            bool textNavigation=(combo.Key>=33&&combo.Key<=40)&&(combo.Modifiers==2||combo.Modifiers==4||combo.Modifiers==6);
            if((combo.Key==38||combo.Key==40)&&(combo.Modifiers&1)!=0){error=TextResources.Get("shortcutCatalog.validate.error.alternativeB");return false;}
            if((combo.Modifiers==4&&!functionKey)||textNavigation||combo.Key==9||combo.Key==13||combo.Key==27||combo.Key==8||combo.Key==46||Equal(value,"Alt+F4")||Equal(value,"Ctrl+Alt+Delete")||Equal(value,"Ctrl+Tab")||Equal(value,"Ctrl+Shift+Tab")||Equal(value,"Alt+Tab")||Equal(value,"Alt+Shift+Tab")||new[]{"Ctrl+A","Ctrl+C","Ctrl+V","Ctrl+X","Ctrl+Z","Ctrl+Y","Ctrl+Shift+Z"}.Any(v=>Equal(value,v))){error=TextResources.Get("shortcutCatalog.validate.error.alternativeC");return false;}
            foreach(var other in All)if(other.Id!=id&&Equal(value,Value(settings,other))){error=TextResources.Format("shortcutCatalog.validate.errorOtherTitle",other.Title);return false;}
            return true;
        }
    }
}
