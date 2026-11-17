using System.Net;
using System.Text.Json.Nodes;

namespace Nook.Api.Tests.Integration;

/// <summary>Reaching a publication's manifest and its token path, and reading what the manifest says.</summary>
internal static class PublicationManifests
{
    public static string ManifestUrl(Guid itemId) => $"/api/items/{itemId}/publication/manifest.json";

    public static string TokenUrl(Guid itemId, string token, string path) =>
        $"/api/items/{itemId}/publication/t/{token}/{path}";

    /// <summary>The item's manifest as <paramref name="client"/> gets it, which must succeed.</summary>
    public static async Task<JsonObject> GetManifestAsync(HttpClient client, Guid itemId)
    {
        var response = await client.GetAsync(ManifestUrl(itemId), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ReadManifestAsync(response);
    }

    public static async Task<JsonObject> ReadManifestAsync(HttpResponseMessage response) =>
        Assert.IsType<JsonObject>(
            JsonNode.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)));

    public static string SelfHref(JsonObject manifest)
    {
        var links = Assert.IsType<JsonArray>(manifest["links"]);
        var self = Assert.Single(links, link => (string?)link?["rel"] == "self");
        var href = (string?)self?["href"];
        Assert.NotNull(href);
        return href;
    }

    /// <summary>The token in a manifest's self link.</summary>
    public static string TokenOf(string selfHref) => selfHref.Split('/')[^2];

    /// <summary>An href of the manifest as an absolute URL, resolved against its self link.</summary>
    public static Uri Resolve(JsonObject manifest, string href) => new(new Uri(SelfHref(manifest)), href);
}
