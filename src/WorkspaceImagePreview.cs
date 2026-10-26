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
            if(asset==null)throw new FileNotFoundException(TextResources.Get("workspaceImagePreview.workspaceImagePreview.text"));
            Name="WorkspaceImagePreview";Tag=asset.Id;
            var image=new Image{Name="WorkspaceGeneratedImage",Stretch=Stretch.Uniform,Cursor=System.Windows.Input.Cursors.Hand};
            int width,height;bool limited;image.Source=ChatImagePreview.DecodeGeneratedImage(pet.ImageStore.Read(asset.Id),1600,out width,out height,out limited);Children.Add(image);
            Action preview=()=>ChatImagePreview.OpenGenerated(Window.GetWindow(this)??host,pet.ImageStore,asset,true,prepare);image.MouseLeftButtonUp+=delegate{preview();};
            var menu=ChatUi.Menu();var details=LocalizedUi.With(new MenuItem{Header=TextResources.Get("workspaceImagePreview.workspaceImagePreview.header"),Name="WorkspacePreviewDetails"},"Header",()=>(TextResources.Get("workspaceImagePreview.workspaceImagePreview.header")));details.Click+=delegate{preview();};menu.Items.Add(details);
            var save=LocalizedUi.With(new MenuItem{Header=TextResources.Get("workspaceImagePreview.workspaceImagePreview.header.alternativeA"),Name="WorkspaceSaveOriginal"},"Header",()=>(TextResources.Get("workspaceImagePreview.workspaceImagePreview.header.alternativeA")));save.Click+=delegate{Run(()=>GeneratedImageCard.SaveOriginal(Window.GetWindow(this)??host,pet.ImageStore,asset));};menu.Items.Add(save);
            var copy=LocalizedUi.With(new MenuItem{Header=TextResources.Get("workspaceImagePreview.workspaceImagePreview.header.alternativeB"),Name="WorkspaceCopyImage"},"Header",()=>(TextResources.Get("workspaceImagePreview.workspaceImagePreview.header.alternativeB")));copy.Click+=delegate{Run(()=>GeneratedImageCard.CopyOriginal(pet.ImageStore,asset));};menu.Items.Add(copy);
            var again=LocalizedUi.With(new MenuItem{Header=TextResources.Get("workspaceImagePreview.workspaceImagePreview.header.alternativeC"),Name="WorkspacePrepareAgain"},"Header",()=>(TextResources.Get("workspaceImagePreview.workspaceImagePreview.header.alternativeC")));again.Click+=delegate{prepare(asset.Copy());};menu.Items.Add(again);ContextMenu=menu;
            var action=Ui.Button("•••");action.Name="WorkspacePreviewActions";action.Width=36;action.MinHeight=30;action.Padding=new Thickness(0);action.Margin=new Thickness(4);action.HorizontalAlignment=HorizontalAlignment.Right;action.VerticalAlignment=VerticalAlignment.Top;LocalizedUi.Set(action,"ToolTip",()=>(TextResources.Get("workspaceImagePreview.workspaceImagePreview.actionToolTip")));LocalizedUi.Automation(action,()=>(TextResources.Get("workspaceImagePreview.workspaceImagePreview.automationAutomationPropertiesSetName")));action.Click+=delegate{menu.PlacementTarget=action;menu.Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom;menu.IsOpen=true;};Children.Add(action);
            var open=LocalizedUi.Button(()=>(TextResources.Get("workspaceImagePreview.workspaceImagePreview.text.alternativeA")));open.Name="WorkspaceOpenPreview";open.HorizontalAlignment=HorizontalAlignment.Center;open.VerticalAlignment=VerticalAlignment.Bottom;open.Margin=new Thickness(0,0,0,6);open.Click+=delegate{preview();};Children.Add(open);
        }
        static void Run(Action action){try{action();}catch(Exception error){if(!(error is IOException||error is UnauthorizedAccessException||error is ArgumentException||error is NotSupportedException||error is FormatException||error is System.Runtime.InteropServices.COMException||error is System.ComponentModel.Win32Exception))throw;MessageBox.Show(TextResources.Get("workspaceImagePreview.run.messageBoxShow"),TextResources.Get("workspaceImagePreview.run.messageBoxShow.alternativeA"));}}
    }
}
