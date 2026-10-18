using System;
using System.Windows.Forms;
namespace Dafeiyu.Tests
{
    public sealed class MenuBlankFixture : Form
    {
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000000;return p;}}
        protected override void WndProc(ref Message message){if(message.Msg==0x21){message.Result=new IntPtr(3);return;}base.WndProc(ref message);}
        [STAThread]public static void Main()
        {
            var form=new MenuBlankFixture{Text="DeskPet menu fixture",Location=new System.Drawing.Point(30,40),Size=new System.Drawing.Size(600,450),FormBorderStyle=FormBorderStyle.None,TopMost=true,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual};
            var timeout=new Timer{Interval=180000};timeout.Tick+=delegate{form.Close();};timeout.Start();Application.Run(form);timeout.Dispose();
        }
    }
}
