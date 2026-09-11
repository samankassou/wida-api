using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Wida.Dal.Services;
using Wida.Dal.Services.Interfaces;

namespace Wida.Tests;

public sealed class QueuedAzureDocumentAnalyzerTests
{
    [Fact]
    public async Task Submission_sends_original_bytes_once_and_stores_only_the_operation_id()
    {
        var id = Guid.NewGuid();
        var file = Path.GetTempFileName();
        await File.WriteAllTextAsync(file, "%PDF-test");
        try
        {
            var handler = new Handler(async request =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.Equal("https://example.test/documentintelligence/documentModels/prebuilt-invoice:analyze?api-version=2024-11-30", request.RequestUri!.AbsoluteUri);
                Assert.Equal("%PDF-test", await request.Content!.ReadAsStringAsync());
                Assert.Equal("application/octet-stream", request.Content.Headers.ContentType!.MediaType);
                var response = new HttpResponseMessage(HttpStatusCode.Accepted);
                response.Headers.Add("Operation-Location", $"https://example.test/documentintelligence/documentModels/prebuilt-invoice/analyzeResults/{id}?api-version=2024-11-30");
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(5));
                return response;
            });
            using var http = new HttpClient(handler);
            var client = Client(http);
            Assert.Equal(id.ToString(), await client.SubmitAsync(file, default));
            Assert.Equal(TimeSpan.FromSeconds(5), client.RetryAfter);
            Assert.Equal(1, handler.Calls);
        }
        finally { File.Delete(file); }
    }

    [Theory]
    [InlineData("running")]
    [InlineData("notStarted")]
    public async Task Polling_a_pending_operation_returns_without_an_internal_loop(string status)
    {
        var id = Guid.NewGuid();
        var handler = new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains($"/analyzeResults/{id}?", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"status\":\"" + status + "\"}") });
        });
        using var http = new HttpClient(handler);
        Assert.Null(await Client(http).PollAsync(id.ToString(), default));
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Throttling_exposes_retry_after_without_replaying_the_request()
    {
        var handler = new Handler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));
            return Task.FromResult(response);
        });
        using var http = new HttpClient(handler);
        var ex = await Assert.ThrowsAsync<AnalysisRequestException>(() => Client(http).PollAsync(Guid.NewGuid().ToString(), default));
        Assert.Equal(429, ex.StatusCode); Assert.Equal(TimeSpan.FromSeconds(30), ex.RetryAfter);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Completed_poll_maps_sdk_fields_without_submitting_another_analysis()
    {
        var handler = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {"status":"succeeded","analyzeResult":{"apiVersion":"2024-11-30","modelId":"prebuilt-invoice",
                "stringIndexType":"utf16CodeUnit","content":"Supplier","pages":[],
                "documents":[{"docType":"invoice","spans":[],"fields":{"VendorName":{"type":"string","valueString":"Supplier","confidence":0.9}}}]}}
                """)
        }));
        using var http = new HttpClient(handler);
        var result = await Client(http).PollAsync(Guid.NewGuid().ToString(), default);
        Assert.Equal("VendorName", Assert.Single(result!.Fields).Name);
        Assert.Equal("Supplier", result.Fields[0].NormalizedValue!.Value.GetString());
    }

    private static QueuedAzureDocumentAnalyzer Client(HttpClient http) => new(http,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureDocumentIntelligence:Endpoint"] = "https://example.test/",
            ["AzureDocumentIntelligence:Key"] = "test-only"
        }).Build());

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; return respond(request); }
    }
}
