using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Desarrollo.Web.Services;

/// <summary>Accept the original HTTPS scheme only from explicitly configured proxy peers.</summary>
public static class ReverseProxyConfiguration
{
    public static bool Configure(IServiceCollection services, IConfiguration configuration)
    {
        var configured = configuration.GetSection("ReverseProxy:KnownProxies").Get<string[]>() ?? [];
        var proxies = new List<IPAddress>();
        foreach (var value in configured.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (!IPAddress.TryParse(value.Trim(), out var address))
                throw new InvalidOperationException("ReverseProxy:KnownProxies admite únicamente direcciones IP exactas; no nombres ni redes.");
            if (!proxies.Contains(address)) proxies.Add(address);
        }
        // Empty known-proxy/network lists mean trust every peer to the framework.
        // Keep the middleware disabled when the operator has not named a proxy.
        if (proxies.Count == 0) return false;
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var address in proxies) options.KnownProxies.Add(address);
        });
        return true;
    }
}
