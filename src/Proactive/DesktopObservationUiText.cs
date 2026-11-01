using System;
using System.Collections.Generic;

namespace Dafeiyu
{
    // Safe display-only labels; consent itself remains bound to the complete route digest.
    public static class DesktopObservationUiText
    {
        public static string SafeAddress(string address)
        {
            Uri uri;
            if (!Uri.TryCreate(address, UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                String.IsNullOrEmpty(uri.Host)) return "";
            var builder = new UriBuilder(uri) { UserName = "", Password = "", Query = "", Fragment = "" };
            return builder.Uri.AbsoluteUri;
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
