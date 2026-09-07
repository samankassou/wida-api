namespace Wida.Api.Files;

public static class DocumentFilePolicy
{
    public const long MaximumBytes = 20 * 1024 * 1024;
    public const long MaximumRequestBytes = MaximumBytes + 1024 * 1024;

    public static string? ContentTypeFor(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".tif" or ".tiff" => "image/tiff",
        _ => null
    };

    public static string SafeName(string fileName) => Path.GetFileName(fileName.Replace('\\', '/'));

    public static bool MatchesSignature(ReadOnlySpan<byte> header, string contentType) => contentType switch
    {
        "application/pdf" => header.StartsWith("%PDF-"u8),
        "image/png" => header.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }),
        "image/jpeg" => header.StartsWith(new byte[] { 255, 216, 255 }),
        "image/tiff" => header.StartsWith(new byte[] { 73, 73, 42, 0 })
            || header.StartsWith(new byte[] { 77, 77, 0, 42 }),
        _ => false
    };

    public static async Task<bool> HasMatchingSignatureAsync(Stream stream, string contentType,
        CancellationToken cancellationToken)
    {
        var header = new byte[8];
        var length = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        stream.Position = 0;
        return MatchesSignature(header.AsSpan(0, length), contentType);
    }
}
