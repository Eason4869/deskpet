using System;
using System.Reflection;
namespace Dafeiyu.Tests
{
    public static class ReleaseVersionTests
    {
        public static int Main()
        {
            try {
                var type=typeof(ReleaseInfo).Assembly.GetType("Dafeiyu.ReleaseVersion");
                if(type==null)throw new Exception("missing shared release version parser");
                var parse=type.GetMethod("Parse",BindingFlags.Static|BindingFlags.Public);
                int count=0;
                foreach(var pair in new[]{new[]{"0.9.1","0.9.2-dev"},new[]{"0.9.2-dev","0.9.3"},new[]{"0.9.4-dev.2","0.9.4-dev.10"},new[]{"0.9.4-dev.10","0.9.4"},new[]{"0.9.9","0.10.0-dev.1"},new[]{"0.9.4","0.9.4.1"}}) {
                    var left=parse.Invoke(null,new object[]{pair[0]});var right=parse.Invoke(null,new object[]{pair[1]});
                    if(((IComparable)left).CompareTo(right)>=0 || ((IComparable)right).CompareTo(left)<=0)throw new Exception("incorrect ordering: "+pair[0]+" < "+pair[1]);count++;
                }
                if(((IComparable)parse.Invoke(null,new object[]{"v0.9.4"})).CompareTo(parse.Invoke(null,new object[]{"0.9.4.0"}))!=0)throw new Exception("legacy zero revision differs");
                foreach(string value in new[]{"../0.9.4","0.9.4\n","0.9.4-dev.-1","0.9.4-dev.01","0.9.4-rc.1","65535.0.0","0.9.4-dev.2147483648","0.9","0.9.4+fake"}) {
                    try{parse.Invoke(null,new object[]{value});throw new Exception("accepted invalid version: "+value);}catch(TargetInvocationException e){if(!(e.InnerException is ArgumentException))throw;}count++;
                }
                if(ReleaseInfo.InstallerAssetName("0.9.5-dev.2")!="DeskPet-Windows-Setup-0.9.5-dev.2.exe" || !ReleaseInfo.WindowsUpdateAsset("DeskPet-Windows-0.9.5-dev.2.zip","0.9.5-dev.2",true))throw new Exception("preview asset rejected");
                Console.WriteLine("Release version tests: "+count+" passed");return 0;
            } catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}
        }
    }
}
