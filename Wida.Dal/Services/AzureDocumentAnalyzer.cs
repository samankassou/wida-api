using System.ClientModel.Primitives;
using System.Globalization;
using System.Text.Json;
using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Configuration;
using Wida.Dal.Models;
using Wida.Dal.Services.Interfaces;

namespace Wida.Dal.Services;

public class AzureDocumentAnalyzer : IDocumentAnalyzer
{
    private static readonly string[] InvoiceFields =
    [
        "InvoiceId", "InvoiceDate", "DueDate", "VendorName",
        "SubTotal", "TotalTax", "InvoiceTotal"
    ];

    private readonly IConfiguration _configuration;
    private DocumentIntelligenceClient? _client;

    public AzureDocumentAnalyzer(
        IConfiguration configuration,
        DocumentIntelligenceClient? client = null)
    {
        _configuration = configuration;
        _client = client;
    }

    public async Task<DocumentAnalysisResult> AnalyzeInvoiceAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Resolve Azure credentials only for OCR requests; manual processing and
        // reading stored results must remain available without Azure configuration.
        var client = _client ??= CreateClient();
        var fileBytes = await File.ReadAllBytesAsync(filePath, cancellationToken);

        var operation = await client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            "prebuilt-invoice",
            BinaryData.FromBytes(fileBytes),
            cancellationToken);

        var result = operation.Value;

        if (result.Documents.Count == 0)
        {
            throw new InvalidOperationException(
                "Azure Document Intelligence returned no analyzed invoice documents.");
        }

        var analysis = new DocumentAnalysisResult
        {
            RawResult = ModelReaderWriter.Write(result).ToString()
        };

        var document = result.Documents[0];
        foreach (var fieldName in InvoiceFields)
        {
            AddField(document, analysis, fieldName);
        }

        return analysis;
    }

    private DocumentIntelligenceClient CreateClient()
    {
        var endpoint = _configuration["AzureDocumentIntelligence:Endpoint"];
        var key = _configuration["AzureDocumentIntelligence:Key"];

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException(
                "Azure Document Intelligence endpoint is missing.");
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)
            || (endpointUri.Scheme != Uri.UriSchemeHttps
                && endpointUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException(
                "Azure Document Intelligence endpoint must be an absolute HTTP or HTTPS URL.");
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "Azure Document Intelligence key is missing.");
        }

        return new DocumentIntelligenceClient(endpointUri, new AzureKeyCredential(key));
    }

    private static void AddField(
        AnalyzedDocument document,
        DocumentAnalysisResult result,
        string fieldName)
    {
        if (!document.Fields.TryGetValue(fieldName, out var field))
        {
            return;
        }

        BoundingRegion? region = field.BoundingRegions.Count == 0
            ? null
            : field.BoundingRegions[0];
        result.Fields.Add(new AnalyzedField
        {
            Name = fieldName,
            RawValue = field.Content,
            NormalizedValue = GetNormalizedValue(field),
            Confidence = field.Confidence.HasValue
                ? Convert.ToDecimal(field.Confidence.Value)
                : null,
            PageNumber = region?.PageNumber,
            BoundingBox = region is null
                ? null
                : JsonSerializer.SerializeToElement(region.Value.Polygon)
        });
    }

    private static JsonElement? GetNormalizedValue(DocumentField field)
    {
        if (field.FieldType == DocumentFieldType.Currency)
        {
            if (field.ValueCurrency is null)
            {
                return null;
            }

            using var currency = JsonDocument.Parse(ModelReaderWriter.Write(field.ValueCurrency).ToMemory());
            return currency.RootElement.Clone();
        }

        object? value = field.FieldType switch
        {
            var type when type == DocumentFieldType.String => field.ValueString,
            var type when type == DocumentFieldType.Date =>
                field.ValueDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            var type when type == DocumentFieldType.Double => field.ValueDouble,
            var type when type == DocumentFieldType.Int64 => field.ValueInt64,
            _ => field.Content
        };

        return value is null ? null : JsonSerializer.SerializeToElement(value);
    }
}
