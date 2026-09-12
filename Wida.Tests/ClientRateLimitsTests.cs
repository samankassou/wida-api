using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Wida.Api.Authentication;

namespace Wida.Tests;

public sealed class ClientRateLimitsTests
{
    private static DefaultHttpContext Context(string ip, string? user = null, string method = "GET")
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        context.Request.Method = method;
        if (user is not null) context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, user)], "test"));
        return context;
    }

    [Fact]
    public void Anonymous_exhaustion_does_not_block_other_clients_or_signed_in_users()
    {
        using var limiter = ClientRateLimits.Create();
        var attacker = Context("192.0.2.1");
        for (var i = 0; i < 120; i++) Assert.True(limiter.AttemptAcquire(attacker).IsAcquired);
        Assert.False(limiter.AttemptAcquire(attacker).IsAcquired);
        Assert.False(limiter.AttemptAcquire(Context("::ffff:192.0.2.1")).IsAcquired);
        Assert.True(limiter.AttemptAcquire(Context("192.0.2.2")).IsAcquired);
        Assert.True(limiter.AttemptAcquire(Context("192.0.2.1", "alice")).IsAcquired);
    }

    [Fact]
    public void Accounts_behind_one_proxy_have_independent_quotas_and_changing_IP_does_not_reset_them()
    {
        using var limiter = ClientRateLimits.Create();
        for (var user = 0; user < 6; user++)
        {
            var context = Context("10.0.0.1", user.ToString());
            for (var i = 0; i < 120; i++) Assert.True(limiter.AttemptAcquire(context).IsAcquired);
            Assert.False(limiter.AttemptAcquire(Context("192.0.2.2", user.ToString())).IsAcquired);
            var write = Context("10.0.0.1", user.ToString(), "POST");
            for (var i = 0; i < 12; i++) Assert.True(limiter.AttemptAcquire(write).IsAcquired);
            Assert.False(limiter.AttemptAcquire(write).IsAcquired);
        }
    }

    [Fact]
    public async Task Only_configured_proxy_can_supply_a_single_client_IP()
    {
        var middleware = new TrustedClientIp(Config("10.0.0.1"), new Environment());
        var valid = Context("::ffff:10.0.0.1");
        valid.Request.Headers[TrustedClientIp.Header] = "192.0.2.1";
        var called = false;
        await middleware.InvokeAsync(valid, context =>
        {
            called = true;
            Assert.Equal(IPAddress.Parse("192.0.2.1"), context.Connection.RemoteIpAddress);
            return Task.CompletedTask;
        });
        Assert.True(called);
        foreach (var value in new[] { "", "invalid", "192.0.2.1, 192.0.2.2" })
        {
            var bad = Context("10.0.0.1");
            bad.Request.Headers[TrustedClientIp.Header] = value;
            await middleware.InvokeAsync(bad, _ => throw new Exception("Must reject malformed IP"));
            Assert.Equal(400, bad.Response.StatusCode);
        }
        var spoofed = Context("192.0.2.9");
        spoofed.Request.Headers[TrustedClientIp.Header] = "192.0.2.1";
        await middleware.InvokeAsync(spoofed, _ => throw new Exception("Must reject untrusted peer"));
        Assert.Equal(403, spoofed.Response.StatusCode);
    }

    [Fact]
    public async Task Development_ignores_untrusted_IP_headers_and_production_requires_proxy_configuration()
    {
        Assert.Throws<InvalidOperationException>(() => new TrustedClientIp(Config(), new Environment()));
        var middleware = new TrustedClientIp(Config(), new Environment { EnvironmentName = "Development" });
        var context = Context("192.0.2.9");
        context.Request.Headers[TrustedClientIp.Header] = "192.0.2.1";
        context.Request.Headers["X-Forwarded-For"] = "192.0.2.2";
        await middleware.InvokeAsync(context, _ => Task.CompletedTask);
        Assert.Equal(IPAddress.Parse("192.0.2.9"), context.Connection.RemoteIpAddress);
    }

    private static IConfiguration Config(string? proxy = null) => new ConfigurationBuilder()
        .AddInMemoryCollection(proxy is null ? [] : new Dictionary<string, string?> { ["RateLimiting:TrustedProxies:0"] = proxy }).Build();

    private sealed class Environment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Tests";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
