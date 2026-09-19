using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using Wida.Dal.Persistence;
using Wida.Bll.Exceptions;
using Wida.Bll.Services.Interfaces;
using Wida.Api.Files;
using Microsoft.Net.Http.Headers;
using Wida.Dal.Storage;

namespace Wida.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly IWebHostEnvironment _environment;
    private readonly IDocumentStorage _storage;

    public DocumentsController(
        IDocumentService documentService,
        IWebHostEnvironment environment, IDocumentStorage? storage = null)
    {
        _documentService = documentService;
        _environment = environment;
        _storage = storage ?? new LocalDocumentStorage(Path.Combine(environment.ContentRootPath, "uploads"));
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(
        CancellationToken cancellationToken)
    {
        var documents = await _documentService.GetAllAsync(cancellationToken);

        return Ok(documents);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var document = await _documentService.GetByIdAsync(
            id,
            cancellationToken);

        if (document is null)
        {
            return NotFound();
        }

        return Ok(document);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var db = HttpContext.RequestServices.GetRequiredService<WidaDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // Serialize deletion with uploads and analysis admission across API replicas.
        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73190421)", cancellationToken);
        var document = await db.Documents.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (document is null) return NotFound();
        if (await db.ProcessingRuns.AnyAsync(x => x.DocumentId == id &&
            (x.Status == Wida.Dal.Enums.ProcessingStatus.Pending || x.Status == Wida.Dal.Enums.ProcessingStatus.Running), cancellationToken))
            return Problem(statusCode: 409, detail: "Wait for the analysis to finish before deleting this document.");

        // Database cascades remove invoices, lines, runs and extracted fields.
        db.Documents.Remove(document);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        // Never delete a file before its metadata deletion has committed. Finish cleanup
        // even if the browser disconnects after the commit.
        try { await _storage.DeleteAsync(document.StoragePath, CancellationToken.None); }
        catch (Exception ex)
        {
            HttpContext.RequestServices.GetService<ILogger<DocumentsController>>()?
                .LogWarning(ex, "Could not remove deleted original {DocumentId} at {StoragePath}.", id, document.StoragePath);
        }
        return NoContent();
    }

    [HttpGet("workspace/page")]
    public async Task<IActionResult> GetWorkspacePage([FromQuery] Wida.Bll.Dtos.Documents.WorkspaceQuery query,
        [FromServices] Wida.Bll.Services.Implementations.WorkspaceService workspace, CancellationToken cancellationToken)
        => Ok(await workspace.GetPageAsync(query, cancellationToken));

    [HttpGet("workspace/{id:guid}")]
    public async Task<IActionResult> GetWorkspaceItem(Guid id,
        [FromServices] Wida.Bll.Services.Implementations.WorkspaceService workspace, CancellationToken cancellationToken)
    {
        var item = await workspace.GetItemAsync(id, cancellationToken);
        return item is null ? NotFound() : Ok(item);
    }

    [HttpGet("workspace")]
    public async Task<IActionResult> GetWorkspace(CancellationToken cancellationToken, [FromQuery] int limit = 100)
    {
        if (limit is < 1 or > 500)
            return BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
            {
                ["limit"] = ["Choose a limit between 1 and 500."]
            }) { Status = StatusCodes.Status400BadRequest });
        return Ok(await _documentService.GetWorkspaceAsync(limit, cancellationToken));
    }

    [HttpGet("{id:guid}/content")]
    public async Task<IActionResult> GetContent(Guid id, CancellationToken cancellationToken,
        [FromQuery] bool download = false)
    {
        var record = await _documentService.GetByIdAsync(id, cancellationToken);
        if (!User.IsInRole("Admin") && record is not null && record.UploadedAt <= DateTime.UtcNow.AddDays(-30))
            return Problem(statusCode: 410, detail: "L’original a expiré après 30 jours. Les données de la facture restent disponibles.");
        var content = await _documentService.GetContentAsync(id, cancellationToken);
        if (content is null) return NotFound();
        var name = DocumentFilePolicy.SafeName(content.OriginalFileName);
        var contentType = DocumentFilePolicy.ContentTypeFor(name);
        if (contentType is null) return NotFound();
        Stream stream;
        try
        {
            stream = await _storage.OpenReadAsync(content.StoragePath, cancellationToken);
        }
        catch (FileNotFoundException) { return NotFound(); }
        catch (DirectoryNotFoundException) { return NotFound(); }

        try
        {
            if (!await DocumentFilePolicy.HasMatchingSignatureAsync(stream, contentType, cancellationToken))
            {
                await stream.DisposeAsync();
                return NotFound();
            }
            var disposition = new ContentDispositionHeaderValue(download ? "attachment" : "inline");
            disposition.SetHttpFileName(name);
            Response.Headers.ContentDisposition = disposition.ToString();
            Response.Headers.XContentTypeOptions = "nosniff";
            Response.Headers.CacheControl = "private, no-store";
            return new FileStreamResult(stream, contentType) { EnableRangeProcessing = true };
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [TypeFilter(typeof(UploadAdmissionFilter))]
    public async Task<IActionResult> Upload(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return FileError("The uploaded file is empty.");
        }
        if (!User.IsInRole("Admin") && file.Length > DocumentFilePolicy.MaximumBytes)
            return FileError("Choose a file no larger than 4 MiB.", StatusCodes.Status413PayloadTooLarge);
        var fileName = DocumentFilePolicy.SafeName(file.FileName);
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 255)
            return FileError("Use a filename between 1 and 255 characters.");
        var contentType = DocumentFilePolicy.ContentTypeFor(fileName);
        if (contentType is null)
            return FileError("Choose a PDF, PNG, JPEG, or TIFF file.");
        if (!string.IsNullOrWhiteSpace(file.ContentType)
            && file.ContentType != "application/octet-stream"
            && !string.Equals(file.ContentType, contentType, StringComparison.OrdinalIgnoreCase))
            return FileError("The file extension and content type do not match.");

        var uploadsPath = Path.Combine(
            _environment.ContentRootPath,
            "uploads");

        Directory.CreateDirectory(uploadsPath);

        var storedFileName = $"{Guid.NewGuid()}{Path.GetExtension(fileName).ToLowerInvariant()}";

        var physicalPath = Path.Combine(
            uploadsPath,
            storedFileName);

        string? storedLocation = null;
        bool commitAttempted = false;
        try
        {
            await using (var stream = System.IO.File.Create(physicalPath))
            {
                await file.CopyToAsync(stream, cancellationToken);
                if (!User.IsInRole("Admin") && stream.Length > DocumentFilePolicy.MaximumBytes)
                {
                    await stream.DisposeAsync();
                    System.IO.File.Delete(physicalPath);
                    return FileError("Choose a file no larger than 4 MiB.", StatusCodes.Status413PayloadTooLarge);
                }
                stream.Position = 0;
                if (!await DocumentFilePolicy.HasMatchingSignatureAsync(stream, contentType, cancellationToken))
                {
                    await stream.DisposeAsync();
                    System.IO.File.Delete(physicalPath);
                    return FileError("The file contents do not match its PDF or image format.");
                }
            }

            var bytes = await System.IO.File.ReadAllBytesAsync(physicalPath, cancellationToken);
            int pages;
            try { pages = TrialFileInspector.CountPages(bytes, contentType); }
            catch (Exception ex) when (ex is not OperationCanceledException and not OutOfMemoryException)
            {
                System.IO.File.Delete(physicalPath);
                return FileError("Le fichier est illisible ou protégé. Choisissez un PDF ou une image valide.");
            }
            if (pages < 1 || (!User.IsInRole("Admin") && pages > 2))
            {
                System.IO.File.Delete(physicalPath);
                return FileError("Deux pages maximum par document. Séparez votre fichier avant de l’importer.");
            }
            var db = HttpContext.RequestServices.GetRequiredService<WidaDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            if (db.Database.IsNpgsql())
                await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(73190421)", cancellationToken);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var duplicate = await db.Documents.SingleOrDefaultAsync(x => x.ContentHash == hash, cancellationToken);
            if (duplicate is not null && (User.IsInRole("Admin") || duplicate.UploadedAt > DateTime.UtcNow.AddDays(-30))
                && await _storage.ExistsAsync(duplicate.StoragePath, cancellationToken))
            {
                System.IO.File.Delete(physicalPath);
                var existingResponse = await _documentService.GetByIdAsync(duplicate.Id, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Ok(existingResponse! with { IsDuplicate = true });
            }
            if (duplicate is null && !User.IsInRole("Admin") && await db.Documents.CountAsync(cancellationToken) >= 10)
                throw new TrialLimitException("La bêta est limitée à 10 documents par compte.");
            storedLocation = await _storage.PersistAsync(physicalPath, contentType, cancellationToken);
            if (duplicate is not null)
            {
                // Never delete the previous original before the metadata replacement commits.
                var expiredPath = duplicate.StoragePath;
                duplicate.StoragePath = storedLocation;
                duplicate.UploadedAt = DateTime.UtcNow;
                duplicate.PageCount = pages;
                duplicate.SizeBytes = bytes.LongLength;
                await db.SaveChangesAsync(cancellationToken);
                var existingResponse = await _documentService.GetByIdAsync(duplicate.Id, cancellationToken);
                commitAttempted = true;
                await transaction.CommitAsync(cancellationToken);
                try { await _storage.DeleteAsync(expiredPath, cancellationToken); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { HttpContext.RequestServices.GetService<ILogger<DocumentsController>>()?.LogWarning("Could not remove replaced original {DocumentId}.", duplicate.Id); }
                return Ok(existingResponse! with { IsDuplicate = true, OriginalRestored = true });
            }
            var document = await _documentService.CreateAsync(
                fileName,
                contentType,
                storedLocation,
                cancellationToken);

            var stored = await db.Documents.SingleAsync(x => x.Id == document.Id, cancellationToken);
            stored.PageCount = pages; stored.ContentHash = hash; stored.SizeBytes = bytes.LongLength;
            await db.SaveChangesAsync(cancellationToken);
            var response = await _documentService.GetByIdAsync(document.Id, cancellationToken);
            commitAttempted = true;
            await transaction.CommitAsync(cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new { id = document.Id },
                response);
        }
        catch
        {
            // A failed commit acknowledgement may still mean committed metadata. Keep the
            // original in that case; reconcile orphans instead of deleting referenced data.
            if (!commitAttempted)
            {
                if (storedLocation is not null)
                {
                    try { await _storage.DeleteAsync(storedLocation, CancellationToken.None); }
                    catch { /* Preserve the original failure; orphan cleanup is an operator task. */ }
                }
                System.IO.File.Delete(physicalPath);
            }
            throw;
        }
        finally
        {
            if (storedLocation is not null && storedLocation != physicalPath)
                System.IO.File.Delete(physicalPath);
        }
    }

    private ObjectResult FileError(string message, int status = StatusCodes.Status400BadRequest)
    {
        var problem = new ValidationProblemDetails(new Dictionary<string, string[]> { ["file"] = [message] })
        {
            Status = status,
            Title = "The file could not be uploaded."
        };
        var result = status == StatusCodes.Status400BadRequest ? BadRequest(problem) : StatusCode(status, problem);
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}
