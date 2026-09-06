using Microsoft.AspNetCore.Mvc;
using Wida.Bll.Services.Interfaces;

namespace Wida.Api.Controllers;

[ApiController]
[Route("api/processing")]
public class ProcessingController : ControllerBase
{
    private readonly IProcessingService _processingService;

    public ProcessingController(
        IProcessingService processingService)
    {
        _processingService = processingService;
    }

    [HttpPost("documents/{documentId:guid}")]
    public async Task<IActionResult> Create(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var run = await _processingService.CreateAsync(
            documentId,
            processor: "Manual",
            processorVersion: "v1",
            cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = run.Id },
            run);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var run = await _processingService.GetByIdAsync(
            id,
            cancellationToken);

        if (run is null)
        {
            return NotFound();
        }

        return Ok(run);
    }

    [HttpGet("documents/{documentId:guid}")]
    public async Task<IActionResult> GetByDocumentId(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var runs = await _processingService.GetByDocumentIdAsync(
            documentId,
            cancellationToken);

        return Ok(runs);
    }
}