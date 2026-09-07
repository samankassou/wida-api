using Microsoft.AspNetCore.Mvc;
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
    [RequestSizeLimit(DocumentFilePolicy.MaximumRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = DocumentFilePolicy.MaximumRequestBytes)]
    public async Task<IActionResult> Upload(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return FileError("The uploaded file is empty.");
        }
        if (file.Length > DocumentFilePolicy.MaximumBytes)
            return FileError("Choose a file no larger than 20 MiB.", StatusCodes.Status413PayloadTooLarge);
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
                if (stream.Length > DocumentFilePolicy.MaximumBytes)
                {
                    await stream.DisposeAsync();
                    System.IO.File.Delete(physicalPath);
                    return FileError("Choose a file no larger than 20 MiB.", StatusCodes.Status413PayloadTooLarge);
                }
                stream.Position = 0;
                if (!await DocumentFilePolicy.HasMatchingSignatureAsync(stream, contentType, cancellationToken))
                {
                    await stream.DisposeAsync();
                    System.IO.File.Delete(physicalPath);
                    return FileError("The file contents do not match its PDF or image format.");
                }
            }

            var document = await _documentService.CreateAsync(
                fileName,
                contentType,
                physicalPath,
                cancellationToken);

            return CreatedAtAction(
                nameof(GetById),
                new { id = document.Id },
                document);
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
