using System.Text.Json;
using Wida.Bll.Dtos.Processing;
using Wida.Bll.Exceptions;
using Wida.Bll.Services.Interfaces;
using Wida.Dal.Entities;
using Wida.Dal.Enums;
using Wida.Dal.Repositories.Interfaces;
using Wida.Dal.Services.Interfaces;

namespace Wida.Bll.Services.Implementations;

public class ProcessingService : IProcessingService
{
    private readonly IProcessingRunRepository _processingRunRepository;
    private readonly IDocumentRepository _documentRepository;
    private readonly IDocumentAnalyzer _documentAnalyzer;

    public ProcessingService(
        IProcessingRunRepository processingRunRepository,
        IDocumentRepository documentRepository,
        IDocumentAnalyzer documentAnalyzer)
    {
        _processingRunRepository = processingRunRepository;
        _documentRepository = documentRepository;
        _documentAnalyzer = documentAnalyzer;
    }

    public async Task<ProcessingRunResponse> CreateAsync(
        Guid documentId,
        string processor,
        string? processorVersion = null,
        CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(
            documentId,
            cancellationToken);

        if (document is null)
        {
            throw new DocumentNotFoundException(documentId);
        }

        var processingRun = new ProcessingRun
        {
            DocumentId = documentId,
            Processor = processor,
            ProcessorVersion = processorVersion,
            Status = ProcessingStatus.Pending,
            StartedAt = DateTime.UtcNow
        };

        await _processingRunRepository.AddAsync(
            processingRun,
            cancellationToken);

        await _processingRunRepository.SaveChangesAsync(
            cancellationToken);

        return Map(processingRun);
    }

    public async Task<ProcessingRunResponse?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var run = await _processingRunRepository.GetByIdAsync(
            id,
            cancellationToken);

        return run is null
            ? null
            : Map(run);
    }

    public async Task<IReadOnlyList<ProcessingRunResponse>> GetByDocumentIdAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var runs = await _processingRunRepository.GetByDocumentIdAsync(
            documentId,
            cancellationToken);

        return runs
            .Select(Map)
            .ToList();
    }

    public async Task<ProcessingRunResponse> ProcessInvoiceAsync(
        Guid documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _documentRepository.GetByIdAsync(
            documentId,
            cancellationToken);

        if (document is null)
        {
            throw new DocumentNotFoundException(documentId);
        }

        var processingRun = new ProcessingRun
        {
            DocumentId = documentId,
            Processor = "AzureDocumentIntelligence",
            ProcessorVersion = "prebuilt-invoice",
            Status = ProcessingStatus.Running,
            StartedAt = DateTime.UtcNow
        };

        var preserveSaved = document.Status == DocumentStatus.Saved;
        document.DocumentType = DocumentType.Invoice;
        if (!preserveSaved) document.Status = DocumentStatus.Processing;
        document.UpdatedAt = DateTime.UtcNow;

        await _processingRunRepository.AddAsync(
            processingRun,
            cancellationToken);

        await _processingRunRepository.SaveChangesAsync(
            cancellationToken);

        try
        {
            var result =
                await _documentAnalyzer.AnalyzeInvoiceAsync(
                    document.StoragePath,
                    cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            ApplyResult(processingRun, result, _processingRunRepository.AddExtractedFields);
            if (!preserveSaved) document.Status = DocumentStatus.ReviewRequired;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            MarkFailed(processingRun, "DOCUMENT_ANALYSIS_CANCELLED", "Document analysis was cancelled.");
            if (!preserveSaved) document.Status = DocumentStatus.Failed;
            document.UpdatedAt = DateTime.UtcNow;
            await SaveTerminalStateAsync();
            throw;
        }
        catch (Exception ex)
        {
            MarkFailed(processingRun, "DOCUMENT_ANALYSIS_FAILED", ex.Message);
            if (!preserveSaved) document.Status = DocumentStatus.Failed;
        }

        document.UpdatedAt = DateTime.UtcNow;
        await SaveTerminalStateAsync();

        return Map(processingRun);
    }

    internal static void ApplyResult(ProcessingRun processingRun, Wida.Dal.Models.DocumentAnalysisResult result,
        Action<IEnumerable<ExtractedField>> addFields)
    {
        if (result.Fields.Any(field => field.RawValue?.Length > 4000))
        {
            throw new InvalidOperationException(
                "An extracted field exceeds the supported raw text length of 4000 characters.");
        }

        var extractedFields = result.Fields
            .Select(field => new ExtractedField
            {
                ProcessingRunId = processingRun.Id,
                FieldName = field.Name,
                RawValue = field.RawValue,
                NormalizedValue = field.NormalizedValue?.GetRawText(),
                Confidence = field.Confidence,
                Source = ExtractionSource.DocumentIntelligence,
                PageNumber = field.PageNumber,
                BoundingBox = field.BoundingBox?.GetRawText(),
                RequiresReview =
                    field.Confidence is null ||
                    field.Confidence < 0.80m
            })
            .ToList();

        addFields(extractedFields);
        processingRun.ExtractedFields = extractedFields;
        processingRun.RawResult = result.RawResult;
        processingRun.Status = ProcessingStatus.Completed;
        processingRun.CompletedAt = DateTime.UtcNow;
    }

    private async Task SaveTerminalStateAsync()
    {
        // A disconnected caller must not prevent an already persisted run from finishing.
        using var persistenceTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await _processingRunRepository.SaveChangesAsync(persistenceTimeout.Token);
    }

    private static void MarkFailed(ProcessingRun run, string errorCode, string errorMessage)
    {
        run.Status = ProcessingStatus.Failed;
        run.ErrorCode = errorCode;
        run.ErrorMessage = errorMessage.Length > 2000 ? errorMessage[..2000] : errorMessage;
        run.CompletedAt = DateTime.UtcNow;
    }

    private static JsonElement? ParseJson(string? value)
    {
        return value is null ? null : JsonSerializer.Deserialize<JsonElement>(value);
    }

    internal static ProcessingRunResponse Map(
        ProcessingRun processingRun)
    {
        return new ProcessingRunResponse(
            processingRun.Id,
            processingRun.DocumentId,
            processingRun.Status,
            processingRun.Processor,
            processingRun.ProcessorVersion,
            processingRun.StartedAt,
            processingRun.CompletedAt,
            processingRun.ErrorCode,
            processingRun.ErrorMessage,
            processingRun.ExtractedFields
                .OrderBy(field => field.FieldName)
                .Select(field => new ExtractedFieldResponse(
                    field.Id,
                    field.FieldName,
                    field.RawValue,
                    ParseJson(field.NormalizedValue),
                    field.Confidence,
                    field.Source,
                    field.PageNumber,
                    ParseJson(field.BoundingBox),
                    field.RequiresReview))
                .ToList()
        );
    }
}
