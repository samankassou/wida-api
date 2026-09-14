using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Wida.Dal.Storage;

public sealed class SupabaseDocumentStorage : IDocumentStorage
{
    private readonly HttpClient http;
    private readonly Uri origin;
    private readonly string key;
    private readonly string bucket;
    private const string Prefix = "supabase:";

    public SupabaseDocumentStorage(HttpClient http, IConfiguration config)
    {
        this.http = http;
        if (!Uri.TryCreate(config["Storage:Supabase:Url"], UriKind.Absolute, out var url)
            || url.Scheme != "https" || url.AbsolutePath != "/" || url.Query != "" || url.Fragment != "" || url.UserInfo != "")
            throw new InvalidOperationException("Configure Storage:Supabase:Url with the HTTPS project origin.");
        origin = url;
        key = config["Storage:Supabase:ServiceKey"] ?? "";
        bucket = config["Storage:Supabase:Bucket"] ?? "wida-originals";
        if (string.IsNullOrWhiteSpace(key) || !SafeSegment(bucket))
            throw new InvalidOperationException("Configure a Supabase server key and private storage bucket.");
    }

    private static bool SafeSegment(string value) => value.Length is > 0 and <= 200
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.') && !value.Contains("..");
    private string ObjectKey(string location)
    {
        if (!location.StartsWith(Prefix, StringComparison.Ordinal) || !SafeSegment(location[Prefix.Length..]))
            throw new FileNotFoundException("Invalid object location. Migrate local originals before changing storage provider.");
        return location[Prefix.Length..];
    }
    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, new Uri(origin, "storage/v1/" + path));
        request.Headers.Add("apikey", key);
        // New sb_secret_ keys go only in apikey; Bearer expects a JWT.
        if (!key.StartsWith("sb_secret_", StringComparison.Ordinal))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return request;
    }
    private async Task CheckAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.NotFound) throw new FileNotFoundException("Original not found.");
        // Storage can return HTTP 400 with a structured 404 error for absent objects.
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            try
            {
                using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                if (body.RootElement.TryGetProperty("statusCode", out var code) && code.ToString() == "404")
                    throw new FileNotFoundException("Original not found.");
            }
            catch (JsonException) { }
        }
        // Do not include upstream response bodies, credentials or document names in exceptions.
        throw new HttpRequestException($"Object storage returned HTTP {(int)response.StatusCode}.", null, response.StatusCode);
    }
    public async Task<string> PersistAsync(string temporaryPath, string contentType, CancellationToken token)
    {
        var name = Path.GetFileName(temporaryPath);
        if (!SafeSegment(name)) throw new ArgumentException("Invalid object name.");
        using var request = Request(HttpMethod.Post, $"object/{bucket}/{name}");
        await using var stream = File.OpenRead(temporaryPath);
        request.Content = new StreamContent(stream);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        request.Headers.Add("x-upsert", "false");
        using var response = await http.SendAsync(request, token);
        await CheckAsync(response, token);
        return Prefix + name;
    }
    public async Task<Stream> OpenReadAsync(string location, CancellationToken token)
    {
        using var request = Request(HttpMethod.Get, $"object/authenticated/{bucket}/{ObjectKey(location)}");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        await CheckAsync(response, token);
        // A temporary, seekable file bounds RAM usage and preserves ASP.NET range processing.
        var path = Path.Combine(Path.GetTempPath(), "wida-read-" + Guid.NewGuid());
        var result = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        try
        {
            await response.Content.CopyToAsync(result, token);
            result.Position = 0;
            return result;
        }
        catch { await result.DisposeAsync(); throw; }
    }
    public async Task<bool> ExistsAsync(string location, CancellationToken token)
    {
        try
        {
            using var request = Request(HttpMethod.Get, $"object/authenticated/{bucket}/{ObjectKey(location)}");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            await CheckAsync(response, token);
            return true;
        }
        catch (FileNotFoundException) { return false; }
    }
    public async Task DeleteAsync(string location, CancellationToken token)
    {
        string name;
        try { name = ObjectKey(location); } catch (FileNotFoundException) { return; }
        using var request = Request(HttpMethod.Delete, $"object/{bucket}");
        request.Content = JsonContent.Create(new { prefixes = new[] { name } });
        using var response = await http.SendAsync(request, token);
        await CheckAsync(response, token);
    }
}
