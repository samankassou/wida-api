using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Configuration;
using Wida.Dal.Models;
using Wida.Dal.Services.Interfaces;

namespace Wida.Dal.Services;

// One HTTP request per call: the durable worker owns retry and polling policy.
public sealed class QueuedAzureDocumentAnalyzer(HttpClient http, IConfiguration configuration) : IQueuedDocumentAnalyzer
{
    public TimeSpan? RetryAfter { get; private set; }

    private const string ModelPath = "documentintelligence/documentModels/prebuilt-invoice";
    private const string ApiVersion = "api-version=2024-11-30";

    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var endpoint = configuration["AzureDocumentIntelligence:Endpoint"];
        var key = configuration["AzureDocumentIntelligence:Key"];
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var origin)
            || origin.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Configure an HTTPS Azure Document Intelligence endpoint and key.");
        var request = new HttpRequestMessage(method, new Uri(origin.AbsoluteUri.TrimEnd('/') + "/" + path));
        request.Headers.Add("Ocp-Apim-Subscription-Key", key);
        return request;
    }

    public async Task<string> SubmitAsync(string filePath, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Post, $"{ModelPath}:analyze?{ApiVersion}");
        await using var file = File.OpenRead(filePath);
        request.Content = new StreamContent(file);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var response = await http.SendAsync(request, cancellationToken);
        CheckResponse(response);
        if (response.StatusCode != HttpStatusCode.Accepted
            || !response.Headers.TryGetValues("Operation-Location", out var values)
            || !Uri.TryCreate(values.FirstOrDefault(), UriKind.Absolute, out var location)
            || !Guid.TryParse(location.Segments.Last(), out var id))
            throw new InvalidOperationException("Azure did not return a usable analysis operation identifier.");
        // Store only the ID; never follow an upstream URL with the subscription key.
        return id.ToString();
    }

    public async Task<DocumentAnalysisResult?> PollAsync(string operationId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(operationId, out var id)) throw new InvalidOperationException("Invalid Azure operation identifier.");
        using var request = Request(HttpMethod.Get, $"{ModelPath}/analyzeResults/{id}?{ApiVersion}");
        using var response = await http.SendAsync(request, cancellationToken);
        CheckResponse(response);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = payload.RootElement;
        return root.GetProperty("status").GetString() switch
        {
            "notStarted" or "running" => null,
            "succeeded" => AzureDocumentAnalyzer.MapResult(ModelReaderWriter.Read<AnalyzeResult>(
                BinaryData.FromString(root.GetProperty("analyzeResult").GetRawText()))!),
            _ => throw new InvalidOperationException("Azure could not complete this analysis.")
        };
    }

    private void CheckResponse(HttpResponseMessage response)
    {
        var retry = response.Headers.RetryAfter;
        var delay = retry?.Delta ?? (retry?.Date is { } date ? date - DateTimeOffset.UtcNow : (TimeSpan?)null);
        RetryAfter = delay;
        if (response.IsSuccessStatusCode) return;
        throw new AnalysisRequestException((int)response.StatusCode, delay);
    }
}
