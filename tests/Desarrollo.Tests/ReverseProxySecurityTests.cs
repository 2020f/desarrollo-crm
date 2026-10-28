using System.Net;
using Desarrollo.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Desarrollo.Tests;

public sealed class ReverseProxySecurityTests
{
    [Fact]
    public async Task Trusted_proxy_restores_https_for_secure_antiforgery_without_trusting_client_ip_or_host()
    {
        using var deployment = new Deployment("", " ", "192.0.2.10", "::1");
        var context = deployment.Request("192.0.2.10", "https");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.20";
        context.Request.Headers["X-Forwarded-Host"] = "attacker.invalid";

        await deployment.Pipeline(context);

        Assert.True(context.Request.IsHttps);
        Assert.IsType<string>(context.Items["antiforgery-token"]);
        Assert.Contains("; secure", context.Response.Headers.SetCookie.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(IPAddress.Parse("192.0.2.10"), context.Connection.RemoteIpAddress);
        Assert.Equal("crm.example.invalid", context.Request.Host.Value);
        Assert.Equal("203.0.113.20", context.Request.Headers["X-Forwarded-For"].ToString());
    }

    [Theory]
    [InlineData("192.0.2.11")]
    [InlineData("127.12.13.14")]
    public async Task Unknown_peer_cannot_claim_https_or_bypass_secure_antiforgery(string peer)
    {
        using var deployment = new Deployment("192.0.2.10");
        var context = deployment.Request(peer, "https");

        await Assert.ThrowsAsync<InvalidOperationException>(() => deployment.Pipeline(context));

        Assert.False(context.Request.IsHttps);
        Assert.Equal(0, context.Response.Headers.SetCookie.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_blank_proxy_configuration_leaves_forwarded_headers_disabled(bool blankEntries)
    {
        using var deployment = blankEntries ? new Deployment("", " ") : new Deployment();
        Assert.False(deployment.ProxyEnabled);
        var context = deployment.Request("127.0.0.1", "https");

        await Assert.ThrowsAsync<InvalidOperationException>(() => deployment.Pipeline(context));

        Assert.Equal("http", context.Request.Scheme);
        Assert.Equal(0, context.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public async Task Only_the_immediate_proxy_scheme_is_consumed()
    {
        using var deployment = new Deployment("192.0.2.10");
        var context = deployment.Request("192.0.2.10", "https,http");

        await Assert.ThrowsAsync<InvalidOperationException>(() => deployment.Pipeline(context));

        Assert.False(context.Request.IsHttps);
    }

    [Theory]
    [InlineData("proxy.example.invalid")]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    [InlineData("not-an-address")]
    public void Invalid_or_network_proxy_addresses_fail_startup(string value)
    {
        var services = new ServiceCollection();
        var configuration = Configuration(value);

        Assert.Throws<InvalidOperationException>(() => ReverseProxyConfiguration.Configure(services, configuration));
    }

    private static IConfiguration Configuration(params string[] proxies) => new ConfigurationBuilder()
        .AddInMemoryCollection(proxies.Select((value, index) => new KeyValuePair<string, string?>($"ReverseProxy:KnownProxies:{index}", value)))
        .Build();

    private sealed class Deployment : IDisposable
    {
        private readonly ServiceProvider _services;
        public bool ProxyEnabled { get; }
        public RequestDelegate Pipeline { get; }

        public Deployment(params string[] proxies)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDataProtection().UseEphemeralDataProtectionProvider();
            services.AddAntiforgery(options =>
            {
                options.Cookie.Name = "XSRF-TOKEN";
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            });
            ProxyEnabled = ReverseProxyConfiguration.Configure(services, Configuration(proxies));
            _services = services.BuildServiceProvider();
            var application = new ApplicationBuilder(_services);
            if (ProxyEnabled) application.UseForwardedHeaders();
            application.Run(context =>
            {
                var tokens = context.RequestServices.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
                context.Items["antiforgery-token"] = tokens.RequestToken;
                return Task.CompletedTask;
            });
            Pipeline = application.Build();
        }

        public DefaultHttpContext Request(string peer, string scheme) => new()
        {
            RequestServices = _services,
            Request = { Scheme = "http", Host = new HostString("crm.example.invalid"), Headers = { ["X-Forwarded-Proto"] = scheme } },
            Connection = { RemoteIpAddress = IPAddress.Parse(peer) },
        };

        public void Dispose() => _services.Dispose();
    }
}
