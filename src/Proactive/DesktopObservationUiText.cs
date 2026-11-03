using System;
using System.Collections.Generic;

namespace Dafeiyu
{
    // Safe display-only labels; consent itself remains bound to the complete route digest.
    public static class DesktopObservationUiText
    {
        public static string ReasonText(ObservationReason reason)
        {
            switch (reason)
            {
                case ObservationReason.Allowed: return TextResources.Get("desktopObservation.reason.allowed");
                case ObservationReason.Disabled: return TextResources.Get("desktopObservation.reason.disabled");
                case ObservationReason.NotDue: return TextResources.Get("desktopObservation.reason.notDue");
                case ObservationReason.Ineligible: return TextResources.Get("desktopObservation.reason.ineligible");
                case ObservationReason.Stale: return TextResources.Get("desktopObservation.reason.stale");
                case ObservationReason.NoRoute: return TextResources.Get("desktopObservation.reason.noRoute");
                case ObservationReason.InvalidConfiguration: return TextResources.Get("desktopObservation.reason.invalidConfiguration");
                case ObservationReason.ConsentRequired: return TextResources.Get("desktopObservation.reason.consentRequired");
                case ObservationReason.MinimumInterval: return TextResources.Get("desktopObservation.reason.minimumInterval");
                case ObservationReason.HourlyLimit: return TextResources.Get("desktopObservation.reason.hourlyLimit");
                case ObservationReason.Cooldown: return TextResources.Get("desktopObservation.reason.cooldown");
                case ObservationReason.Busy: return TextResources.Get("desktopObservation.reason.busy");
                case ObservationReason.CaptureUnavailable: return TextResources.Get("desktopObservation.reason.captureUnavailable");
                case ObservationReason.CaptureTimeout: return TextResources.Get("desktopObservation.reason.captureTimeout");
                case ObservationReason.NoFrame: return TextResources.Get("desktopObservation.reason.noFrame");
                case ObservationReason.Excluded: return TextResources.Get("desktopObservation.reason.excluded");
                case ObservationReason.DuplicateScene: return TextResources.Get("desktopObservation.reason.duplicateScene");
                case ObservationReason.ModelSkip: return TextResources.Get("desktopObservation.reason.modelSkip");
                case ObservationReason.DuplicateMessage: return TextResources.Get("desktopObservation.reason.duplicateMessage");
                case ObservationReason.ModelFailed: return TextResources.Get("desktopObservation.reason.modelFailed");
                case ObservationReason.InvalidResponse: return TextResources.Get("desktopObservation.reason.invalidResponse");
                case ObservationReason.ModelTimeout: return TextResources.Get("desktopObservation.reason.modelTimeout");
                case ObservationReason.TotalTimeout: return TextResources.Get("desktopObservation.reason.totalTimeout");
                case ObservationReason.Cancelled: return TextResources.Get("desktopObservation.reason.cancelled");
                default: return TextResources.Get("desktopObservation.reason.ineligible");
            }
        }

        public static string SafeAddress(string address)
        {
            Uri uri;
            if (!Uri.TryCreate(address, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                String.IsNullOrEmpty(uri.Host)) return "";
            var builder = new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" };
            return builder.Uri.AbsoluteUri;
        }

        public static string ReceivingSummary(PetSettings settings)
        {
            if (settings == null) return "";
            try
            {
                var route = ImageModelResolver.Resolve(settings, null);
                if (route.Route == ImageRouteKind.Unavailable) return "";
                var imageConnection = route.Route == ImageRouteKind.Direct ? route.ConversationConnection : route.DescriptionConnection;
                string image = SafeAddress(ApiClient.Endpoint(imageConnection, ApiClient.ChatPath(imageConnection)).AbsoluteUri);
                if (image.Length == 0) return "";
                string result = TextResources.Format("desktopObservation.imageDestination", image);
                if (route.Route == ImageRouteKind.Described)
                {
                    string text = SafeAddress(ApiClient.Endpoint(route.ConversationConnection, ApiClient.ChatPath(route.ConversationConnection)).AbsoluteUri);
                    if (text.Length == 0) return "";
                    result += "\n" + TextResources.Format("desktopObservation.textDestination", text);
                }
                return result;
            }
            catch (ApiException) { return ""; }
        }
        public static string[] Destinations(PetSettings settings)
        {
            if (settings == null) return new string[0];
            try
            {
                var route = ImageModelResolver.Resolve(settings, null);
                if (route.Route == ImageRouteKind.Unavailable) return new string[0];
                var addresses = new List<string>();
                if (route.Route == ImageRouteKind.Described) AddAddress(addresses, route.DescriptionConnection);
                AddAddress(addresses, route.ConversationConnection);
                return addresses.ToArray();
            }
            catch (ApiException) { return new string[0]; }
        }

        static void AddAddress(List<string> addresses, ConnectionSettings connection)
        {
            string address = SafeAddress(ApiClient.Endpoint(connection, ApiClient.ChatPath(connection)).AbsoluteUri);
            if (address.Length > 0 && !addresses.Contains(address)) addresses.Add(address);
        }
    }
}
