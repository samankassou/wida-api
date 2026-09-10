using System.ClientModel.Primitives;
using System.Text.Json;
using Azure;
using Azure.AI.DocumentIntelligence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wida.Dal;
using Wida.Dal.Services;
using Wida.Dal.Services.Interfaces;
using Xunit;

namespace Wida.Tests;

public class AzureDocumentAnalyzerTests
{
    [Fact]
    public async Task AnalyzeInvoiceAsync_MapsTypedHeaderFieldsAndKeepsRawSdkResult()
    {
        var sdkResult = ModelReaderWriter.Read<AnalyzeResult>(BinaryData.FromString("""
            {
              "apiVersion": "2024-11-30",
              "modelId": "prebuilt-invoice",
              "stringIndexType": "utf16CodeUnit",
              "content": "Invoice INV-42",
              "pages": [],
              "documents": [
                {
                  "docType": "invoice",
                  "confidence": 0.99,
                  "spans": [],
                  "fields": {
                    "InvoiceId": {
                      "type": "string", "valueString": "INV-42", "content": "Invoice INV-42",
                      "confidence": 0.98,
                      "boundingRegions": [
                        { "pageNumber": 2, "polygon": [1, 2, 3, 2, 3, 4, 1, 4] },
                        { "pageNumber": 3, "polygon": [5, 6, 7, 6, 7, 8, 5, 8] }
                      ]
                    },
                    "InvoiceDate": { "type": "date", "valueDate": "2026-09-07", "content": "7 Sept 2026" },
                    "DueDate": { "type": "date", "valueDate": "2026-10-07", "content": "7 Oct 2026" },
                    "VendorName": { "type": "string", "valueString": "Example Vendor", "content": "Example Vendor" },
                    "SubTotal": { "type": "number", "valueNumber": 100.5, "content": "100.50" },
                    "TotalTax": { "type": "integer", "valueInteger": 20, "content": "20" },
                    "InvoiceTotal": {
                      "type": "currency", "content": "$120.50",
                      "valueCurrency": { "amount": 120.5, "currencySymbol": "$", "currencyCode": "USD" }
                    },
                    "TotalDiscount": { "type": "currency", "content": "$5", "valueCurrency": { "amount": 5 } },
                    "CustomerName": { "type": "string", "valueString": "Unselected customer" }
                  }
                },
                {
                  "docType": "invoice", "confidence": 0.9,
                  "spans": [],
                  "fields": { "InvoiceId": { "type": "string", "valueString": "SECOND-INVOICE" } }
                }
              ]
            }
            """))!;
        var client = new FakeDocumentIntelligenceClient(sdkResult);
        var analyzer = new AzureDocumentAnalyzer(new ConfigurationBuilder().Build(), client);
        var filePath = Path.GetTempFileName();
        using var cancellation = new CancellationTokenSource();

        try
        {
            await File.WriteAllTextAsync(filePath, "invoice bytes");
            var result = await analyzer.AnalyzeInvoiceAsync(filePath, cancellation.Token);

            Assert.Equal("prebuilt-invoice", client.ModelId);
            Assert.Equal(WaitUntil.Completed, client.WaitUntil);
            Assert.Equal("invoice bytes", client.DocumentBytes?.ToString());
            Assert.Equal(cancellation.Token, client.CancellationToken);
            Assert.Equal(
                ["InvoiceId", "InvoiceDate", "DueDate", "VendorName", "SubTotal", "TotalTax", "InvoiceTotal", "TotalDiscount"],
                result.Fields.Select(field => field.Name));

            var fields = result.Fields.ToDictionary(field => field.Name);
            Assert.Equal("INV-42", fields["InvoiceId"].NormalizedValue!.Value.GetString());
            Assert.Equal("Invoice INV-42", fields["InvoiceId"].RawValue);
            Assert.Equal(0.98m, fields["InvoiceId"].Confidence);
            Assert.Equal(2, fields["InvoiceId"].PageNumber);
            Assert.Equal(
                new float[] { 1, 2, 3, 2, 3, 4, 1, 4 },
                fields["InvoiceId"].BoundingBox!.Value.EnumerateArray().Select(point => point.GetSingle()));
            Assert.Equal("2026-09-07", fields["InvoiceDate"].NormalizedValue!.Value.GetString());
            Assert.Equal("2026-10-07", fields["DueDate"].NormalizedValue!.Value.GetString());
            Assert.Equal("Example Vendor", fields["VendorName"].NormalizedValue!.Value.GetString());
            Assert.Null(fields["VendorName"].Confidence);
            Assert.Null(fields["VendorName"].PageNumber);
            Assert.Null(fields["VendorName"].BoundingBox);
            Assert.Equal(100.5, fields["SubTotal"].NormalizedValue!.Value.GetDouble());
            Assert.Equal(20, fields["TotalTax"].NormalizedValue!.Value.GetInt64());
            Assert.Equal(5, fields["TotalDiscount"].NormalizedValue!.Value.GetProperty("amount").GetDouble());
            var currency = fields["InvoiceTotal"].NormalizedValue!.Value;
            Assert.Equal(JsonValueKind.Object, currency.ValueKind);
            Assert.Equal(120.5, currency.GetProperty("amount").GetDouble());
            Assert.Equal("USD", currency.GetProperty("currencyCode").GetString());
            Assert.Equal("$", currency.GetProperty("currencySymbol").GetString());

            using var raw = JsonDocument.Parse(result.RawResult);
            Assert.Equal("prebuilt-invoice", raw.RootElement.GetProperty("modelId").GetString());
            Assert.Equal(2, raw.RootElement.GetProperty("documents").GetArrayLength());
            Assert.Equal("Unselected customer", raw.RootElement.GetProperty("documents")[0]
                .GetProperty("fields").GetProperty("CustomerName").GetProperty("valueString").GetString());
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task AnalyzeInvoiceAsync_OmitsAbsentFieldsAndPreservesMissingNormalizedValues()
    {
        var sdkResult = ModelReaderWriter.Read<AnalyzeResult>(BinaryData.FromString("""
            {
              "apiVersion": "2024-11-30", "modelId": "prebuilt-invoice", "content": "",
              "pages": [],
              "documents": [{ "docType": "invoice", "confidence": 0.5, "spans": [], "fields": {
                "InvoiceDate": { "type": "date", "content": "unreadable date" },
                "InvoiceTotal": { "type": "currency", "content": "unreadable total" }
              }}]
            }
            """))!;
        var analyzer = new AzureDocumentAnalyzer(
            new ConfigurationBuilder().Build(), new FakeDocumentIntelligenceClient(sdkResult));
        var filePath = Path.GetTempFileName();

        try
        {
            var result = await analyzer.AnalyzeInvoiceAsync(filePath);

            Assert.Equal(["InvoiceDate", "InvoiceTotal"], result.Fields.Select(field => field.Name));
            Assert.All(result.Fields, field => Assert.Null(field.NormalizedValue));
            Assert.Equal("unreadable date", result.Fields[0].RawValue);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Fact]
    public async Task AnalyzeInvoiceAsync_ExtractsLineFieldsWithTypesOrderAndSourceMetadata()
    {
        var sdkResult = ModelReaderWriter.Read<AnalyzeResult>(BinaryData.FromString("""
            {
              "apiVersion": "2024-11-30", "modelId": "prebuilt-invoice", "content": "",
              "pages": [], "documents": [{ "docType": "invoice", "spans": [], "fields": {
                "Items": { "type": "array", "valueArray": [
                  { "type": "object", "valueObject": {
                    "Description": { "type": "string", "valueString": "Consulting", "content": "Consulting", "confidence": 0.7,
                      "boundingRegions": [{ "pageNumber": 2, "polygon": [1, 2, 3, 2, 3, 4, 1, 4] }] },
                    "Quantity": { "type": "number", "valueNumber": 2.5 },
                    "Unit": { "type": "string", "valueString": "hours" },
                    "UnitPrice": { "type": "currency", "valueCurrency": { "amount": 100, "currencyCode": "EUR" } },
                    "TaxRate": { "type": "string", "valueString": "20 %" },
                    "Tax": { "type": "currency", "valueCurrency": { "amount": 50 } },
                    "Amount": { "type": "currency", "valueCurrency": { "amount": 250 } }
                  } },
                  { "type": "object", "valueObject": {} },
                  { "type": "string", "valueString": "invalid row" },
                  { "type": "object", "valueObject": {
                    "Description": { "type": "string", "content": "unreadable" },
                    "Quantity": { "type": "integer", "valueInteger": 0 }
                  } }
                ] }
              } }]
            }
            """))!;
        var analyzer = new AzureDocumentAnalyzer(new ConfigurationBuilder().Build(), new FakeDocumentIntelligenceClient(sdkResult));
        var path = Path.GetTempFileName();
        try
        {
            var result = await analyzer.AnalyzeInvoiceAsync(path);
            Assert.Equal(new[] { "Items[0].Description", "Items[0].Quantity", "Items[0].Unit", "Items[0].UnitPrice",
                "Items[0].TaxRate", "Items[0].Tax", "Items[0].Amount", "Items[3].Description", "Items[3].Quantity" },
                result.Fields.Select(field => field.Name));
            var fields = result.Fields.ToDictionary(field => field.Name);
            Assert.Equal("Consulting", fields["Items[0].Description"].NormalizedValue!.Value.GetString());
            Assert.Equal("Consulting", fields["Items[0].Description"].RawValue);
            Assert.Equal(0.7m, fields["Items[0].Description"].Confidence);
            Assert.Equal(2, fields["Items[0].Description"].PageNumber);
            Assert.Equal(8, fields["Items[0].Description"].BoundingBox!.Value.GetArrayLength());
            Assert.Equal(2.5, fields["Items[0].Quantity"].NormalizedValue!.Value.GetDouble());
            Assert.Equal(100, fields["Items[0].UnitPrice"].NormalizedValue!.Value.GetProperty("amount").GetDouble());
            Assert.Equal("20 %", fields["Items[0].TaxRate"].NormalizedValue!.Value.GetString());
            Assert.Equal(50, fields["Items[0].Tax"].NormalizedValue!.Value.GetProperty("amount").GetDouble());
            Assert.Null(fields["Items[3].Description"].NormalizedValue);
            Assert.Null(fields["Items[3].Description"].Confidence);
            Assert.Equal(0, fields["Items[3].Quantity"].NormalizedValue!.Value.GetInt64());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task AnalyzeInvoiceAsync_RejectsEmptyAnalysis()
    {
        var analyzer = new AzureDocumentAnalyzer(new ConfigurationBuilder().Build(),
            new FakeDocumentIntelligenceClient(DocumentIntelligenceModelFactory.AnalyzeResult(
                apiVersion: "2024-11-30", modelId: "prebuilt-invoice", content: "", documents: [])));
        var filePath = Path.GetTempFileName();

        try
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(
                () => analyzer.AnalyzeInvoiceAsync(filePath));

            Assert.Contains("no analyzed invoice documents", error.Message);
        }
        finally
        {
            File.Delete(filePath);
        }
    }

    [Theory]
    [InlineData(null, null, "endpoint is missing")]
    [InlineData("not a URL", "key", "absolute HTTP or HTTPS URL")]
    [InlineData("file:///invoice", "key", "absolute HTTP or HTTPS URL")]
    [InlineData("https://example.cognitiveservices.azure.com", null, "key is missing")]
    public async Task Configuration_IsValidatedOnlyWhenAnalysisIsRequested(
        string? endpoint, string? key, string expectedError)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["AzureDocumentIntelligence:Endpoint"] = endpoint,
                ["AzureDocumentIntelligence:Key"] = key
            }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDal(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var analyzer = scope.ServiceProvider.GetRequiredService<IDocumentAnalyzer>();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => analyzer.AnalyzeInvoiceAsync("unused-file-path"));

        Assert.Contains(expectedError, error.Message);
    }

    private sealed class FakeDocumentIntelligenceClient(AnalyzeResult result)
        : DocumentIntelligenceClient
    {
        public string? ModelId { get; private set; }
        public WaitUntil WaitUntil { get; private set; }
        public BinaryData? DocumentBytes { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public override Task<Operation<AnalyzeResult>> AnalyzeDocumentAsync(
            WaitUntil waitUntil,
            string modelId,
            BinaryData bytesSource,
            CancellationToken cancellationToken = default)
        {
            ModelId = modelId;
            WaitUntil = waitUntil;
            DocumentBytes = bytesSource;
            CancellationToken = cancellationToken;
            return Task.FromResult<Operation<AnalyzeResult>>(new CompletedAnalysisOperation(result));
        }
    }

    private sealed class CompletedAnalysisOperation(AnalyzeResult result) : Operation<AnalyzeResult>
    {
        public override string Id => "test-analysis";
        public override bool HasCompleted => true;
        public override bool HasValue => true;
        public override AnalyzeResult Value => result;
        public override Response GetRawResponse() => throw new NotSupportedException();
        public override Response UpdateStatus(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public override ValueTask<Response> UpdateStatusAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
