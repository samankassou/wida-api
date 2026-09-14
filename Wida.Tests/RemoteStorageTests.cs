using System.Net;
using Microsoft.Extensions.Configuration;
using Wida.Dal.Storage;

namespace Wida.Tests;

public sealed class RemoteStorageTests
{
    [Fact]
    public async Task Upload_download_and_delete_use_private_authenticated_objects_and_seekable_streams()
    {
        var source = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf");
        await File.WriteAllTextAsync(source, "%PDF-original");
        var calls = 0;
        using var http = new HttpClient(new Handler(async request =>
        {
            Assert.Equal("service-test", request.Headers.Authorization!.Parameter);
            Assert.Equal("service-test", request.Headers.GetValues("apikey").Single());
            Assert.Equal("storage.test", request.RequestUri!.Host);
            calls++;
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal("false", request.Headers.GetValues("x-upsert").Single());
                Assert.Equal("%PDF-original", await request.Content!.ReadAsStringAsync());
                return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
            }
            if (request.Method == HttpMethod.Get)
            {
                Assert.Contains("/object/authenticated/wida-originals/", request.RequestUri.AbsolutePath);
                return new(HttpStatusCode.OK) { Content = new StringContent("%PDF-original") };
            }
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.Contains(Path.GetFileName(source), await request.Content!.ReadAsStringAsync());
            return new(HttpStatusCode.OK) { Content = new StringContent("[]") };
        }));
        try
        {
            var storage = new SupabaseDocumentStorage(http, Config());
            var location = await storage.PersistAsync(source, "application/pdf", default);
            Assert.StartsWith("supabase:", location);
            await using (var stream = await storage.OpenReadAsync(location, default))
            {
                Assert.True(stream.CanSeek);
                Assert.Equal(0, stream.Position);
                Assert.Equal(13, stream.Length);
                stream.Position = 5;
                Assert.Equal("original", await new StreamReader(stream).ReadToEndAsync());
            }
            Assert.True(await storage.ExistsAsync(location, default));
            await storage.DeleteAsync(location, default);
            Assert.Equal(4, calls);
        }
        finally { File.Delete(source); }
    }

    [Theory]
    [InlineData("supabase:../outside.pdf")]
    [InlineData("https://evil.test/file")]
    [InlineData("/app/uploads/legacy.pdf")]
    public async Task Invalid_locations_never_send_credentials(string location)
    {
        using var http = new HttpClient(new Handler(_ => throw new Exception("Must not fetch")));
        var storage = new SupabaseDocumentStorage(http, Config());
        await Assert.ThrowsAsync<FileNotFoundException>(() => storage.OpenReadAsync(location, default));
        Assert.False(await storage.ExistsAsync(location, default));
    }

    [Fact]
    public async Task Only_a_missing_object_is_treated_as_absent_not_outages_or_bad_credentials()
    {
        HttpStatusCode status = HttpStatusCode.BadRequest;
        using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(status)
            { Content = new StringContent("{\"statusCode\":\"404\"}") })));
        var storage = new SupabaseDocumentStorage(http, Config());
        Assert.False(await storage.ExistsAsync("supabase:valid.pdf", default));
        status = HttpStatusCode.ServiceUnavailable;
        await Assert.ThrowsAsync<HttpRequestException>(() => storage.ExistsAsync("supabase:valid.pdf", default));
        status = HttpStatusCode.Unauthorized;
        await Assert.ThrowsAsync<HttpRequestException>(() => storage.ExistsAsync("supabase:valid.pdf", default));
        status = HttpStatusCode.TemporaryRedirect;
        await Assert.ThrowsAsync<HttpRequestException>(() => storage.OpenReadAsync("supabase:valid.pdf", default));
    }

    [Fact]
    public async Task New_secret_keys_use_apikey_without_an_invalid_Bearer_token()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("sb_secret_test", request.Headers.GetValues("apikey").Single());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["Storage:Supabase:Url"] = "https://storage.test", ["Storage:Supabase:ServiceKey"] = "sb_secret_test" }).Build();
        Assert.True(await new SupabaseDocumentStorage(http, config).ExistsAsync("supabase:original.pdf", default));
    }

    private static IConfiguration Config() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    { ["Storage:Supabase:Url"] = "https://storage.test", ["Storage:Supabase:ServiceKey"] = "service-test" }).Build();
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request);
    }
}
