using System;
using System.Collections.Generic;

namespace Dafeiyu
{
    public sealed class CompanionAction
    {
        public string Id {get;private set;}
        public string Name {get;private set;}
        readonly Action<PetWindow> play;
        public CompanionAction(string id,string name,Action<PetWindow> playback)
        {if(String.IsNullOrWhiteSpace(id) || String.IsNullOrWhiteSpace(name) || playback==null)throw new ArgumentException("动作需要 ID、名称和播放方法。");Id=id;Name=name;play=playback;}
        public void Play(PetWindow pet){play(pet);}
    }
    // A future holiday module supplies one active action for the local date.
    // Returning null leaves the ordinary four choices intact; no dummy fifth slot.
    public interface IHolidayHoverActionProvider
    {
        CompanionAction GetAction(SkinDefinition skin,DateTime localDate);
    }
    public static class HoverActions
    {
        public static IList<CompanionAction> Choices(SkinDefinition skin,IHolidayHoverActionProvider holidays,DateTime localDate)
        {
            var actions=new List<CompanionAction>{
                new CompanionAction("jumping","跳跃",p=>p.PlayAction("jumping",.7)),
                new CompanionAction("waving","打招呼",p=>p.PlayAction("waving",1.2)),
                new CompanionAction("special",skin.SpecialAction,p=>p.PlayInteraction("special"))};
            if(AdditionalInteractions.Has(skin,"special2"))actions.Add(new CompanionAction("special2",AdditionalInteractions.SpecialName(skin),p=>p.PlayInteraction("special2")));
            var holiday=holidays==null?null:holidays.GetAction(skin,localDate.Date);
            if(holiday!=null && !actions.Exists(a=>a.Id==holiday.Id))actions.Add(holiday);
            return actions.AsReadOnly();
        }
    }
}
