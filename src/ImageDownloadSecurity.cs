using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Dafeiyu
{
    internal static class ImageDownloadSecurity
    {
        internal static async Task<Uri> ValidateAddressAsync(string address,Uri service,CancellationToken token)
        {
            Uri uri;if(String.IsNullOrWhiteSpace(address) || address.Length>8192 || !Uri.TryCreate(address,UriKind.Absolute,out uri) || (uri.Scheme!="http" && uri.Scheme!="https") || uri.UserInfo.Length>0 || uri.Fragment.Length>0)throw new InvalidDataException("图片下载地址无效；仅支持直接 HTTP 或 HTTPS 图片地址。");
            token.ThrowIfCancellationRequested();
            // A deliberately configured local image server may return its own images.
            // This exception is exact origin only: no other host, port or scheme inherits it.
            if(service!=null && uri.Scheme==service.Scheme && uri.DnsSafeHost.Equals(service.DnsSafeHost,StringComparison.OrdinalIgnoreCase) && uri.Port==service.Port)return uri;
            IPAddress literal;if(IPAddress.TryParse(uri.DnsSafeHost,out literal)){if(!Public(literal))throw Unsafe();return uri;}
            if(uri.IsLoopback || uri.Host.IndexOf('.')<0 || uri.Host.EndsWith(".localhost",StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".local",StringComparison.OrdinalIgnoreCase))throw Unsafe();
            try{
                var resolve=Dns.GetHostAddressesAsync(uri.DnsSafeHost);var canceled=new TaskCompletionSource<bool>();
                using(token.Register(delegate{canceled.TrySetResult(true);})){if(await Task.WhenAny(resolve,canceled.Task).ConfigureAwait(false)!=resolve)token.ThrowIfCancellationRequested();var addresses=await resolve.ConfigureAwait(false);if(addresses.Length==0)throw Unsafe();foreach(var ip in addresses)if(!Public(ip))throw Unsafe();}
            }catch(SocketException){throw new InvalidDataException("图片下载地址无法解析。");}
            return uri;
        }
        static InvalidDataException Unsafe(){return new InvalidDataException("图片下载地址指向本机、内网或保留网络，已拒绝访问。");}
        internal static bool Public(IPAddress address)
        {
            if(address.IsIPv4MappedToIPv6)return Public(address.MapToIPv4());byte[] bytes=address.GetAddressBytes();
            if(address.AddressFamily==AddressFamily.InterNetwork){int a=bytes[0],b=bytes[1],c=bytes[2];return !(a==0 || a==10 || a==127 || a>=224 || (a==100 && b>=64 && b<=127) || (a==169 && b==254) || (a==172 && b>=16 && b<=31) || (a==192 && b==168) || (a==192 && b==0) || (a==198 && (b==18 || b==19)) || (a==198 && b==51 && c==100) || (a==203 && b==0 && c==113));}
            if(address.AddressFamily!=AddressFamily.InterNetworkV6)return false;
            // Restrict downloads to global unicast; mapped IPv4 is checked above.
            if((bytes[0]&224)!=32 || (bytes[0]==32 && bytes[1]==2) || (bytes[0]==32 && bytes[1]==1 && bytes[2]==13 && bytes[3]==184))return false;
            if(bytes[0]==32 && bytes[1]==1 && bytes[2]<2)return false;return true;
        }
    }
}
