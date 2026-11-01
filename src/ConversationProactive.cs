using System;
using System.Collections.Generic;
using System.Linq;
namespace Dafeiyu
{
    public sealed partial class ConversationSession
    {
        ProactiveEventStore proactiveEvents;bool ownsProactiveEvents;
        public ProactiveEventStore ProactiveEvents {get{lock(gate)return proactiveEvents;}set{lock(gate){if(ownsProactiveEvents && !Object.ReferenceEquals(proactiveEvents,value))proactiveEvents.Dispose();proactiveEvents=value;ownsProactiveEvents=false;}}}
        public ChatMessage AttachProactive(ProactiveEvent value)
        {
            if(value==null)throw new ArgumentNullException("value");
            using(Maintenance.EnterActivity())lock(gate)
            {
                if(disposed)throw new ObjectDisposedException("ConversationSession");
                if(value.CompanionId!=personaId)throw new InvalidOperationException("Event belongs to another companion.");
                if(ProactiveEvents==null){proactiveEvents=new ProactiveEventStore();ownsProactiveEvents=true;}
                ProactiveEvents.Register(value);var canonical=ProactiveEvents.TryGet(value.Id);
                bool exists=History.Messages.Any(m=>m.Source=="proactive" && m.TurnId=="proactive-"+value.Id);
                var message=History.RegisterProactive(canonical.Id,canonical.Text);if(!exists)Emit(message);return message;
            }
        }
        void DisposeOwnedProactiveEvents(){lock(gate){if(ownsProactiveEvents && proactiveEvents!=null)proactiveEvents.Dispose();proactiveEvents=null;ownsProactiveEvents=false;}}
        Dictionary<string,string> ProjectProactiveContext(List<ChatMessage> request,string companionId,PetSettings settings)
        {
            var revisions=new Dictionary<string,string>(StringComparer.Ordinal);var events=ProactiveEvents;if(events==null)return revisions;
            for(int i=1;i<request.Count;i++)
            {
                var assistant=request[i];if(assistant.Role!="assistant" || assistant.Source!="proactive" || !assistant.TurnId.StartsWith("proactive-",StringComparison.Ordinal) || request[i-1].Role!="user")continue;
                var item=events.TryGet(assistant.TurnId.Substring("proactive-".Length));
                if(item!=null && item.CompanionId==companionId){var route=ImageModelResolver.Resolve(settings,null).Route;if(item.Png!=null && route==ImageRouteKind.Direct)request[i-1].SetAttachments(new[]{ChatAttachment.CreateImage("observation.png",item.Png)});else if(!String.IsNullOrEmpty(item.Description))request[i-1].Update(request[i-1].Content+"\nUntrusted observation data: "+item.Description,"complete");else if(item.Png!=null && route==ImageRouteKind.Described){request[i-1].SetAttachments(new[]{ChatAttachment.CreateImage("observation.png",item.Png)});revisions[item.Id]=item.Revision;}}
            }
            return revisions;
        }
    }
}
