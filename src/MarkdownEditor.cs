using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Dafeiyu
{
    // One editable document, with Markdown as the persistence/transport format.
    internal sealed class MarkdownEditor : RichTextBox
    {
        string source="";bool updating,plain,composing,undoing,pasting;
        MarkdownEditorSource map;
        internal bool IsComposing {get{return composing;}}
        public int MaxLength {get;set;}
        public bool PlainTextMode {get{return plain;}set{if(plain==value)return;int caret=CaretIndex;plain=value;Render(source,caret);}}
        public string Text {get{return source;}set{value=value??"";if(source==value)return;source=value;Render(value,value.Length);base.OnTextChanged(new TextChangedEventArgs(TextChangedEvent,UndoAction.Create));}}
        public int CaretIndex {get{return map==null?0:CaretPosition.CompareTo(Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward))==0?source.Length:map.Offset(CaretPosition);}set{CaretPosition=Position(value);}}
        TextPointer Position(int offset){if(offset>=source.Length)return Document.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);return map==null?Document.ContentStart:map.Position(Math.Max(0,offset));}
        public void Select(int start,int length){Selection.Select(Position(start),Position(start+length));}
        public void Clear(){Text="";}
        public Rect GetRectFromCharacterIndex(int index){return Position(index).GetCharacterRect(LogicalDirection.Forward);}
        public MarkdownEditor()
        {
            BorderThickness=new Thickness(0);Background=Brushes.Transparent;Foreground=Ui.Ink;AcceptsTab=false;IsDocumentEnabled=true;
            HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;ContextMenu=ChatUi.EditorMenu(this,false);
            TextCompositionManager.AddPreviewTextInputStartHandler(this,delegate{composing=true;});
            TextCompositionManager.AddPreviewTextInputHandler(this,delegate{composing=false;});
            LostKeyboardFocus+=delegate{composing=false;};
            CommandManager.AddPreviewExecutedHandler(this,delegate(object sender,ExecutedRoutedEventArgs e){if(e.Command==ApplicationCommands.Undo || e.Command==ApplicationCommands.Redo){undoing=true;Dispatcher.BeginInvoke(new Action(delegate{undoing=false;}));}});
            CommandManager.AddPreviewExecutedHandler(this,delegate(object sender,ExecutedRoutedEventArgs e){if(e.Command==EditingCommands.EnterLineBreak || e.Command==EditingCommands.EnterParagraphBreak){if(ExitHeadingAtEnd()){e.Handled=true;return;}ExitInlineAtEnd();}});
            DataObject.AddPastingHandler(this,delegate{pasting=true;Dispatcher.BeginInvoke(new Action(delegate{pasting=false;}));});
            Render("",0);
        }
        void Render(string text,int caret)
        {
            updating=true;
            try
            {
                bool formatted;var doc=plain?new FlowDocument(new Paragraph(new Run(text)){Margin=new Thickness(0)}):MarkdownDocument.Create(text,FontFamily,Foreground,true,out formatted);
                doc.PagePadding=new Thickness(0);doc.FontFamily=FontFamily;doc.FontSize=14;doc.Foreground=Foreground;doc.ColumnWidth=Double.PositiveInfinity;
                Document=doc;map=new MarkdownEditorSource(doc,plain);CaretIndex=caret;
            }
            finally{updating=false;}
        }
        protected override void OnTextChanged(TextChangedEventArgs e)
        {
            if(updating)return;
            // The base constructor raises this before our document/source exists.
            if(source==null){base.OnTextChanged(e);return;}
            map=new MarkdownEditorSource(Document,plain);string next=map.Text;int caret=map.Offset(CaretPosition);
            if(MaxLength>0 && next.Length>MaxLength){Undo();return;}
            source=next;
            if(!plain && !composing && !undoing && e.UndoAction!=UndoAction.Undo && e.UndoAction!=UndoAction.Redo && (pasting || HasTypedMarkup(Document.Blocks)))
            {
                // Change blocks in the existing document so native undo retains the edit.
                bool formatted;var parsed=MarkdownDocument.Create(source,FontFamily,Foreground,true,out formatted);
                if(formatted)
                {
                    updating=true;BeginChange();
                    try{Document.Blocks.Clear();while(parsed.Blocks.FirstBlock!=null){var block=parsed.Blocks.FirstBlock;parsed.Blocks.Remove(block);Document.Blocks.Add(block);}map=new MarkdownEditorSource(Document,false);source=map.Text;CaretIndex=caret;}
                    finally{EndChange();updating=false;}
                }
            }
            base.OnTextChanged(e);
        }
        Inline EndingSpan()
        {
            if(plain || !Selection.IsEmpty)return null;
            var element=CaretPosition.Parent as FrameworkContentElement;Inline outer=element is Run && ((Run)element).Tag as string=="code"?(Inline)element:null;
            while(element!=null && !(element is Paragraph)){if(element is Span)outer=(Span)element;element=element.Parent as FrameworkContentElement;}
            return outer!=null && new TextRange(CaretPosition,outer.ContentEnd).Text.Length==0?outer:null;
        }
        bool ExitInlineAtEnd(string separator="")
        {
            var span=EndingSpan();if(span==null)return false;
            var parent=span.Parent as Paragraph;if(parent==null)return false;
            var next=new Run(separator){FontWeight=FontWeights.Normal,FontStyle=FontStyles.Normal,TextDecorations=null};parent.Inlines.InsertAfter(span,next);CaretPosition=next.ContentEnd;
            Selection.ApplyPropertyValue(TextElement.FontWeightProperty,FontWeights.Normal);
            Selection.ApplyPropertyValue(TextElement.FontStyleProperty,FontStyles.Normal);
            Selection.ApplyPropertyValue(Inline.TextDecorationsProperty,null);
            return true;
        }
        bool ExitHeadingAtEnd()
        {
            var paragraph=CaretPosition.Paragraph;if(plain||!Selection.IsEmpty||paragraph==null||!((paragraph.Tag as string)??"").StartsWith("heading:")||!(paragraph.Parent is FlowDocument)||new TextRange(CaretPosition,paragraph.ContentEnd).Text.Length!=0)return false;
            BeginChange();try{var next=new Paragraph(new Run("")){Margin=new Thickness(0),FontWeight=FontWeights.Normal,FontSize=14};Document.Blocks.InsertAfter(paragraph,next);CaretPosition=next.ContentStart;}finally{EndChange();}return true;
        }
        protected override void OnPreviewTextInput(TextCompositionEventArgs e)
        {
            if(e.Text==" " && ExitInlineAtEnd(" ")){e.Handled=true;return;}
            base.OnPreviewTextInput(e);
        }
        static bool HasTypedLink(InlineCollection inlines)
        {
            foreach(Inline inline in inlines){if(inline is Hyperlink){if((inline.Tag as string)=="autolink")return true;continue;}var run=inline as Run;var span=inline as Span;if(run!=null && Regex.IsMatch(run.Text,@"https?://[^\s]+\s"))return true;if(span!=null && HasTypedLink(span.Inlines))return true;}return false;
        }
        static bool HasTypedMarkup(BlockCollection blocks)
        {
            if(blocks.Count>0 && Regex.Matches(new TextRange(blocks.FirstBlock.ContentStart,blocks.LastBlock.ContentEnd).Text,"```").Count>=2)return true;
            foreach(Block block in blocks)
            {
                var p=block as Paragraph;var section=block as Section;
                if(p!=null && Regex.IsMatch(new TextRange(p.ContentStart,p.ContentEnd).Text,@"\*\*[^*]+\*\*|(?<!\*)\*[^*]+\*(?!\*)|_[^_]+_|`[^`]+`|^#{1,6} |^[-+] |^\d+\. |\[[^\]]+\]\([^)]+\)"))return true;
                if(p!=null && HasTypedLink(p.Inlines))return true;
                if(section!=null && !(section.Tag as string??"").StartsWith("code:") && HasTypedMarkup(section.Blocks))return true;
            }
            return false;
        }
    }

    // Maps source offsets to real WPF insertion positions; palettes and dictation
    // keep using Markdown without putting a hidden TextBox behind the editor.
    internal sealed class MarkdownEditorSource
    {
        sealed class Segment {internal TextPointer Start,End;internal int Offset,Length;}
        readonly StringBuilder text=new StringBuilder();readonly List<Segment> segments=new List<Segment>();readonly FlowDocument doc;
        internal string Text {get{return text.ToString();}}
        internal MarkdownEditorSource(FlowDocument document,bool plain){doc=document;if(plain){bool first=true;foreach(Block block in doc.Blocks){var p=block as Paragraph;if(p!=null){if(!first)text.Append('\n');first=false;Inlines(p.Inlines,false,false,true);}}}else Blocks(doc.Blocks,"");}
        internal int Offset(TextPointer pointer)
        {
            foreach(var part in segments){if(pointer.CompareTo(part.Start)<0)return part.Offset;if(pointer.CompareTo(part.End)<=0)return part.Offset+Math.Min(part.Length,Math.Max(0,part.Start.GetOffsetToPosition(pointer)));}return text.Length;
        }
        internal TextPointer Position(int offset)
        {
            foreach(var part in segments)if(offset<=part.Offset+part.Length)return part.Start.GetPositionAtOffset(Math.Max(0,offset-part.Offset),LogicalDirection.Forward)??part.End;
            return doc.ContentEnd.GetInsertionPosition(LogicalDirection.Backward);
        }
        void Blocks(BlockCollection blocks,string prefix)
        {
            bool first=true;foreach(Block block in blocks){if(!first)text.Append((block.Tag as string)=="draft-tail"?"\n":"\n\n");first=false;WriteBlock(block,prefix);}
        }
        void WriteBlock(Block block,string prefix)
        {
            var p=block as Paragraph;var list=block as System.Windows.Documents.List;var section=block as Section;var table=block as Table;
            if(p!=null){string tag=p.Tag as string??"";text.Append(prefix);if(tag.StartsWith("heading:"))text.Append(new string('#',Int32.Parse(tag.Substring(8)))+" ");if(tag=="rule")text.Append("---");else Inlines(p.Inlines,p.FontWeight.ToOpenTypeWeight()>=600,p.FontStyle==FontStyles.Italic,false);}
            else if(list!=null)
            {
                int index=list.StartIndex;bool firstItem=true;
                foreach(var item in list.ListItems)
                {
                    if(!firstItem)text.Append('\n');firstItem=false;
                    string marker=list.MarkerStyle==TextMarkerStyle.Decimal?(index++).ToString()+". ":"- ";bool firstBlock=true;
                    foreach(Block child in item.Blocks)
                    {
                        if(!firstBlock)text.Append(child is System.Windows.Documents.List?"\n":"\n\n");
                        WriteBlock(child,prefix+(firstBlock?marker:new string(' ',marker.Length)));firstBlock=false;
                    }
                }
            }
            else if(section!=null){string tag=section.Tag as string??"";if(tag.StartsWith("code:")){text.Append("```"+tag.Substring(5)+"\n");bool codeFirst=true;foreach(Block child in section.Blocks){var para=child as Paragraph;if(para!=null){if(!codeFirst)text.Append('\n');codeFirst=false;Inlines(para.Inlines,false,false,true);}}text.Append("\n```");}else Blocks(section.Blocks,prefix+(tag=="quote"?"> ":""));}
            else if(table!=null){bool header=true;foreach(var group in table.RowGroups)foreach(var row in group.Rows){text.Append("| ");foreach(var cell in row.Cells){Blocks(cell.Blocks,"");text.Append(" | ");}text.Append('\n');if(header){header=false;text.Append('|');foreach(var cell in row.Cells)text.Append(" --- |");text.Append('\n');}}}
        }
        void Inlines(InlineCollection inlines,bool bold,bool italic,bool literal)
        {
            foreach(Inline inline in inlines)
            {
                var run=inline as Run;var span=inline as Span;var link=inline as Hyperlink;
                if(inline is InlineUIContainer)continue;
                if(inline is LineBreak){text.Append('\n');continue;}
                bool b=!literal && inline.FontWeight.ToOpenTypeWeight()>=600;bool i=!literal && inline.FontStyle==FontStyles.Italic;bool code=!literal && (inline.Tag as string)=="code";
                bool strike=!literal && inline.TextDecorations!=null && inline.TextDecorations.Contains(TextDecorations.Strikethrough[0]);
                if(link!=null && link.NavigateUri!=null){if((link.Tag as string)=="autolink"){Inlines(link.Inlines,bold,italic,literal);continue;}text.Append((link.Tag as string)=="image"?"![":"[");Inlines(link.Inlines,bold,italic,literal);text.Append("]("+link.NavigateUri.AbsoluteUri+")");continue;}
                string marker=(b&&!bold?"**":"")+(i&&!italic?"*":"")+(strike?"~~":"")+(code?"`":"");text.Append(marker);
                if(run!=null){segments.Add(new Segment{Start=run.ContentStart,End=run.ContentEnd,Offset=text.Length,Length=run.Text.Length});text.Append(run.Text);}
                else if(span!=null)Inlines(span.Inlines,b,i,literal);
                for(int n=marker.Length-1;n>=0;n--)text.Append(marker[n]);
            }
        }
    }
}
