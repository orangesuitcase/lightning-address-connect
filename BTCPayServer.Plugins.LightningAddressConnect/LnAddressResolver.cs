#nullable enable
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.LightningAddressConnect;

/// <summary>Resolved LNURL-pay metadata endpoint for a Lightning Address.</summary>
public sealed record ResolvedLnAddress(string LightningAddress, Uri PayEndpoint, string Domain);

public static class LnAddressResolver
{
    public static async Task<ResolvedLnAddress> Resolve(string lightningAddress, HttpClient http, CancellationToken ct)
    {
        lightningAddress = (lightningAddress ?? "").Trim();
        if (!lightningAddress.Contains('@'))
            throw new FormatException(
                "Expected a Lightning Address like you@getalby.com (user@domain).");

        var parts = lightningAddress.Split('@');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            throw new FormatException($"Invalid Lightning Address '{lightningAddress}'.");

        var username = parts[0].Trim();
        var domain = parts[1].Trim().ToLowerInvariant();
        lightningAddress = $"{username}@{domain}";

        // Providers not supported yet: fail fast with a friendly message before probing.
        var known = SupportedWallets.Match(lightningAddress);
        if (known is { Lud21Supported: false })
            throw new FormatException(known.Notes);

        Uri endpoint;
        try
        {
            endpoint = LNURL.LNURL.ExtractUriFromInternetIdentifier(lightningAddress);
        }
        catch (Exception e)
        {
            throw new FormatException($"Could not resolve Lightning Address '{lightningAddress}': {e.Message}");
        }

        var json = await GetJson(http, endpoint, ct);
        if (json["tag"]?.Value<string>() != "payRequest")
            throw new FormatException($"'{lightningAddress}' did not return a valid LNURL-pay (payRequest) endpoint.");

        return new ResolvedLnAddress(lightningAddress, endpoint, domain);
    }

    internal static async Task<JObject> GetJson(HttpClient http, Uri uri, CancellationToken ct)
    {
        using var resp = await http.GetAsync(uri, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new FormatException($"LNURL endpoint '{uri}' returned HTTP {(int)resp.StatusCode}.");
        var json = JObject.Parse(body);
        if (json["status"]?.Value<string>()?.Equals("ERROR", StringComparison.OrdinalIgnoreCase) == true)
            throw new FormatException(json["reason"]?.Value<string>() ?? $"LNURL endpoint '{uri}' returned an error.");
        return json;
    }
}
