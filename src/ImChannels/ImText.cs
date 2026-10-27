using System;

namespace Dafeiyu.ImChannels
{
    // Keep status and field keys explicit so the localization audit can verify every branch.
    internal static class ImText
    {
        internal static string Get(string key)
        {
            switch(key){
                case "im.add":return TextResources.Get("im.add");
                case "im.allowHint":return TextResources.Get("im.allowHint");
                case "im.appId":return TextResources.Get("im.appId");
                case "im.authFailed":return TextResources.Get("im.authFailed");
                case "im.binding":return TextResources.Get("im.binding");
                case "im.cancelLogin":return TextResources.Get("im.cancelLogin");
                case "im.connect":return TextResources.Get("im.connect");
                case "im.connected":return TextResources.Get("im.connected");
                case "im.connecting":return TextResources.Get("im.connecting");
                case "im.connectionReplaced":return TextResources.Get("im.connectionReplaced");
                case "im.contextCleared":return TextResources.Get("im.contextCleared");
                case "im.delete":return TextResources.Get("im.delete");
                case "im.deliveryFailed":return TextResources.Get("im.deliveryFailed");
                case "im.deliveryUncertain":return TextResources.Get("im.deliveryUncertain");
                case "im.disconnected":return TextResources.Get("im.disconnected");
                case "im.endpointRejected":return TextResources.Get("im.endpointRejected");
                case "im.failed":return TextResources.Get("im.failed");
                case "im.groups":return TextResources.Get("im.groups");
                case "im.help":return TextResources.Get("im.help");
                case "im.httpFailed":return TextResources.Get("im.httpFailed");
                case "im.invalidConfig":return TextResources.Get("im.invalidConfig");
                case "im.lastSender":return TextResources.Get("im.lastSender");
                case "im.localOnly":return TextResources.Get("im.localOnly");
                case "im.login":return TextResources.Get("im.login");
                case "im.modelFailed":return TextResources.Get("im.modelFailed");
                case "im.name":return TextResources.Get("im.name");
                case "im.openPlatform":return TextResources.Get("im.openPlatform");
                case "im.pause":return TextResources.Get("im.pause");
                case "im.paused":return TextResources.Get("im.paused");
                case "im.payloadTooLarge":return TextResources.Get("im.payloadTooLarge");
                case "im.platform":return TextResources.Get("im.platform");
                case "im.platform.dingtalk":return TextResources.Get("im.platform.dingtalk");
                case "im.platform.feishu":return TextResources.Get("im.platform.feishu");
                case "im.platform.qq":return TextResources.Get("im.platform.qq");
                case "im.platform.wecom":return TextResources.Get("im.platform.wecom");
                case "im.platform.weixin":return TextResources.Get("im.platform.weixin");
                case "im.platformFailed":return TextResources.Get("im.platformFailed");
                case "im.protocolFailed":return TextResources.Get("im.protocolFailed");
                case "im.qrExpired":return TextResources.Get("im.qrExpired");
                case "im.reconnecting":return TextResources.Get("im.reconnecting");
                case "im.replyExpired":return TextResources.Get("im.replyExpired");
                case "im.save":return TextResources.Get("im.save");
                case "im.secret":return TextResources.Get("im.secret");
                case "im.selectWeixin":return TextResources.Get("im.selectWeixin");
                case "im.sessionLimit":return TextResources.Get("im.sessionLimit");
                case "im.storageFailed":return TextResources.Get("im.storageFailed");
                case "im.subtitle":return TextResources.Get("im.subtitle");
                case "im.textOnly":return TextResources.Get("im.textOnly");
                case "im.title":return TextResources.Get("im.title");
                case "im.unsaved":return TextResources.Get("im.unsaved");
                case "im.users":return TextResources.Get("im.users");
                case "im.verificationRequired":return TextResources.Get("im.verificationRequired");
                default:return TextResources.Get("im.failed");
            }
        }
    }
}
