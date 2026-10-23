using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace Dafeiyu
{
    public sealed class HotkeyCombination
    {
        public uint Modifiers { get; private set; }
        public uint Key { get; private set; }
        public static bool TryParse(string value,out HotkeyCombination combination)
        {
            combination=null;if(String.IsNullOrWhiteSpace(value))return false;
            uint modifiers=0,key=0;bool hasKey=false;
            foreach(string raw in value.Split('+')) {
                string part=raw.Trim();uint flag=part.Equals("Ctrl",StringComparison.OrdinalIgnoreCase)?2u:part.Equals("Alt",StringComparison.OrdinalIgnoreCase)?1u:part.Equals("Shift",StringComparison.OrdinalIgnoreCase)?4u:part.Equals("Win",StringComparison.OrdinalIgnoreCase)?8u:0u;
                if(flag!=0){if((modifiers&flag)!=0)return false;modifiers|=flag;continue;}
                if(part==",")part="OemComma";else if(part=="/")part="OemQuestion";else if(part=="`")part="Oem3";else if(part=="←")part="Left";else if(part=="→")part="Right";
                if(part.Length==1&&part[0]>='0'&&part[0]<='9')part="D"+part;
                Keys parsed;if(hasKey || part.Length==0 || Char.IsDigit(part[0]) || !Enum.TryParse<Keys>(part,true,out parsed) || !Enum.IsDefined(typeof(Keys),parsed))return false;
                key=(uint)parsed;if(key<8 || key>254 || parsed==Keys.ControlKey || parsed==Keys.Menu || parsed==Keys.ShiftKey || parsed==Keys.LWin || parsed==Keys.RWin)return false;hasKey=true;
            }
            if(!hasKey || modifiers==0)return false;combination=new HotkeyCombination{Modifiers=modifiers,Key=key};return true;
        }
    }
    public sealed class HotkeyRegistry : IDisposable
    {
        [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterHotKey(IntPtr handle,int id,uint modifiers,uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr handle,int id);
        sealed class Binding { public int Id;public HotkeyCombination Combination; }
        readonly IntPtr handle;readonly Dictionary<string,Binding> bindings=new Dictionary<string,Binding>();
        static int sequence=16000;bool disposed;
        public HotkeyRegistry(IntPtr window){handle=window;}
        public bool TryBind(string action,string value,out string error)
        {
            error="";if(disposed){error=TextResources.Get("hotkeys.tryBind.error");return false;}
            Binding old;bindings.TryGetValue(action,out old);
            if(String.IsNullOrWhiteSpace(value)){if(old!=null){UnregisterHotKey(handle,old.Id);bindings.Remove(action);}return true;}
            HotkeyCombination combo;if(!HotkeyCombination.TryParse(value,out combo)){error=TextResources.Get("hotkeys.tryBind.error.alternativeA");return false;}
            if(old!=null && old.Combination.Key==combo.Key && old.Combination.Modifiers==combo.Modifiers)return true;
            // Register the replacement first. A conflict leaves the old registration intact.
            int id=System.Threading.Interlocked.Increment(ref sequence);
            if(!RegisterHotKey(handle,id,combo.Modifiers|0x4000,combo.Key)){error=TextResources.Get("hotkeys.tryBind.error.alternativeB");return false;}
            if(old!=null)UnregisterHotKey(handle,old.Id);
            bindings[action]=new Binding{Id=id,Combination=combo};return true;
        }
        public string ActionForId(int id){foreach(var pair in bindings)if(pair.Value.Id==id)return pair.Key;return null;}
        public void Dispose(){if(disposed)return;disposed=true;foreach(var pair in bindings)UnregisterHotKey(handle,pair.Value.Id);bindings.Clear();}
    }
}
