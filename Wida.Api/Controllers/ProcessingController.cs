using Microsoft.AspNetCore.Mvc;
using Wida.Bll.Exceptions;
using Wida.Bll.Services.Interfaces;

namespace Wida.Api.Controllers;

[ApiController]
[Route("api/processing")]
public class ProcessingController : ControllerBase
{
    private readonly IProcessingService _processingService;
    private readonly IInvoiceQueue _queue;

    public ProcessingController(
        IProcessingService processingService, IInvoiceQueue queue)
    {
        _processingService = processingService;
        _queue = queue;
    }

    [HttpPost("documents/{documentId:guid}")]
    public async Task<IActionResult> Create(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        try
        {
            var run = await _processingService.CreateAsync(
                documentId,
                processor: "Manual",
                processorVersion: "v1",
                cancellationToken);

            return CreatedAtAction(nameof(GetById), new { id = run.Id }, run);
        }
        catch (DocumentNotFoundException ex)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, detail: ex.Message);
        }
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

    [HttpPost("documents/{documentId:guid}/invoice")]
    public async Task<IActionResult> ProcessInvoice(
        Guid documentId,
        CancellationToken cancellationToken, [FromQuery] bool reanalyze = false)
    {
        try
        {
            var run = await _queue.EnqueueAsync(
                documentId,
                cancellationToken, reanalyze);

            return AcceptedAtAction(nameof(GetById), new { id = run.Id }, run);
        }
        catch (QueueUnavailableException ex)
        {
            Response.Headers.RetryAfter = "10";
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, detail: ex.Message);
        }
        catch (QueueCapacityException ex)
        {
            Response.Headers.RetryAfter = "10";
            return Problem(statusCode: StatusCodes.Status429TooManyRequests, detail: ex.Message);
        }
        catch (DocumentNotFoundException ex)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, detail: ex.Message);
        }
    }
}
