using System;
using Dafeiyu;

// Real ledger events for owned UI fixtures, never hand-edited relationship scores.
internal static class RelationshipHomeFixtures
{
    internal static string ProfileBiography(string language)
    {
        switch(language){
            case "zh_CN":return "他是一位带着画册旅行的年轻插画师，喜欢在清晨出门，沿着安静的小路寻找适合写生的地方。白色短发常被风吹得有些凌乱，浅蓝色眼睛却总能发现树叶背面的光、旧窗台上的花，以及街角不起眼的小店。他说话直接，偶尔会用一句玩笑掩饰认真；遇到需要帮忙的事情，通常已经先把袖子卷了起来。休息时，他会整理沿途收集的车票和树叶，给速写补上颜色，也会尝试按照当地食谱做一顿简单的晚饭。雨天适合读书，晴天适合散步，而一杯温热的牛奶和一张尚未画完的纸，就足以让他安心地度过一个下午。出发前，他总会检查铅笔、雨伞和背包侧袋里的小手电，给下一段旅程留出从容的余地。";
            case "en_US":return "A young illustrator who travels with a sketchbook, he prefers quiet paths and the soft light of early morning. His white hair rarely stays tidy, but his blue eyes notice the small things: a leaf catching the sun, flowers on an old windowsill, or a welcoming shop at the end of a side street. He speaks plainly and sometimes hides his earnestness behind a dry joke. When someone needs a hand, he is usually already rolling up his sleeves. On rainy afternoons he sorts collected tickets and leaves, adds color to unfinished sketches, and tries a simple recipe from the places he has visited. Before setting out again, he checks his pencils, umbrella, and the little torch in his backpack.";
            case "de_DE":return "Der junge Illustrator reist mit einem Skizzenbuch und ist am liebsten früh am Morgen unterwegs, wenn die Wege noch ruhig sind. Sein weißes Haar wird schnell vom Wind zerzaust, doch seine blauen Augen entdecken selbst kleine Einzelheiten: Licht auf einem Blatt, Blumen an einem alten Fenster oder einen freundlichen Laden in einer Seitenstraße. Er spricht direkt und versteckt seinen Ernst manchmal hinter einem trockenen Witz. Wird Hilfe gebraucht, hat er meist schon die Ärmel hochgekrempelt. An Regentagen ordnet er gesammelte Fahrkarten, koloriert angefangene Zeichnungen und probiert einfache Rezepte aus. Vor der nächsten Etappe prüft er seine Stifte, den Regenschirm und die kleine Taschenlampe im Rucksack.";
            case "ja_JP":return "スケッチブックを手に旅をする若いイラストレーター。人通りの少ない朝の小道を歩き、木の葉を透かす光や、古い窓辺に飾られた花、小さな路地の店先を描くのが好き。白い髪は風ですぐに乱れるけれど、青い目は周りのささやかな変化をよく見ている。言葉は少し率直で、真剣な気持ちをごまかすように冗談を言うこともある。誰かが困っていると、考えるより先に袖をまくって手伝いに向かう。雨の日には集めた切符や葉を整理し、描きかけの絵に色を重ねたり、旅先で教わった料理を作ったりして過ごす。次の出発前には鉛筆と傘、それからリュックの小さな懐中電灯を確かめる。";
            default:return ProfileBiography("en_US");
        }
    }
    internal static string ProfileName(string language)
    {
        switch(language){
            case "zh_CN":return "自定义角色示例";
            case "en_US":return "Custom companion example";
            case "de_DE":return "Eigene Figur als Beispiel";
            case "ja_JP":return "カスタムキャラクターの例";
            default:return ProfileName("en_US");
        }
    }
    internal static void Seed156(RelationshipService service,string id)
    {
        var now=RelationshipTime.Current();
        for(int day=0;day<15;day++){
            var time=new RelationshipTime(now.UtcNow.AddDays(day-20),now.LocalOffset);
            Record(service,id,RelationshipEventType.Visit,"seed-visit-"+day,time);
            Record(service,id,RelationshipEventType.SuccessfulWorkTask,"seed-work-a-"+day,time);
            Record(service,id,RelationshipEventType.SuccessfulWorkTask,"seed-work-b-"+day,time);
        }
        Record(service,id,RelationshipEventType.Visit,"seed-current-visit",now);
        Record(service,id,RelationshipEventType.SuccessfulWorkTask,"seed-current-work",now);
    }
    internal static void SeedMaximum(RelationshipService service,string id)
    {
        var now=RelationshipTime.Current();
        for(int day=0;day<100;day++){
            var time=new RelationshipTime(now.UtcNow.AddDays(day-99),now.LocalOffset);
            Record(service,id,RelationshipEventType.Visit,"max-visit-"+day,time);
            Record(service,id,RelationshipEventType.SuccessfulWorkTask,"max-work-a-"+day,time);
            Record(service,id,RelationshipEventType.SuccessfulWorkTask,"max-work-b-"+day,time);
        }
    }
    internal static void ReachDailyLimit(RelationshipService service,string id)
    {
        // Keep the owned cap fixture at local noon so simulated cooldowns cannot cross midnight.
        var clock=RelationshipTime.Current();var now=new RelationshipTime(clock.UtcNow,TimeSpan.FromHours(12-clock.UtcNow.Hour));int index=0;
        var types=new[]{RelationshipEventType.Visit,RelationshipEventType.SuccessfulWorkTask,RelationshipEventType.SuccessfulWorkTask,RelationshipEventType.CompletedConversation,RelationshipEventType.CompletedConversation,RelationshipEventType.CompletedConversation,RelationshipEventType.CompletedConversation,RelationshipEventType.UserInteraction,RelationshipEventType.UserInteraction,RelationshipEventType.UserInteraction,RelationshipEventType.UserInteraction,RelationshipEventType.UserInteraction,RelationshipEventType.UserInteraction};
        foreach(var type in types){index++;Record(service,id,type,"cap-fixture-"+index,new RelationshipTime(now.UtcNow.AddSeconds(index*25),now.LocalOffset));}
    }
    static void Record(RelationshipService service,string id,RelationshipEventType type,string token,RelationshipTime time)
    {var result=service.Record(id,type,token,time);if(!result.Success)throw new Exception("UI fixture ledger: "+result.Error);}
}
