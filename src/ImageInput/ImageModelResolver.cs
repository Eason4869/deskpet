using System;
using System.Linq;
namespace Dafeiyu
{
    public enum ImageRouteKind { Direct,Described,Unavailable }
    // Request-owned copies: subsequent settings/model changes cannot redirect an in-flight request.
    public sealed class ImageRouteSnapshot
    {
        public ImageRouteKind Route {get;private set;}
        public ConnectionSettings ConversationConnection {get;private set;}
        public ConnectionSettings DescriptionConnection {get;private set;}
        public string Reason {get;private set;}
        internal ImageRouteSnapshot(ImageRouteKind route,ConnectionSettings conversation,ConnectionSettings description,string reason)
        {Route=route;ConversationConnection=conversation;DescriptionConnection=description;Reason=reason;}
    }
    public static class ImageModelResolver
    {
        public static ImageRouteSnapshot Resolve(PetSettings settings,ImageDescriptionOverride selection)
        {
            if(settings==null)throw new ArgumentNullException("settings");
            var current=(settings.Text??new ConnectionSettings()).Copy();
            var images=AgentCapabilityResolver.Resolve(new PetSettings{Text=current},null).Images;
            if(images==CapabilitySupport.Supported)return new ImageRouteSnapshot(ImageRouteKind.Direct,current,null,"");
            var effective=selection??settings.ImageDescriptionOverride;
            effective=effective==null?new ImageDescriptionOverride():effective.Copy();effective.Normalize();
            if(effective.Mode=="disabled")return Unavailable(current,"description-disabled");
            var reference=effective.Mode=="selected"?effective.Model:settings.ImageDescriptionModel;
            if(reference==null)return Unavailable(current,"description-not-selected");
            reference=reference.Copy();reference.Normalize();
            if(reference.ProviderId.Length==0||reference.ModelId.Length==0)return Unavailable(current,"description-reference-invalid");
            // Do not normalize settings or fall back to another provider's same-named model.
            var matches=(settings.TextProviders??new System.Collections.Generic.List<TextProviderSettings>()).Where(p=>p!=null&&p.Id==reference.ProviderId).ToArray();
            if(matches.Length!=1)return Unavailable(current,"description-provider-missing");
            if(matches[0].Models==null||!matches[0].Models.Any(m=>String.Equals((m??"").Trim(),reference.ModelId,StringComparison.Ordinal)))return Unavailable(current,"description-model-missing");
            var provider=matches[0].Copy();provider.Normalize();
            var description=provider.Connection.Copy();description.Model=reference.ModelId;description.ContextWindowTokens=provider.ContextCapacity(reference.ModelId);provider.ApplyModelCapabilities(description);
            Uri destination;if(!Uri.TryCreate(description.BaseUrl,UriKind.Absolute,out destination)||(destination.Scheme!="https"&&destination.Scheme!="http")||String.IsNullOrEmpty(destination.Host))return Unavailable(current,"description-destination-invalid");
            if(AgentCapabilityResolver.Resolve(new PetSettings{Text=description},null).Images!=CapabilitySupport.Supported)return Unavailable(current,"description-images-unconfirmed");
            return new ImageRouteSnapshot(ImageRouteKind.Described,current,description,images==CapabilitySupport.Unknown?"current-images-unknown":"");
        }
        static ImageRouteSnapshot Unavailable(ConnectionSettings current,string reason){return new ImageRouteSnapshot(ImageRouteKind.Unavailable,current,null,reason);}
    }
}
