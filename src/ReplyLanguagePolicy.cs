using System;
namespace Dafeiyu
{
    // Outgoing request policy only: never persist this suffix into the user's persona.
    public static class ReplyLanguagePolicy
    {
        const string Begin="\n\n[DeskPet reply language]\n";
        const string End="\n[/DeskPet reply language]";
        internal static string WithoutInstruction(string systemPrompt)
        {
            string prompt=systemPrompt??"";
            foreach(var choice in UiLanguages.Choices)
            {
                string previous=Begin+TextResources.CatalogFor(choice.Code).Get("languages.replyInstruction")+End;
                if(prompt.EndsWith(previous,StringComparison.Ordinal)){prompt=prompt.Substring(0,prompt.Length-previous.Length);break;}
            }
            return prompt;
        }
        public static string Apply(string systemPrompt)
        {return Apply(systemPrompt,LanguageRequestScope.Capture());}
        public static string Apply(string systemPrompt,LanguageSelection language)
        {return WithoutInstruction(systemPrompt)+Begin+TextResources.Get("languages.replyInstruction",language)+End;}
    }
}
