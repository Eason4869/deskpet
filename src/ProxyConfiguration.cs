using System;
using System.Net;
using System.Net.Http;
using System.Threading;
namespace Dafeiyu
{
    public static class ProxyConfiguration
    {
        public const string DefaultAddress="http://127.0.0.1:7890";
        public static string Normalize(string address){return String.IsNullOrWhiteSpace(address)?DefaultAddress:address.Trim();}
        public static string DisplayAddress(string address){string value=Normalize(address);return value.Contains("://")?value:"http://"+value;}
        public static Uri Endpoint(string address)
        {
            string value=Normalize(address);if(!value.Contains("://"))value="http://"+value;
            Uri uri;
            if(!Uri.TryCreate(value,UriKind.Absolute,out uri) || uri.Scheme!="http" || String.IsNullOrEmpty(uri.Host) || uri.Port<1 || uri.Port>65535 || uri.UserInfo.Length>0 || uri.Query.Length>0 || uri.Fragment.Length>0 || uri.AbsolutePath!="/" || value.IndexOfAny(new[]{'\r','\n','\t',' '})>=0)
                throw new InvalidOperationException(TextResources.Get("proxyConfiguration.endpoint.text"));
            return uri;
        }
        public static HttpClient CreateHttpClient(string address,bool allowRedirect)
        {
            return new HttpClient(new HttpClientHandler{UseProxy=true,Proxy=new WebProxy(Endpoint(address),false),AllowAutoRedirect=allowRedirect}){Timeout=Timeout.InfiniteTimeSpan};
        }
    }
}
