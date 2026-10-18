using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Dafeiyu {
 internal sealed class UsageSummary {
  internal long KnownTokens,InputTokens,OutputTokens;
  internal int Requests,UnknownRequests,EstimatedRequests,UnknownInputRequests,UnknownOutputRequests,AuxiliaryRequests;
  internal DateTime Date;
 }
 internal sealed class UsageConversation {
  internal string Id;
  internal long KnownTokens;
  internal int AuxiliaryRequests;
  internal IList<UsageRecord> Records;
 }
 internal sealed class UsageHeatDetail {internal string Title,Value;}
 // The page aggregates request records once. Cached input and reasoning are subsets,
 // never additional charges, and one known field cannot fill another unknown field.
 internal static class UsageStatisticsModel {
  internal static long? EffectiveTotal(UsageRecord row){if(row.TotalTokens.HasValue)return row.TotalTokens;if(row.InputTokens.HasValue&&row.OutputTokens.HasValue)return Add(row.InputTokens.Value,row.OutputTokens.Value);return null;}
  internal static long Add(long left,long right){return right>0&&left>long.MaxValue-right?long.MaxValue:left+right;}
  internal static UsageSummary Summarize(IEnumerable<UsageRecord> rows){var result=new UsageSummary();foreach(var row in rows){result.Requests++;var total=EffectiveTotal(row);if(total.HasValue)result.KnownTokens=Add(result.KnownTokens,total.Value);else result.UnknownRequests++;if(row.InputTokens.HasValue)result.InputTokens=Add(result.InputTokens,row.InputTokens.Value);else result.UnknownInputRequests++;if(row.OutputTokens.HasValue)result.OutputTokens=Add(result.OutputTokens,row.OutputTokens.Value);else result.UnknownOutputRequests++;if(row.IsEstimated)result.EstimatedRequests++;if(IsAuxiliary(row.Feature))result.AuxiliaryRequests++;}return result;}
  internal static bool IsAuxiliary(string feature){return feature=="subagent"||feature=="review"||feature=="prediction"||feature=="title"||feature=="compaction"||feature=="greeting";}
  internal static DateTime LocalDay(UsageRecord row){return row.TimestampUtc.ToLocalTime().Date;}
  internal static List<UsageRecord> Filter(IEnumerable<UsageRecord> rows,string provider,string model,string project,string feature){return rows.Where(r=>(provider==null||(r.Provider??"")==provider)&&(model==null||(r.Model??"")==model)&&(project==null||(r.ProjectId??"")==project)&&(feature==null||(r.Feature??"")==feature)).ToList();}
  internal static UsageSummary Day(IEnumerable<UsageRecord> rows,DateTime date){var result=Summarize(rows.Where(r=>LocalDay(r)==date.Date));result.Date=date.Date;return result;}
  internal static UsageSummary Between(IEnumerable<UsageRecord> rows,DateTime start,DateTime end){return Summarize(rows.Where(r=>LocalDay(r)>=start.Date&&LocalDay(r)<=end.Date));}
  internal static List<UsageSummary> Calendar(IEnumerable<UsageRecord> rows,DateTime first,DateTime last,int mode){
   var buckets=rows.Where(r=>LocalDay(r)>=first&&LocalDay(r)<=last).GroupBy(r=>mode==1?LocalDay(r).AddDays(-(((int)LocalDay(r).DayOfWeek+6)%7)):LocalDay(r)).ToDictionary(g=>g.Key,g=>Summarize(g));var result=new List<UsageSummary>();var cumulative=new UsageSummary();DateTime cursor=mode==1?first.AddDays(-(((int)first.DayOfWeek+6)%7)):first;
   for(;cursor<=last;cursor=cursor.AddDays(mode==1?7:1)){UsageSummary day;if(!buckets.TryGetValue(cursor,out day))day=new UsageSummary();if(mode==2){cumulative.KnownTokens=Add(cumulative.KnownTokens,day.KnownTokens);cumulative.InputTokens=Add(cumulative.InputTokens,day.InputTokens);cumulative.OutputTokens=Add(cumulative.OutputTokens,day.OutputTokens);cumulative.Requests+=day.Requests;cumulative.UnknownRequests+=day.UnknownRequests;cumulative.EstimatedRequests+=day.EstimatedRequests;cumulative.UnknownInputRequests+=day.UnknownInputRequests;cumulative.UnknownOutputRequests+=day.UnknownOutputRequests;day=new UsageSummary{KnownTokens=cumulative.KnownTokens,InputTokens=cumulative.InputTokens,OutputTokens=cumulative.OutputTokens,Requests=cumulative.Requests,UnknownRequests=cumulative.UnknownRequests,EstimatedRequests=cumulative.EstimatedRequests,UnknownInputRequests=cumulative.UnknownInputRequests,UnknownOutputRequests=cumulative.UnknownOutputRequests};}day.Date=cursor;result.Add(day);}return result;
  }
  internal static List<UsageConversation> Conversations(IEnumerable<UsageRecord> rows){return rows.GroupBy(r=>r.ConversationId??"").Select(g=>{var list=g.ToList();var summary=Summarize(list);return new UsageConversation{Id=g.Key,Records=list,KnownTokens=summary.KnownTokens,AuxiliaryRequests=summary.AuxiliaryRequests};}).OrderByDescending(g=>g.KnownTokens).ThenBy(g=>g.Id,StringComparer.Ordinal).ToList();}
  internal static string Number(long value){return value.ToString("N0",CultureInfo.InvariantCulture);}
  internal static string Total(UsageSummary value){return value.Requests==0?"—":value.UnknownRequests==value.Requests?"未知":Number(value.KnownTokens)+(value.UnknownRequests>0?" + 未知":"")+(value.EstimatedRequests>0?"（含估算）":"");}
  internal static string Describe(UsageSummary value){string date=value.Date.ToString("yyyy-MM-dd");if(value.Requests==0)return date+"\n未采集到请求，不能据此确认零用量";return date+"\n"+Total(value)+" Token\n"+value.Requests+" 次请求"+(value.UnknownRequests>0?"，"+value.UnknownRequests+" 次总量未知":"")+"\n输入 "+Number(value.InputTokens)+(value.UnknownInputRequests>0?" + 未知":"")+" / 输出 "+Number(value.OutputTokens)+(value.UnknownOutputRequests>0?" + 未知":"");}
  internal static UsageHeatDetail HeatDetail(UsageSummary summary,int mode,DateTime first,DateTime last){string prefix=mode==1?"周合计":mode==2?"累计":"当日";string title=summary.Date.ToString("yyyy年M月d日");if(mode==1){DateTime start=summary.Date<first?first:summary.Date;DateTime end=summary.Date.AddDays(6)>last?last:summary.Date.AddDays(6);title=start.ToString("yyyy年M月d日")+" 至 "+end.ToString("yyyy年M月d日");}else if(mode==2)title="累计至 "+title;
   string value=summary.Requests==0?prefix+"未采集":summary.UnknownRequests==summary.Requests?prefix+"用量未知":prefix+" "+Number(summary.KnownTokens)+" Token"+(summary.UnknownRequests>0?" + 未知":"");if(summary.EstimatedRequests>0)value+="（含估算）";if(mode==2)value="自 "+first.ToString("yyyy.MM.dd")+" 起，"+value;return new UsageHeatDetail{Title=title,Value=value};
  }
  internal static string Feature(string value){switch(value){case "chat":return "个人聊天";case "work":return "工作";case "subagent":return "子智能体";case "review":return "自动审核";case "prediction":return "预测消息";case "title":return "标题生成";case "compaction":return "上下文压缩";case "greeting":return "AI 问候";case "transcription":return "语音识别";case "speech":return "朗读";case "realtime":return "实时语音";default:return string.IsNullOrEmpty(value)?"未归类":value;}}
 }
 internal static class UsageVisuals {
  internal static readonly Brush Background=Brushes.White,Ink=Ui.PageInk,Muted=Brush("#7A7E86"),Line=Brush("#E9EAED"),Blue=Brush("#357FF5");
  internal static readonly string[] Colors={"#357FF5","#79A5F0","#44A6A1","#B38ACB","#E6AA62","#6B809E","#CE8493","#93AB73"};
  internal static Brush Brush(string color){var brush=(SolidColorBrush)new BrushConverter().ConvertFromString(color);brush.Freeze();return brush;}
  internal static Brush GroupColor(string key){var features=new[]{"chat","work","subagent","review","prediction","title","compaction","greeting"};int index=Array.IndexOf(features,key);if(index>=0)return Brush(Colors[index]);uint hash=2166136261;foreach(char c in key??"")hash=(hash^c)*16777619;return Brush(Colors[hash%Colors.Length]);}
  internal static TextBlock Text(string text,double size,Brush color){return new TextBlock{Text=text,FontSize=size,FontWeight=size>=16?FontWeights.SemiBold:FontWeights.Normal,Foreground=color,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};}
  internal static ToolTip HeatTooltip(UsageHeatDetail detail){var lines=new StackPanel();var title=Text(detail.Title,11,Brush("#E4E7EC"));title.Name="UsageHeatTooltipTitle";title.TextWrapping=TextWrapping.NoWrap;var value=Text(detail.Value,13,Brushes.White);value.Name="UsageHeatTooltipValue";value.TextWrapping=TextWrapping.NoWrap;value.Margin=new Thickness(0,5,0,0);lines.Children.Add(title);lines.Children.Add(value);var surface=new Border{Name="UsageHeatTooltipSurface",CornerRadius=new CornerRadius(8),Background=Brush("#24262B"),Padding=new Thickness(12,9,12,9),Child=lines};var presenter=new FrameworkElementFactory(typeof(ContentPresenter));var template=new ControlTemplate(typeof(ToolTip)){VisualTree=presenter};return new ToolTip{Content=surface,Template=template,OverridesDefaultStyle=true,Padding=new Thickness(0),BorderThickness=new Thickness(0),Background=Brushes.Transparent,HasDropShadow=false,Placement=System.Windows.Controls.Primitives.PlacementMode.Top,VerticalOffset=-7,FontFamily=new FontFamily("Microsoft YaHei UI")};}
  internal static Button Button(string text,string name,Action action){var button=new Button{Name=name,Content=text,Padding=new Thickness(13,7,13,7),Margin=new Thickness(0,0,6,0),Background=Brushes.White,Foreground=Ink,BorderBrush=Line,BorderThickness=new Thickness(1),Cursor=Cursors.Hand,MinHeight=32,Template=ButtonTemplate()};button.Click+=delegate{action();};return button;}
  static ControlTemplate ButtonTemplate(){var border=new FrameworkElementFactory(typeof(Border));border.Name="Surface";border.SetValue(Border.CornerRadiusProperty,new CornerRadius(6));border.SetValue(Border.BackgroundProperty,new TemplateBindingExtension(Control.BackgroundProperty));border.SetValue(Border.BorderBrushProperty,new TemplateBindingExtension(Control.BorderBrushProperty));border.SetValue(Border.BorderThicknessProperty,new TemplateBindingExtension(Control.BorderThicknessProperty));border.SetValue(Border.PaddingProperty,new TemplateBindingExtension(Control.PaddingProperty));var content=new FrameworkElementFactory(typeof(ContentPresenter));content.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);content.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);border.AppendChild(content);var template=new ControlTemplate(typeof(Button)){VisualTree=border};var hover=new Trigger{Property=UIElement.IsMouseOverProperty,Value=true};hover.Setters.Add(new Setter(Border.BackgroundProperty,Brush("#EFF3FA"),"Surface"));template.Triggers.Add(hover);var disabled=new Trigger{Property=UIElement.IsEnabledProperty,Value=false};disabled.Setters.Add(new Setter(UIElement.OpacityProperty,.4));template.Triggers.Add(disabled);return template;}
 }
 internal sealed class UsageBarChart:FrameworkElement {
  readonly DateTime start,end;
  readonly string group;
  readonly IList<string> keys;
  readonly Dictionary<DateTime,Dictionary<string,UsageSummary>> buckets;
  readonly Dictionary<string,Brush> colors;
  readonly List<UsageSummary> days;
  internal UsageBarChart(IList<UsageRecord> rows,DateTime first,DateTime last,string grouping,IList<string> legend){start=first;end=last;group=grouping;keys=legend;buckets=rows.GroupBy(UsageStatisticsModel.LocalDay).ToDictionary(g=>g.Key,g=>g.GroupBy(r=>Key(r,group)).ToDictionary(p=>p.Key,p=>UsageStatisticsModel.Summarize(p)));colors=keys.ToDictionary(k=>k,k=>UsageVisuals.GroupColor(k));days=UsageStatisticsModel.Calendar(rows,start,end,0);MinHeight=215;Height=215;ClipToBounds=true;MouseMove+=Move;MouseLeave+=delegate{ToolTip=null;};}
  internal static string Key(UsageRecord row,string group){string key=group=="model"?row.Model:group=="provider"?row.Provider:row.Feature;return string.IsNullOrEmpty(key)?"未记录":key;}
  internal static IList<int> TickIndices(int count,double width){int capacity=Math.Max(2,(int)(width/65));int stride=Math.Max(1,(int)Math.Ceiling((count-1)/(double)(capacity-1)));var result=new List<int>();for(int i=0;i<count-1;i+=stride)if(count-1-i>=Math.Max(1,stride/2))result.Add(i);result.Add(count-1);return result;}
  UsageSummary Bucket(DateTime date,string key){Dictionary<string,UsageSummary> daily;UsageSummary result;return buckets.TryGetValue(date,out daily)&&daily.TryGetValue(key,out result)?result:null;}
  void Move(object sender,MouseEventArgs e){double width=Math.Max(1,ActualWidth-52);int count=(end-start).Days+1;int index=Math.Max(0,Math.Min(count-1,(int)((e.GetPosition(this).X-48)/width*count)));DateTime day=start.AddDays(index);string text=UsageStatisticsModel.Describe(days[index]);foreach(var key in keys){var part=Bucket(day,key);if(part!=null)text+="\n"+(group=="feature"?UsageStatisticsModel.Feature(key):key)+"："+UsageStatisticsModel.Total(part);}ToolTip=text;}
  void Label(DrawingContext drawing,string text,double x,double y,Brush color,double size){var formatted=new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),size,color,VisualTreeHelper.GetDpi(this).PixelsPerDip);drawing.DrawText(formatted,new Point(x,y));}
  protected override void OnRender(DrawingContext drawing){base.OnRender(drawing);double width=Math.Max(1,ActualWidth-52),height=166;int count=(end-start).Days+1;long maximum=Math.Max(1,days.Max(d=>d.KnownTokens));var ticks=TickIndices(count,width);
   for(int i=0;i<=3;i++){double y=12+height*i/3;drawing.DrawLine(new Pen(UsageVisuals.Line,1),new Point(48,y),new Point(ActualWidth,y));Label(drawing,Compact(maximum*(3-i)/3),0,y-7,UsageVisuals.Muted,10);}
   for(int i=0;i<count;i++){DateTime date=start.AddDays(i);double slot=width/count,x=48+i*slot+slot*.22,bottom=12+height;foreach(string key in keys){var part=Bucket(date,key);if(part==null)continue;double barHeight=height*part.KnownTokens/maximum;if(barHeight>0){drawing.DrawRoundedRectangle(colors[key],null,new Rect(x,bottom-barHeight,Math.Max(1,slot*.56),barHeight),1.5,1.5);bottom-=barHeight;}}
    if(days[i].UnknownRequests>0)drawing.DrawEllipse(Brushes.White,new Pen(UsageVisuals.Muted,1),new Point(x+slot*.28,12+height-3),2.5,2.5);
    if(ticks.Contains(i))Label(drawing,date.ToString("M/d"),Math.Min(Math.Max(48,x-3),Math.Max(48,ActualWidth-32)),188,UsageVisuals.Muted,10);
   }
  }
  static string Compact(long value){return value>=1000000?(value/1000000d).ToString("0.#")+"M":value>=1000?(value/1000d).ToString("0.#")+"k":value.ToString();}
 }
}
