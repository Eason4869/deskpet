using System;
using System.Collections.Generic;
namespace Dafeiyu
{
    // Shared palette for the WPF and Avalonia clients. Unknown colors (including
    // provider artwork and charts) retain their original value.
    public static class ThemeColors
    {
        static readonly Dictionary<string,string> dark=Create();
        public static string Normalize(string mode){return mode=="light"||mode=="dark"?mode:"system";}
        public static string Resolve(string light,bool isDark){string value;return isDark&&dark.TryGetValue(light,out value)?value:light;}
        static Dictionary<string,string> Create()
        {
            var result=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            Add(result,"#10E5EAF2","#10000000");
            Add(result,"#191F2A","#F7F8FA #F7F9FD #F8F9FB #FAFAFA #FAFAFB #FAFBFD");
            Add(result,"#1D2430","#E1F0ED #E2ECF9 #EDF3F8 #F5F7FB");
            Add(result,"#203D35","#E0F8EC #E8F5EF #E8F7ED");
            Add(result,"#20E5EAF2","#20000000");
            Add(result,"#222832","#C9DCFC #DCE5F4 #DCE7F8 #E0E9FA #E0EAFC #E2F8FB #E4EAF0 #E8EBF0 #E8EEFB #E9ECF0 #E9EDF3 #EAF1FD #EAF2FF #ECEEF1 #ECEFF2 #EDEEF0 #EDF3FE #EDF4FF #EEEEF0 #EFF1F5 #EFF3FA #F0F1F2 #F0F1F4 #F0F2F5 #F0F3F7 #F0F4FF #F1F3F5 #F1F4F9 #F2F3F5 #F2F4F6 #F2F4F7 #F3F4F5 #F3F5F8 #F3F6FE #F4F5F7 #F5DCE1 #F6F6F7 #F7F7F6 #F7F7F8 #FFF6E8 #FFFFFF");
            Add(result,"#252D3A","#FDFEFF");
            Add(result,"#263B58","#DBE4FC #DCE5FF #DCE7FF #E8F2FF #EDF3FF #F2F7FF");
            Add(result,"#2D3A50","#EAEAEA #ECECEC #EDEEF1 #EFEFEF #F0F1F3 #F1F1F1 #F3F3F3");
            Add(result,"#354258","#BCCFEB #C9D2DE #CBD5E7 #CDD3DD #D4D6DA #D4DEEB #D5D9DF #D5E1ED #D8DEE7 #DADDE0 #DADDE1 #DCE0E5 #DDDEE1 #DDE1E7 #DDE2E8 #DDE3EA #DEDFE2 #DEE0E3 #DEE6EE #DFDFDF #E0E3E8 #E0E4E9 #E1E8EE #E2E3E6 #E2E4E8 #E2E5EA #E3E7EC #E3E7ED #E4E5E8 #E4E7EC #E5E7EB #E6E7EA #E6E8EC #E6E9EE #E9EAEC #E9EAED #EBEDEF #ECEBE8 #ECEDEF");
            Add(result,"#452B32","#F9E5E5 #FAE7E6 #FBE9E9 #FCE8EC #FDECEC #FFF3F5");
            Add(result,"#56151D2A","#56EEF0F3");
            Add(result,"#6024272D","#60FFFFFF");
            Add(result,"#687484","#9DA8B8 #A1A6AE #ADB0B5 #AEB4BD #B8BCC2 #BABDC2 #C5C9D0");
            Add(result,"#77CCA3","#00A86B #157A45 #159A5A #227443 #2E7D4D #44A6A1 #6AAFA4");
            Add(result,"#9DBBFF","#126F80 #177A88 #243044 #6B809E");
            Add(result,"#AFBAC9","#555E69 #566171 #59616D #5C6778 #61738B #626D7F #666B73 #687282 #72777F #72839B #747B83 #777E88 #777F89 #7A7E86 #7B8490 #81858B #818894 #85898F #858D99 #89929F #929397 #92969D #9299A3 #939BA6 #939BA7 #96999E #969BA2 #969BA3 #9CA3AC #A0A4AB");
            Add(result,"#B024272D","#B0E9F2FF");
            Add(result,"#BF24272D","#BFFFFFFF");
            Add(result,"#C324272D","#C3FAFCFF");
            Add(result,"#C424272D","#C4FAFDFF");
            Add(result,"#CD24272D","#CDEEF5FF");
            Add(result,"#D7B783","#93AB73");
            Add(result,"#D824272D","#D8FFFFFF");
            Add(result,"#DB24272D","#DBFFFFFF");
            Add(result,"#DE24272D","#DEF9FCFF #DEFFFFFF");
            Add(result,"#E5EAF2","#000000 #1C2024 #202329 #203754 #222222 #222426 #24262B #25282D #252A31 #282A2F #323943 #3B3B3B #3F434A #4A4A4A #53565C #555555");
            Add(result,"#EC24272D","#ECFFFFFF");
            Add(result,"#ED24272D","#EDF4FAFF");
            Add(result,"#EF24272D","#EFFFFFFF");
            Add(result,"#F224272D","#F2FFFFFF");
            Add(result,"#F2949D","#806000 #813B9B #8A6500 #8D48A5 #91682E #926B32 #A54A37 #A65050 #AC425D #AC4A57");
            Add(result,"#FA24272D","#FAFFFFFF");
            return result;
        }
        static void Add(Dictionary<string,string> result,string color,string keys){foreach(string key in keys.Split(' '))result[key]=color;}
    }
}
