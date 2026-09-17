using System.Text.RegularExpressions;

namespace Wida.Dal.Models;

public static class InvoiceIdentity
{
    // Preserve punctuation and accents: different invoice numbering schemes must
    // not be collapsed into a hard uniqueness rule.
    public static string Normalize(string? value) =>
        Regex.Replace(value?.Trim() ?? "", @"\s+", " ").ToUpperInvariant();
}
