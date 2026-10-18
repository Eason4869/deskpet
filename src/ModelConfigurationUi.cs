using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Dafeiyu
{
    public sealed partial class TextProviderEditor
    {
        bool IsEmptyInitial(TextProviderSettings provider){return kind=="text"&&emptyInitialIds.Contains(provider.Id)&&String.IsNullOrWhiteSpace(provider.Connection.BaseUrl)&&String.IsNullOrWhiteSpace(provider.Connection.ApiKey)&&provider.Models.Count==0;}
        void UpdateEmptyProviders(){if(providerDetail==null)return;bool empty=kind=="text"&&drafts.All(IsEmptyInitial);providerDetail.Visibility=empty?Visibility.Collapsed:Visibility.Visible;emptyProviderHint.Visibility=empty?Visibility.Visible:Visibility.Collapsed;}
        UIElement DiscoveryModelRow(string id)
        {
            var row=new DockPanel();var add=UiIcons.Button("add","添加模型");add.Name="AddDiscoveredModel";add.Tag=id;add.Width=add.Height=30;add.MinHeight=30;add.Padding=new Thickness(5);add.Click+=delegate{OpenModelEditor(id);};DockPanel.SetDock(add,Dock.Right);row.Children.Add(add);var info=new StackPanel();info.Children.Add(Ui.Text(id,12));int capacity=selected.ContextCapacity(id);info.Children.Add(Ui.Text(capacity>0?capacity.ToString("N0")+" Token":"上下文容量未提供，可在添加时填写",10,Ui.Muted));row.Children.Add(info);return row;
        }
        void OpenModelEditor(string id)
        {
            Capture();var previous=selected.Copy();int index=drafts.IndexOf(selected);updating=true;newModel.Text=id??"";modelDisplayName.Text=id==null?"":selected.DisplayName(id)==id?"":selected.DisplayName(id);RefreshContextField();updating=false;
            var owner=Window.GetWindow(this);var dialog=new Window();modelDialog=dialog;Ui.Window(dialog,id!=null&&selected.Models.Contains(id)?"编辑模型":"添加模型",620,420);dialog.MinWidth=460;dialog.MinHeight=370;dialog.ResizeMode=ResizeMode.CanResize;dialog.ShowInTaskbar=false;if(owner!=null){dialog.Owner=owner;dialog.WindowStartupLocation=WindowStartupLocation.CenterOwner;}
            providerDetailHost.Children.Remove(modelForm);modelForm.Visibility=Visibility.Visible;var body=new Grid{Margin=new Thickness(24)};body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.RowDefinitions.Add(new RowDefinition());body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});var title=Ui.Text(id==null?"自定义模型":"配置 "+id,20,Ui.Brush("#25282D"));title.FontWeight=FontWeights.SemiBold;title.Margin=new Thickness(0,0,0,16);body.Children.Add(title);var scroll=Ui.Scroll(modelForm);Grid.SetRow(scroll,1);body.Children.Add(scroll);modelDialogNotice=Ui.Text("",11,Ui.Brush("#B34F4F"));modelDialogNotice.TextWrapping=TextWrapping.Wrap;modelForm.Children.Add(modelDialogNotice);var cancel=Ui.Button("取消");cancel.Name="CancelModelConfiguration";cancel.IsCancel=true;cancel.HorizontalAlignment=HorizontalAlignment.Right;cancel.Margin=new Thickness(0,12,0,0);cancel.Click+=delegate{dialog.DialogResult=false;dialog.Close();};var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};actions.Children.Add(cancel);var save=modelForm.Children.OfType<FrameworkElement>().SelectMany(e=>e is Grid?((Grid)e).Children.OfType<Button>():Enumerable.Empty<Button>()).First(b=>b.Name=="AddTextModel");var saveParent=(Grid)save.Parent;saveParent.Children.Remove(save);save.Content="保存";save.IsDefault=true;save.Background=Ui.Brush("#202329");save.Foreground=Brushes.White;save.BorderBrush=save.Background;save.Margin=new Thickness(10,12,0,0);actions.Children.Add(save);Grid.SetRow(actions,2);body.Children.Add(actions);DialogUi.Apply(dialog,body);
            bool accepted=false;try{accepted=dialog.ShowDialog()==true;}finally{actions.Children.Remove(save);saveParent.Children.Add(save);modelForm.Children.Remove(modelDialogNotice);modelDialogNotice=null;scroll.Content=null;modelForm.Visibility=Visibility.Collapsed;providerDetailHost.Children.Add(modelForm);modelDialog=null;if(!accepted){drafts[index]=previous;selected=previous;Show(previous);}else{RefreshModels();RefreshContextField();RaiseChanged();}}
        }
    }
}
