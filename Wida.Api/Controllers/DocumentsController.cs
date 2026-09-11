using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using Wida.Dal.Persistence;
using Wida.Bll.Exceptions;
using Wida.Bll.Services.Interfaces;
using Wida.Api.Files;
using Microsoft.Net.Http.Headers;

namespace Wida.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DocumentsController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly IWebHostEnvironment _environment;

    public DocumentsController(
        IDocumentService documentService,
        IWebHostEnvironment environment)
    {
        _documentService = documentService;
        _environment = environment;
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
        var uploadsPath = Path.GetFullPath(Path.Combine(_environment.ContentRootPath, "uploads"));
        string physicalPath;
        try { physicalPath = Path.GetFullPath(content.StoragePath); }
        catch (ArgumentException) { return NotFound(); }
        if (!Path.IsPathFullyQualified(content.StoragePath)
            || !string.Equals(Path.GetDirectoryName(physicalPath), uploadsPath, StringComparison.Ordinal))
            return NotFound();

        var name = DocumentFilePolicy.SafeName(content.OriginalFileName);
        var contentType = DocumentFilePolicy.ContentTypeFor(name);
        if (contentType is null) return NotFound();
        FileStream stream;
        try
        {
            var info = new FileInfo(physicalPath);
            if (!info.Exists || info.LinkTarget is not null) return NotFound();
            stream = new FileStream(physicalPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 4096, useAsync: true);
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
                && System.IO.File.Exists(duplicate.StoragePath))
            {
                System.IO.File.Delete(physicalPath);
                var existingResponse = await _documentService.GetByIdAsync(duplicate.Id, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Ok(existingResponse);
            }
            if (duplicate is not null)
            {
                // Restore only the original, retaining its invoice, history and credit ledger.
                var expiredPath = duplicate.StoragePath;
                if (Path.IsPathFullyQualified(expiredPath)
                    && Path.GetDirectoryName(Path.GetFullPath(expiredPath)) == Path.GetFullPath(uploadsPath))
                {
                    var expired = new FileInfo(expiredPath);
                    if (expired.Exists && expired.LinkTarget is null) expired.Delete();
                }
                duplicate.StoragePath = physicalPath;
                duplicate.UploadedAt = DateTime.UtcNow;
                duplicate.PageCount = pages;
                await db.SaveChangesAsync(cancellationToken);
                var existingResponse = await _documentService.GetByIdAsync(duplicate.Id, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return Ok(existingResponse);
            }
            if (!User.IsInRole("Admin") && await db.Documents.CountAsync(cancellationToken) >= 10)
                throw new TrialLimitException("La bêta est limitée à 10 documents par compte.");
            var document = await _documentService.CreateAsync(
                fileName,
                contentType,
                physicalPath,
                cancellationToken);

            var stored = await db.Documents.SingleAsync(x => x.Id == document.Id, cancellationToken);
            stored.PageCount = pages; stored.ContentHash = hash;
            await db.SaveChangesAsync(cancellationToken);
            var response = await _documentService.GetByIdAsync(document.Id, cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new { id = document.Id },
                response);
        }
        catch
        {
            System.IO.File.Delete(physicalPath);
            throw;
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
