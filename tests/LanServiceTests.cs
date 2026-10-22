using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Dafeiyu.Tests
{
    public static class LanServiceTests
    {
        static int checks;
        static void Check(bool ok,string message){if(!ok)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
        static ConnectionSettings Config(string address,string key=""){return new ConnectionSettings{BaseUrl=address,ApiKey=key,Model="relay-model",Protocol="openai"};}
        static bool Reject(ConnectionSettings settings){try{ApiClient.ValidateConnection(settings,"chat/completions");return false;}catch(ApiException){return true;}}
        public static int Main(string[] args)
        {
            try{
                foreach(string address in new[]{"http://192.168.1.50:3000/relay/v1","http://10.20.30.40:8000/v1","http://172.16.0.1:8080","http://172.31.255.254/v1","http://169.254.10.20:8080/v1","http://100.116.102.27:3000/v1","http://[fd12:3456::20]:3000/v1","http://[fe80::20]:8000/v1","http://[::ffff:192.168.1.50]:3000/v1","http://127.0.0.1:3000/v1","http://localhost:8000/v1"}){
                    var cfg=Config(address);ApiClient.ValidateConnection(cfg,ApiClient.ChatPath(cfg));Check(ApiClient.Endpoint(cfg,"chat/completions").AbsoluteUri==new Uri(new Uri(address).AbsoluteUri.TrimEnd('/')+"/chat/completions").AbsoluteUri,"HTTP private relay supports blank key and preserves prefix: "+address);
                    cfg.ApiKey="relay-test-key";ApiClient.ValidateConnection(cfg,"models");Check(ApiClient.Endpoint(cfg,"models").AbsoluteUri.EndsWith("/models"),"authenticated LAN model catalog validates");
                }
                foreach(string address in new[]{"http://192.167.255.1/v1","http://192.169.0.1/v1","http://172.15.255.255/v1","http://172.32.0.1/v1","http://11.0.0.1/v1","http://100.63.255.255/v1","http://100.128.0.1/v1","http://169.255.1.1/v1","http://8.8.8.8/v1","http://relay.example/v1","http://[2001:4860::8888]/v1","http://[::ffff:8.8.8.8]/v1","ftp://192.168.1.50/v1","http://user:secret@192.168.1.50/v1","http://192.168.1.50/v1?key=secret","http://192.168.1.50/v1#fragment"})Check(Reject(Config(address,"relay-test-key")),"unrelated public plaintext or credential URLs remain rejected: "+address);
                Check(Reject(Config("https://api.example/v1")),"public HTTPS services still require their API key");
                Check(Reject(Config("http://192.168.1.50/v1","bad\r\nkey")),"LAN keys cannot inject headers");
                var full=Config("http://192.168.1.50:3000/prefix/v1/chat/completions");Check(ApiClient.Endpoint(full,"chat/completions").AbsolutePath=="/prefix/v1/chat/completions","full LAN chat endpoint is not duplicated");
                if(args.Contains("--live-lan"))Live();
                Console.WriteLine("LAN service tests: "+checks+" passed");return 0;
            }catch(Exception error){Console.Error.WriteLine(error);return 1;}
        }
        static void Live()
        {
            var address=NetworkInterface.GetAllNetworkInterfaces().Where(n=>n.OperationalStatus==OperationalStatus.Up).SelectMany(n=>n.GetIPProperties().UnicastAddresses).Select(a=>a.Address).FirstOrDefault(a=>a.AddressFamily==AddressFamily.InterNetwork&&!IPAddress.IsLoopback(a)&&IsPrivate(a));
            if(address==null)throw new Exception("No active RFC1918 IPv4 interface available for the direct LAN smoke test.");
            Console.WriteLine("LAN fixture bound to "+address);
            using(var server=new ApiTests.Fixture("{\"data\":[{\"id\":\"relay-model\"}]}",200,address))using(var api=new ApiClient()){
                var models=api.ListModelsAsync(Config(server.Url+"/relay/v1"),CancellationToken.None).GetAwaiter().GetResult();server.Done.GetAwaiter().GetResult();Check(models.Single()=="relay-model"&&server.Path=="/relay/v1/models"&&server.Authorization==null,"model discovery reaches a real private-IP listener without a key");}
            foreach(string protocol in new[]{"openai","anthropic"})using(var server=new ApiTests.Fixture(protocol=="openai"?"{\"choices\":[{\"message\":{\"content\":\"LAN relay reply\"}}]}":"{\"content\":[{\"type\":\"text\",\"text\":\"LAN relay reply\"}]}",200,address))using(var api=new ApiClient()){
                var cfg=Config(server.Url+"/relay/v1","relay-test-key");cfg.Protocol=protocol;string reply=api.ChatAsync(cfg,new List<ChatMessage>{new ChatMessage("user","LAN fixture prompt")},CancellationToken.None).GetAwaiter().GetResult();server.Done.GetAwaiter().GetResult();Check(reply=="LAN relay reply"&&server.Path==(protocol=="openai"?"/relay/v1/chat/completions":"/relay/v1/messages"),protocol+" chat reaches the private-IP relay with the expected prefix");Check(protocol=="openai"?server.Authorization=="Bearer relay-test-key":server.Headers.Contains("x-api-key: relay-test-key"),protocol+" LAN authentication is preserved");}
            using(var server=new ApiTests.Fixture(Encoding.UTF8.GetBytes("data: {\"choices\":[{\"delta\":{\"content\":\"LAN stream\"}}]}\n\ndata: [DONE]\n\n"),"text/event-stream",200,address))using(var api=new ApiClient()){
                string delta="";string reply=api.ChatStreamAsync(Config(server.Url+"/relay/v1"),new List<ChatMessage>{new ChatMessage("user","LAN fixture prompt")},part=>delta+=part,CancellationToken.None).GetAwaiter().GetResult();server.Done.GetAwaiter().GetResult();Check(reply=="LAN stream"&&delta==reply&&server.Path=="/relay/v1/chat/completions","SSE streaming reaches the LAN relay and renders real deltas");}
            byte[] wav=WaveData.EncodePcm(new byte[16000]);
            using(var server=new ApiTests.Fixture("{\"text\":\"LAN transcript\"}",200,address))using(var api=new ApiClient()){
                string text=api.TranscribeAsync(Config(server.Url+"/relay/v1"),wav,CancellationToken.None).GetAwaiter().GetResult();server.Done.GetAwaiter().GetResult();Check(text=="LAN transcript"&&server.Path=="/relay/v1/audio/transcriptions","ASR reaches the LAN relay without a key");}
            using(var server=new ApiTests.Fixture(wav,"audio/wav",200,address))using(var api=new ApiClient()){
                byte[] speech=api.SpeechAsync(Config(server.Url+"/relay/v1"),"fixture-voice","LAN text",CancellationToken.None).GetAwaiter().GetResult();server.Done.GetAwaiter().GetResult();Check(speech.SequenceEqual(wav)&&server.Path=="/relay/v1/audio/speech","TTS receives valid audio from the LAN relay");}
        }
        static bool IsPrivate(IPAddress address){byte[] b=address.GetAddressBytes();return b[0]==10||b[0]==192&&b[1]==168||b[0]==172&&b[1]>=16&&b[1]<=31;}
    }
}
