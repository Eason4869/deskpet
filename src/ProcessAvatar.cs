using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace Dafeiyu
{
    internal sealed partial class ChatMessageView
    {
        Border processAvatar;
        DropShadowEffect processAvatarGlow;
        TextBlock processRecordSummary;
        bool processWasPending,processMotionSubscribed;

        void InitializeProcessAvatar(ChatMessage message,StackPanel processDetails)
        {
            if(user)return;
            // Persisted message identity survives history reopening and changing
            // the active companion. Legacy records fall back to their session.
            var skin=pet.Catalog.Skins.FirstOrDefault(s=>s.Id==message.CompanionSkinId)
                ??pet.Catalog.Skins.FirstOrDefault(s=>s.Id==session.PersonaId)??pet.Skin;
            var face=new Image{Name="ProcessAvatarImage",Source=SkinCatalog.GetAvatar(skin),Width=20,Height=20,Stretch=Stretch.Uniform,Clip=new RectangleGeometry(new Rect(0,0,20,20),4,4)};
            RenderOptions.SetBitmapScalingMode(face,BitmapScalingMode.HighQuality);
            processAvatarGlow=new DropShadowEffect{Color=Color.FromRgb(0x70,0x92,0xC0),BlurRadius=6,ShadowDepth=0,Opacity=0};
            processAvatar=new Border{Name="ProcessAvatar",Width=20,Height=20,CornerRadius=new CornerRadius(4),Background=Ui.Brush("#F0F3F7"),Child=face,Effect=processAvatarGlow,ToolTip=skin.Name,Tag=skin.Id,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,7,0)};
            processRecordSummary=Ui.Text("",11,AgentUi.Muted);processRecordSummary.Name="ProcessRecordSummary";processRecordSummary.Margin=new Thickness(27,2,0,5);processRecordSummary.Visibility=Visibility.Collapsed;processDetails.Children.Add(processRecordSummary);
        }

        void PositionProcessRecord(bool pending)
        {
            if(user)return;var panel=(StackPanel)Child;
            int index=panel.Children.IndexOf(thoughtArea);
            if(pending)
            {
                if(index>panel.Children.IndexOf(Body)){panel.Children.Remove(thoughtArea);panel.Children.Insert(panel.Children.IndexOf(Body),thoughtArea);}
            }
            else if(index!=panel.Children.Count-1){panel.Children.Remove(thoughtArea);panel.Children.Add(thoughtArea);}
            thoughtArea.Margin=pending?new Thickness(0):new Thickness(0,10,0,0);
        }

        void RefreshProcessRecordSummary(ChatMessage message,List<AgentToolEvent> events)
        {
            if(user)return;
            string duration=message.ProcessingStartedAt.HasValue?message.ProcessingLabel(DateTime.UtcNow):"";
            string tools=events.Count>0?events.Count+" 项工具记录":"";
            processRecordSummary.Text=duration+(duration.Length>0&&tools.Length>0?" · ":"")+tools;
            processRecordSummary.Visibility=message.Status!="pending"&&processRecordSummary.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
        }

        void LoadProcessMotion()
        {
            if(user)return;
            if(!processMotionSubscribed){SystemParameters.StaticPropertyChanged+=ProcessMotionChanged;processMotionSubscribed=true;}
            RefreshProcessMotion(SystemParameters.ClientAreaAnimation);
        }
        void UnloadProcessMotion()
        {
            if(processMotionSubscribed){SystemParameters.StaticPropertyChanged-=ProcessMotionChanged;processMotionSubscribed=false;}
            RefreshProcessMotion(false);
        }
        void ProcessMotionChanged(object sender,PropertyChangedEventArgs args)
        {
            if(args.PropertyName!="ClientAreaAnimation"&&!String.IsNullOrEmpty(args.PropertyName))return;
            if(!Dispatcher.CheckAccess()){Dispatcher.BeginInvoke(new Action(delegate{if(IsLoaded)RefreshProcessMotion(SystemParameters.ClientAreaAnimation);}));return;}
            RefreshProcessMotion(SystemParameters.ClientAreaAnimation);
        }
        void RefreshProcessMotion(bool animationsEnabled)
        {
            bool running=!user&&IsLoaded&&current!=null&&current.Status=="pending"&&animationsEnabled&&SystemParameters.ClientAreaAnimation;
            if(!running)
            {
                StopShimmer();
                if(processAvatarGlow!=null){processAvatarGlow.BeginAnimation(DropShadowEffect.OpacityProperty,null);processAvatarGlow.Opacity=0;}
                return;
            }
            StartShimmer();
            if(!processAvatarGlow.HasAnimatedProperties)
                processAvatarGlow.BeginAnimation(DropShadowEffect.OpacityProperty,new DoubleAnimation(.08,.28,TimeSpan.FromSeconds(2.6)){AutoReverse=true,RepeatBehavior=RepeatBehavior.Forever});
        }
    }
}
