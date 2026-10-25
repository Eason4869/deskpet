using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
namespace Dafeiyu
{
    // Static notification holds weak registrations, never windows or edit buffers.
    internal static class LiveTextBindings
    {
        sealed class Owner
        {
            internal readonly WeakReference Target;
            internal readonly Dictionary<string,Entry> Entries=new Dictionary<string,Entry>();
            internal bool Enabled=true;
            internal Owner(object target){Target=new WeakReference(target);}
        }
        sealed class Entry
        {
            internal Func<string> Render;
            internal Func<object,object> Read;
            internal Action<object,object> Write;
            internal object Last;
            internal bool Initialized;
            internal void Refresh(object target)
            {
                // An ordinary state update or user edit owns a changed value.
                if(Initialized&&!Object.Equals(Read(target),Last))return;
                string value=Render();Write(target,value);Last=value;Initialized=true;
            }
        }
        internal sealed class LanguageRevision:System.ComponentModel.INotifyPropertyChanged
        {
            public string Code {get{return TextResources.CurrentLanguage;}}
            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
            internal void Notify(){var handler=PropertyChanged;if(handler!=null)handler(this,new System.ComponentModel.PropertyChangedEventArgs("Code"));}
        }
        internal static readonly LanguageRevision Revision=new LanguageRevision();
        static readonly ConditionalWeakTable<object,Owner> owners=new ConditionalWeakTable<object,Owner>();
        static readonly List<WeakReference> registrations=new List<WeakReference>();
        static LiveTextBindings(){TextResources.LanguageChanged+=Changed;}
        static Owner GetOwner(object target)
        {
            Owner owner;if(owners.TryGetValue(target,out owner))return owner;
            if(registrations.Count%128==0)registrations.RemoveAll(reference=>!reference.IsAlive);owner=new Owner(target);owners.Add(target,owner);registrations.Add(new WeakReference(owner));return owner;
        }
        internal static void Enable(object target,bool enabled)
        {Owner owner;if(owners.TryGetValue(target,out owner)){owner.Enabled=enabled;if(enabled)Refresh(owner);}}
        internal static void Bind(object target,string property,Func<string> render)
        {
            if(target==null)throw new ArgumentNullException("target");
            var info=target.GetType().GetProperty(property,BindingFlags.Instance|BindingFlags.Public);
            if(info==null||!info.CanWrite)throw new ArgumentException("Missing display property: "+property);
            Bind(target,property,render,value=>info.GetValue(value,null),(value,text)=>info.SetValue(value,text,null));
        }
        internal static void Bind(object target,string identity,Func<string> render,Func<object,object> read,Action<object,object> write)
        {
            var owner=GetOwner(target);var entry=new Entry{Render=render,Read=read,Write=write};owner.Entries[identity]=entry;entry.Refresh(target);
        }
        static void Changed(object sender,EventArgs args)
        {
            Revision.Notify();
            for(int i=registrations.Count-1;i>=0;i--){var owner=registrations[i].Target as Owner;if(owner==null){registrations.RemoveAt(i);continue;}if(owner.Enabled)Refresh(owner);}
        }
        static void Refresh(Owner owner)
        {
            var target=owner.Target.Target;if(target==null)return;
            foreach(var entry in new List<Entry>(owner.Entries.Values))try{entry.Refresh(target);}catch(Exception error){foreach(System.Diagnostics.TraceListener sink in System.Diagnostics.Trace.Listeners)try{sink.WriteLine("Localized display refresh failed: "+error);}catch(Exception){}}
        }
    }
}
