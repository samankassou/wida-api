using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Tokens;
using Wida.Api.Authentication;
using Wida.Dal.Entities;
using Wida.Dal.Persistence;

namespace Wida.Tests;

public sealed class AuthHttpTests
{
    [Theory]
    [InlineData("GET", "/api/documents")]
    [InlineData("GET", "/api/documents/workspace")]
    [InlineData("GET", "/api/documents/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/content")]
    [InlineData("GET", "/api/invoices")]
    [InlineData("GET", "/api/processing/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    [InlineData("POST", "/api/documents")]
    [InlineData("POST", "/api/processing/documents/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/invoice")]
    [InlineData("PUT", "/api/invoices/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")]
    public async Task Anonymous_requests_cannot_access_any_data(string method, string path)
    {
        await using var factory = new AuthFactory();
        using var browser = new TestBrowser(factory);
        var response = await browser.SendAsync(method, path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
    }

    [Fact]
    public async Task Google_code_flow_creates_local_session_with_pkce_and_verified_invitation()
    {
        await using var factory = new AuthFactory();
        using var browser = new TestBrowser(factory);
        var response = await browser.GoogleLoginAsync("alice@example.com", "google-alice");
        Assert.True(response.Headers.Location?.AbsoluteUri == "http://localhost:3000/", factory.Failure?.ToString() ?? response.Headers.Location?.ToString());
        var session = await browser.SessionAsync();
        Assert.True(session.GetProperty("authenticated").GetBoolean());
        Assert.Equal("alice@example.com", session.GetProperty("user").GetProperty("email").GetString());
        Assert.False(string.IsNullOrWhiteSpace(session.GetProperty("csrfToken").GetString()));
        Assert.Contains("Wida.Session", browser.Cookies.Keys);
        Assert.DoesNotContain(browser.Cookies.Values, value => value.Contains("test-access-token"));
        using var scope = factory.Services.CreateScope();
        Assert.Equal("google-alice", (await scope.ServiceProvider.GetRequiredService<WidaDbContext>().Users.SingleAsync()).GoogleSubject);
    }

    [Theory]
    [InlineData("outsider@example.com", true)]
    [InlineData("alice@example.com", false)]
    public async Task Uninvited_or_unverified_google_identity_cannot_create_an_account(string email, bool verified)
    {
        await using var factory = new AuthFactory { Verified = verified };
        using var browser = new TestBrowser(factory);
        var response = await browser.GoogleLoginAsync(email, "google-outsider");
        Assert.Equal("http://localhost:3000/login?error=not_invited", response.Headers.Location?.AbsoluteUri);
        Assert.False((await browser.SessionAsync()).GetProperty("authenticated").GetBoolean());
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<WidaDbContext>().Users.ToListAsync());
    }

    [Fact]
    public async Task Tampered_state_and_wrong_nonce_fail_without_creating_accounts()
    {
        await using var factory = new AuthFactory();
        using var browser = new TestBrowser(factory);
        var state = await browser.StartGoogleAsync();
        var response = await browser.SendAsync("GET", "/api/auth/callback?code=code&state=tampered");
        Assert.Equal("http://localhost:3000/login?error=authentication_failed", response.Headers.Location?.AbsoluteUri);
        factory.Nonce = "wrong-nonce";
        response = await browser.SendAsync("GET", "/api/auth/callback?code=code&state=" + Uri.EscapeDataString(state));
        Assert.Equal("http://localhost:3000/login?error=authentication_failed", response.Headers.Location?.AbsoluteUri);
        Assert.False((await browser.SessionAsync()).GetProperty("authenticated").GetBoolean());
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<WidaDbContext>().Users.ToListAsync());
    }

    [Theory]
    [InlineData("audience")]
    [InlineData("issuer")]
    [InlineData("signature")]
    public async Task Invalid_google_tokens_do_not_create_sessions(string invalidPart)
    {
        await using var factory = new AuthFactory { InvalidTokenPart = invalidPart };
        using var browser = new TestBrowser(factory);
        var response = await browser.GoogleLoginAsync("alice@example.com", "google-alice");
        Assert.Equal("http://localhost:3000/login?error=authentication_failed", response.Headers.Location?.AbsoluteUri);
        Assert.False((await browser.SessionAsync()).GetProperty("authenticated").GetBoolean());
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<WidaDbContext>().Users.ToListAsync());
    }

    [Fact]
    public async Task Mutations_require_user_bound_antiforgery_and_logout_clears_session()
    {
        await using var factory = new AuthFactory();
        using var browser = new TestBrowser(factory);
        await browser.GoogleLoginAsync("alice@example.com", "google-alice");
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.SendAsync("POST", "/api/auth/logout")).StatusCode);
        var session = await browser.SessionAsync();
        var csrf = session.GetProperty("csrfToken").GetString();
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.SendAsync("POST", "/api/auth/logout", csrf: "wrong")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await browser.SendAsync("POST", "/api/auth/logout", csrf: csrf)).StatusCode);
        Assert.False((await browser.SessionAsync()).GetProperty("authenticated").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.SendAsync("GET", "/api/documents")).StatusCode);
    }

    [Fact]
    public async Task Removing_an_invitation_invalidates_an_existing_session()
    {
        await using var factory = new AuthFactory();
        using var browser = new TestBrowser(factory);
        await browser.GoogleLoginAsync("alice@example.com", "google-alice");
        Assert.True((await browser.SessionAsync()).GetProperty("authenticated").GetBoolean());
        factory.Services.GetRequiredService<IConfiguration>()["Authentication:AllowedEmails:0"] = "removed@example.com";
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.SendAsync("GET", "/api/documents")).StatusCode);
        Assert.False((await browser.SessionAsync()).GetProperty("authenticated").GetBoolean());
    }

    [Fact]
    public async Task Different_accounts_cannot_read_download_or_analyze_each_others_documents()
    {
        await using var factory = new AuthFactory();
        using var alice = new TestBrowser(factory);
        await alice.GoogleLoginAsync("alice@example.com", "google-alice");
        var aliceSession = await alice.SessionAsync();
        var csrf = aliceSession.GetProperty("csrfToken").GetString();
        using var upload = new MultipartFormDataContent();
        var file = new ByteArrayContent("%PDF-1.7\nowned-test"u8.ToArray());
        file.Headers.ContentType = new("application/pdf");
        upload.Add(file, "file", "private.pdf");
        var uploaded = await alice.SendAsync("POST", "/api/documents", upload, csrf);
        Assert.Equal(HttpStatusCode.Created, uploaded.StatusCode);
        var id = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var bob = new TestBrowser(factory);
        await bob.GoogleLoginAsync("bob@example.com", "google-bob");
        var bobCsrf = (await bob.SessionAsync()).GetProperty("csrfToken").GetString();
        Assert.Equal("[]", await (await bob.SendAsync("GET", "/api/documents")).Content.ReadAsStringAsync());
        foreach (var path in new[] { $"/api/documents/{id}", $"/api/documents/{id}/content" })
            Assert.Equal(HttpStatusCode.NotFound, (await bob.SendAsync("GET", path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.SendAsync("POST", $"/api/processing/documents/{id}/invoice", csrf: bobCsrf)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await bob.SendAsync("POST", "/api/auth/logout", csrf: csrf)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.SendAsync("GET", $"/api/documents/{id}/content")).StatusCode);
        var workspace = await (await alice.SendAsync("GET", "/api/documents/workspace")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, workspace.GetArrayLength());
    }

    private sealed class AuthFactory : WebApplicationFactory<Program>
    {
        private readonly string _database = Guid.NewGuid().ToString();
        private readonly string _contentRoot = Directory.CreateTempSubdirectory("wida-auth-http-").FullName;
        private readonly RSA _rsa = RSA.Create(2048);
        public string? Nonce { get; set; }
        public string Email { get; set; } = "alice@example.com";
        public string Subject { get; set; } = "google-alice";
        public bool Verified { get; set; } = true;
        public Exception? Failure { get; set; }
        public string? InvalidTokenPart { get; set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseContentRoot(_contentRoot);
            builder.UseSetting("ConnectionStrings:DefaultConnection", "Host=unused;Database=unused");
            builder.UseSetting("Authentication:PublicOrigin", "http://localhost:3000");
            builder.UseSetting("Authentication:Google:ClientId", "pilot-test-client");
            builder.UseSetting("Authentication:Google:ClientSecret", "pilot-test-secret");
            builder.UseSetting("Authentication:AllowedEmails:0", "alice@example.com");
            builder.UseSetting("Authentication:AllowedEmails:1", "bob@example.com");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<WidaDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<WidaDbContext>>();
                services.AddDbContext<WidaDbContext>(options => options.UseInMemoryDatabase(_database));
                services.PostConfigure<OpenIdConnectOptions>(WidaAuthentication.GoogleScheme, options =>
                {
                    var key = new RsaSecurityKey(_rsa) { KeyId = "test-key" };
                    options.Configuration = new OpenIdConnectConfiguration
                    {
                        Issuer = "https://accounts.google.com",
                        AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth",
                        TokenEndpoint = "https://accounts.google.com/token"
                    };
                    options.Configuration.SigningKeys.Add(key);
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(options.Configuration);
                    var remoteFailure = options.Events.OnRemoteFailure;
                    options.Events.OnRemoteFailure = async context => { Failure = context.Failure; await remoteFailure(context); };
                    options.Backchannel = new HttpClient(new TokenHandler(async request =>
                    {
                        Assert.Equal("https://accounts.google.com/token", request.RequestUri?.AbsoluteUri);
                        var form = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync());
                        Assert.Equal("http://localhost:3000/api/wida/auth/callback", form["redirect_uri"].ToString());
                        Assert.False(string.IsNullOrEmpty(form["code_verifier"].ToString()));
                        var claims = new[] { new Claim("sub", Subject), new Claim("email", Email), new Claim("email_verified", Verified ? "true" : "false", ClaimValueTypes.Boolean), new Claim("name", "Test User"), new Claim("nonce", Nonce!), new Claim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64) };
                        using var wrongRsa = RSA.Create(2048);
                        var signingKey = InvalidTokenPart == "signature" ? new RsaSecurityKey(wrongRsa) { KeyId = key.KeyId } : key;
                        var token = new JwtSecurityToken(InvalidTokenPart == "issuer" ? "https://evil.example" : "https://accounts.google.com",
                            InvalidTokenPart == "audience" ? "wrong-client" : "pilot-test-client", claims,
                            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5), new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256));
                        return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { id_token = new JwtSecurityTokenHandler().WriteToken(token), access_token = "test-access-token", token_type = "Bearer", expires_in = 300 }) };
                    }));
                });
            });
        }

        private sealed class TokenHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            _rsa.Dispose();
            if (Directory.Exists(_contentRoot)) Directory.Delete(_contentRoot, recursive: true);
        }
    }

    private sealed class TestBrowser(AuthFactory factory) : IDisposable
    {
        private readonly HttpClient _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        public Dictionary<string, string> Cookies { get; } = [];

        public async Task<HttpResponseMessage> SendAsync(string method, string path, HttpContent? content = null, string? csrf = null)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = content };
            if (Cookies.Count > 0) request.Headers.Add("Cookie", string.Join("; ", Cookies.Select(x => $"{x.Key}={x.Value}")));
            if (csrf is not null) request.Headers.Add("X-CSRF-TOKEN", csrf);
            var response = await _client.SendAsync(request);
            if (response.Headers.TryGetValues("Set-Cookie", out var values))
                foreach (var value in values)
                {
                    var pair = value.Split(';')[0].Split('=', 2);
                    if (pair[1].Length == 0) Cookies.Remove(pair[0]); else Cookies[pair[0]] = pair[1];
                }
            return response;
        }

        public async Task<string> StartGoogleAsync()
        {
            var response = await SendAsync("GET", "/api/auth/login?returnUrl=https://evil.example");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var location = response.Headers.Location!;
            Assert.Equal("accounts.google.com", location.Host);
            var query = QueryHelpers.ParseQuery(location.Query);
            Assert.Equal("code", query["response_type"].ToString());
            Assert.Equal("S256", query["code_challenge_method"].ToString());
            Assert.Equal("http://localhost:3000/api/wida/auth/callback", query["redirect_uri"].ToString());
            factory.Nonce = query["nonce"].ToString();
            return query["state"].ToString();
        }

        public async Task<HttpResponseMessage> GoogleLoginAsync(string email, string subject)
        {
            factory.Email = email; factory.Subject = subject;
            var state = await StartGoogleAsync();
            return await SendAsync("GET", "/api/auth/callback?code=test-code&state=" + Uri.EscapeDataString(state));
        }

        public async Task<JsonElement> SessionAsync() => await (await SendAsync("GET", "/api/auth/session")).Content.ReadFromJsonAsync<JsonElement>();
        public void Dispose() => _client.Dispose();
    }
}
