using Microsoft.AspNetCore.Mvc;
using Wida.Bll.Services.Interfaces;

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

    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> Upload(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return BadRequest("The uploaded file is empty.");
        }

        var uploadsPath = Path.Combine(
            _environment.ContentRootPath,
            "uploads");

        Directory.CreateDirectory(uploadsPath);

        var storedFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";

        var physicalPath = Path.Combine(
            uploadsPath,
            storedFileName);

        try
        {
            await using (var stream = System.IO.File.Create(physicalPath))
            {
                await file.CopyToAsync(stream, cancellationToken);
            }

            var document = await _documentService.CreateAsync(
                file.FileName,
                file.ContentType,
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
}
