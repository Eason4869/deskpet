using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace Dafeiyu
{
    // Incremental decoding keeps UTF-8 characters intact across arbitrary HTTP chunks.
    internal sealed class ChatStream
    {
        private const int MaxEventCharacters = 128 * 1024;
        private const int MaxReplyCharacters = 50000;
        private readonly bool anthropic;
        private readonly Action<string> onDelta;
        private readonly Action<string> onReasoning;
        private int reasoningCharacters;
        private readonly StringBuilder line = new StringBuilder(), data = new StringBuilder(), reply = new StringBuilder();
        private string eventName = "";
        private int eventCharacters;
        private bool afterCr, completed;
        private ChatStream(bool anthropic, Action<string> onDelta, Action<string> onReasoning) { this.anthropic = anthropic; this.onDelta = onDelta; this.onReasoning=onReasoning; }
        internal static async Task<string> ReadAsync(Stream stream, bool anthropic, Action<string> onDelta, CancellationToken token, int maximumBytes, Action<string> onReasoning=null)
        {
            var parser = new ChatStream(anthropic, onDelta, onReasoning);
            var encoding = new UTF8Encoding(false, true); Decoder decoder = encoding.GetDecoder();
            byte[] bytes = new byte[8192]; char[] chars = new char[encoding.GetMaxCharCount(bytes.Length)]; int total = 0;
            bool? json = null; var prefix = new StringBuilder(); var jsonText = new StringBuilder();
            try
            {
                while(true)
                {
                    token.ThrowIfCancellationRequested();
                    int read = await stream.ReadAsync(bytes, 0, bytes.Length, token).ConfigureAwait(false);
                    if(total > maximumBytes - read) throw new ApiException("服务返回的内容过大，请缩短本次请求。");
                    total += read;
                    int count = decoder.GetChars(bytes, 0, read, chars, 0, read == 0);
                    for(int i = 0; i < count; i++)
                    {
                        token.ThrowIfCancellationRequested(); char character = chars[i];
                        if(!json.HasValue)
                        {
                            if(character == '\uFEFF' && prefix.Length == 0) continue;
                            prefix.Append(character);
                            if(prefix.Length > MaxEventCharacters) throw TooLarge();
                            if(Char.IsWhiteSpace(character)) continue;
                            json = character == '{' || character == '[';
                            if(json.Value) jsonText.Append(prefix);
                            else foreach(char initial in prefix.ToString()) parser.Character(initial);
                            prefix.Clear();
                        }
                        else if(json.Value) jsonText.Append(character);
                        else parser.Character(character);
                        if(parser.completed) { token.ThrowIfCancellationRequested(); return parser.Result(); }
                    }
                    if(read == 0) break;
                }
            }
            catch(DecoderFallbackException) { throw InvalidStream(); }
            token.ThrowIfCancellationRequested();
            if(json == true)
            {
                string result;
                try { result = ApiClient.ParseChatReply(encoding.GetBytes(jsonText.ToString()), anthropic, onReasoning); }
                catch(ApiException) { throw InvalidStream(); }
                if(onDelta != null) onDelta(result); token.ThrowIfCancellationRequested(); return result;
            }
            // A stream is committed only by its protocol terminal event, never arbitrary EOF.
            throw new ApiException("流式回复连接中断，回复尚未完成；可以重试，或在文字服务设置中关闭流式回复。");
        }
        private void Character(char value)
        {
            if(afterCr) { afterCr = false; if(value == '\n') return; }
            if(++eventCharacters > MaxEventCharacters) throw TooLarge();
            if(value == '\r' || value == '\n')
            { FinishLine(); afterCr = value == '\r'; }
            else line.Append(value);
        }
        private void FinishLine()
        {
            if(line.Length == 0)
            {
                Dispatch(); data.Clear(); eventName = ""; eventCharacters = 0; return;
            }
            string value = line.ToString(); line.Clear();
            if(value[0] == ':') return;
            int colon = value.IndexOf(':'); string field = colon < 0 ? value : value.Substring(0, colon);
            string content = colon < 0 ? "" : value.Substring(colon + 1); if(content.StartsWith(" ")) content = content.Substring(1);
            if(field == "data") { data.Append(content); data.Append('\n'); }
            else if(field == "event") eventName = content;
        }
        private void Dispatch()
        {
            if(eventName == "error") throw InvalidStream();
            if(data.Length == 0) return;
            if(!KnownEvent(eventName)) return;
            string value = data.ToString(0, data.Length - 1);
            if(!anthropic && value.Trim() == "[DONE]") { completed = true; return; }
            try
            {
                var json = new JavaScriptSerializer { MaxJsonLength = MaxEventCharacters }.DeserializeObject(value) as Dictionary<string,object>;
                if(json == null) throw new FormatException();
                UsageTracking.Observe(json,anthropic);
                if(json.ContainsKey("error")) throw InvalidStream();
                if(anthropic)
                {
                    object kind; json.TryGetValue("type", out kind);
                    if((kind as string) == "error") throw InvalidStream();
                    if((kind as string) == "message_stop") { completed = true; return; }
                    if((kind as string) == "content_block_start")
                    {
                        var block=(Dictionary<string,object>)json["content_block"];object initial;
                        if((block["type"] as string)=="thinking" && block.TryGetValue("thinking",out initial))Reasoning(initial as string);
                        else if((block["type"] as string)=="text" && block.TryGetValue("text",out initial))Append(initial as string);
                        return;
                    }
                    if((kind as string) != "content_block_delta") return;
                    var delta = (Dictionary<string,object>)json["delta"];
                    if((delta["type"] as string) == "text_delta") Append((string)delta["text"]);
                    else if((delta["type"] as string) == "thinking_delta") Reasoning((string)delta["thinking"]);
                }
                else
                {
                    object choices; if(!json.TryGetValue("choices", out choices)) return;
                    var items = (object[])choices; if(items.Length == 0) return;
                    var choice = (Dictionary<string,object>)items[0]; object deltaValue;
                    if(!choice.TryGetValue("delta", out deltaValue)) return;
                    var delta = (Dictionary<string,object>)deltaValue; object text;
                    if(delta.TryGetValue("reasoning_content",out text) && text is string)Reasoning((string)text);
                    else if(delta.TryGetValue("reasoning",out text) && text is string)Reasoning((string)text);
                    if(delta.TryGetValue("content", out text) && text != null) Append((string)text);
                }
            }
            catch(Exception e)
            {
                if(!(e is ArgumentException || e is InvalidCastException || e is KeyNotFoundException || e is FormatException || e is IndexOutOfRangeException || e is NullReferenceException)) throw;
                throw InvalidStream();
            }
        }
        private bool KnownEvent(string name)
        {
            if(name == "" || name == "message") return true;
            return anthropic && (name == "message_start" || name == "message_delta" || name == "message_stop" || name == "content_block_start" || name == "content_block_delta" || name == "content_block_stop" || name == "ping");
        }
        private void Append(string text)
        {
            if(String.IsNullOrEmpty(text)) return;
            if(reply.Length > MaxReplyCharacters - text.Length) throw new ApiException("模型回复过长，请缩短本次请求。");
            reply.Append(text); if(onDelta != null) onDelta(text);
        }
        private void Reasoning(string text)
        {
            if(String.IsNullOrEmpty(text))return;
            if(reasoningCharacters>MaxReplyCharacters-text.Length)throw new ApiException("模型思考内容过长，请缩短本次请求。");
            reasoningCharacters+=text.Length;if(onReasoning!=null)onReasoning(text);
        }
        private string Result() { if(String.IsNullOrWhiteSpace(reply.ToString())) throw InvalidStream(); return reply.ToString().Trim(); }
        private static ApiException TooLarge() { return new ApiException("流式回复单条事件过大，请缩短本次请求。"); }
        private static ApiException InvalidStream() { return new ApiException("服务返回了不兼容或失败的流式回复，请检查模型与接口，或在文字服务设置中关闭流式回复后重试。"); }
    }
}
