using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dafeiyu
{
    // Workspace results fill the preview surface; chat and grouped result cards keep their own layout.
    internal sealed class WorkspaceImagePreview:Grid
    {
        internal WorkspaceImagePreview(PetWindow pet,Window host,GeneratedImage asset,Action<GeneratedImage> prepare)
        {
            if(asset==null)throw new FileNotFoundException("图片文件缺失");
            Name="WorkspaceImagePreview";Tag=asset.Id;
            var image=new Image{Name="WorkspaceGeneratedImage",Stretch=Stretch.Uniform,Cursor=System.Windows.Input.Cursors.Hand};
            int width,height;bool limited;image.Source=ChatImagePreview.DecodeGeneratedImage(pet.ImageStore.Read(asset.Id),1600,out width,out height,out limited);Children.Add(image);
            Action preview=()=>ChatImagePreview.OpenGenerated(Window.GetWindow(this)??host,pet.ImageStore,asset,true,prepare);image.MouseLeftButtonUp+=delegate{preview();};
            var menu=ChatUi.Menu();var details=new MenuItem{Header="查看详情",Name="WorkspacePreviewDetails"};details.Click+=delegate{preview();};menu.Items.Add(details);
            var save=new MenuItem{Header="另存为原图…",Name="WorkspaceSaveOriginal"};save.Click+=delegate{Run(()=>GeneratedImageCard.SaveOriginal(Window.GetWindow(this)??host,pet.ImageStore,asset));};menu.Items.Add(save);
            var copy=new MenuItem{Header="复制图片",Name="WorkspaceCopyImage"};copy.Click+=delegate{Run(()=>GeneratedImageCard.CopyOriginal(pet.ImageStore,asset));};menu.Items.Add(copy);
            var again=new MenuItem{Header="再生成一张",Name="WorkspacePrepareAgain"};again.Click+=delegate{prepare(asset.Copy());};menu.Items.Add(again);ContextMenu=menu;
            var action=Ui.Button("•••");action.Name="WorkspacePreviewActions";action.Width=36;action.MinHeight=30;action.Padding=new Thickness(0);action.Margin=new Thickness(4);action.HorizontalAlignment=HorizontalAlignment.Right;action.VerticalAlignment=VerticalAlignment.Top;action.ToolTip="图片操作";System.Windows.Automation.AutomationProperties.SetName(action,"图片操作");action.Click+=delegate{menu.PlacementTarget=action;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom;menu.IsOpen=true;};Children.Add(action);
            var open=Ui.Button("查看图片");open.Name="WorkspaceOpenPreview";open.HorizontalAlignment=HorizontalAlignment.Center;open.VerticalAlignment=VerticalAlignment.Bottom;open.Margin=new Thickness(0,0,0,6);open.Click+=delegate{preview();};Children.Add(open);
        }
        static void Run(Action action){try{action();}catch(Exception error){if(!(error is IOException||error is UnauthorizedAccessException||error is ArgumentException||error is NotSupportedException||error is FormatException||error is System.Runtime.InteropServices.COMException||error is System.ComponentModel.Win32Exception))throw;MessageBox.Show("操作未完成，请检查原图、保存位置或剪贴板后重试。","图片操作");}}
    }
}
