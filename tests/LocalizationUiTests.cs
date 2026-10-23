using System;
using System.Reflection;
using System.Windows.Controls;
namespace Dafeiyu.Tests
{
    public static class LocalizationUiTests
    {
        static int checks;
        static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
        [STAThread]public static int Main()
        {
            try
            {
                var choices=ImageGenerationSettings.DefaultChoices(new[]{"1024x1024"});
                choices.SelectedIndex=0;
                Check(ImageGenerationSettings.Optional(choices)=="","default option omits parameter");
                choices.ItemsSource=new[]{"A different display label","1024x1024"};choices.SelectedIndex=0;
                Check(ImageGenerationSettings.Optional(choices)=="","default identity independent of display text");
                choices.SelectedIndex=1;
                Check(ImageGenerationSettings.Optional(choices)=="1024x1024","custom choice retained");
                var method=typeof(ConversationSession).GetMethod("PreviewSpeechAsync");
                Check(method.ReturnType==typeof(System.Threading.Tasks.Task<bool>),"speech preview exposes success result instead of notice comparison");
                Check(typeof(ConversationRenameDialog).GetConstructors()[0].GetParameters().Length==4,"rename dialog has explicit section purpose");
                Console.WriteLine("LocalizationUiTests: "+checks+" passed");return 0;
            }
            catch(Exception e){Console.Error.WriteLine("FAIL LocalizationUiTests: "+e);return 1;}
        }
    }
}
