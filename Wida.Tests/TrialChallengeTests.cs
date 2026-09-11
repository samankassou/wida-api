using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Wida.Api.Authentication;
using Wida.Bll.Exceptions;

namespace Wida.Tests;

public class TrialChallengeTests
{
    [Fact]
    public void Admin_does_not_require_a_captcha_cookie()
    {
        var challenge = Create("{}");
        var context = Context("admin");
        ((ClaimsIdentity)context.User.Identity!).AddClaim(new Claim(ClaimTypes.Role, "Admin"));
        Assert.True(challenge.IsVerified(context));
        Assert.False(challenge.IsVerified(Context("user")));
    }

    [Fact]
    public async Task Valid_challenge_is_bound_to_the_authenticated_owner()
    {
        var challenge = Create("{\"success\":true,\"action\":\"trial\",\"hostname\":\"localhost\"}");
        var alice = Context("alice");
        await challenge.VerifyAsync(alice, "token", default);
        var cookie = alice.Response.Headers.SetCookie.ToString().Split(';')[0];
        alice.Request.Headers.Cookie = cookie;
        Assert.True(challenge.IsVerified(alice));
        var bob = Context("bob"); bob.Request.Headers.Cookie = cookie;
        Assert.False(challenge.IsVerified(bob));
    }

    [Theory]
    [InlineData("{\"success\":false}")]
    [InlineData("{\"success\":true,\"action\":\"login\",\"hostname\":\"localhost\"}")]
    [InlineData("{\"success\":true,\"action\":\"trial\",\"hostname\":\"elsewhere.example\"}")]
    public async Task Invalid_challenges_cannot_authorize_uploads(string result)
    {
        var challenge = Create(result);
        var context = Context("alice");
        await Assert.ThrowsAsync<TrialLimitException>(() => challenge.VerifyAsync(context, "token", default));
        Assert.False(challenge.IsVerified(context));
        Assert.Equal(0, context.Response.Headers.SetCookie.Count);
    }

    private static DefaultHttpContext Context(string owner) => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, owner)], "test"))
    };

    private static TrialChallenge Create(string result)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Turnstile:SiteKey"] = "test-site", ["Turnstile:SecretKey"] = "test-secret",
            ["Authentication:PublicOrigin"] = "http://localhost:3000"
        }).Build();
        return new TrialChallenge(configuration, new EphemeralDataProtectionProvider(), new ClientFactory(result),
            new PilotAccess(configuration, new EnvironmentStub()));
    }
    private sealed class ClientFactory(string result) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Handler(result));
    }
    private sealed class Handler(string result) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Assert.Equal("https://challenges.cloudflare.com/turnstile/v0/siteverify", request.RequestUri!.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(result) });
        }
    }
    private sealed class EnvironmentStub : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "/tmp";
        public string WebRootPath { get; set; } = "/tmp";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
