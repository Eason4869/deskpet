using System;
using System.IO;
namespace Dafeiyu.Tests
{
    // Built deliberately without the language resource; never part of the shipped app.
    public static class LocalizationMissingResourceTests
    {
        public static int Main()
        {
            try
            {
                bool failed=false;try{TextResources.Get("common.cancel");}catch(InvalidDataException){failed=true;}
                if(!failed)throw new Exception("Missing catalog must fail initialization");
                if(TextResources.StartupMessage("common.cancel")!="界面文字资源不可用，请修复安装。")throw new Exception("Startup reporting cannot repair a broken catalog");
                Console.WriteLine("LocalizationMissingResourceTests: 2 passed");return 0;
            }
            catch(Exception error){Console.Error.WriteLine("FAIL LocalizationMissingResourceTests: "+error);return 1;}
        }
    }
}
