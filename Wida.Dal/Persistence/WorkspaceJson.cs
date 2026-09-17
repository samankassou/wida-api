using System.Text.Json;

namespace Wida.Dal.Persistence;

// PostgreSQL jsonb functions; managed bodies keep the same semantics for tests.
public static class WorkspaceJson
{
    public static string? Value(string? json) => Read(json, null);
    public static string? Property(string? json, string key) => Read(json, key);
    private static string? Read(string? json, string? key)
    {
        if (json is null) return null;
        using var document = JsonDocument.Parse(json);
        var value = document.RootElement;
        if (key is not null && (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(key, out value))) return null;
        return value.ValueKind is JsonValueKind.String ? value.GetString()
            : value.ValueKind is JsonValueKind.Number ? value.GetRawText() : null;
    }
}
