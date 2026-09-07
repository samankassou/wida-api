using Microsoft.AspNetCore.Mvc;
using Wida.Bll.Dtos.Invoices;
using Wida.Bll.Services.Interfaces;
using Wida.Bll.Exceptions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Wida.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InvoicesController : ControllerBase
{
    private readonly IInvoiceService _invoiceService;

    public InvoicesController(IInvoiceService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var invoice = await _invoiceService.GetByIdAsync(
            id,
            cancellationToken);

        if (invoice is null)
        {
            return NotFound();
        }

        return Ok(invoice);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken) =>
        Ok(await _invoiceService.GetAllAsync(cancellationToken));

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, CreateInvoiceRequest request, CancellationToken cancellationToken) =>
        WithInvoiceErrors(async () => Ok(await _invoiceService.UpdateAsync(id, request, cancellationToken)));

    [HttpGet("document/{documentId:guid}")]
    public async Task<IActionResult> GetByDocumentId(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var invoice = await _invoiceService.GetByDocumentIdAsync(
            documentId,
            cancellationToken);

        if (invoice is null)
        {
            return NotFound();
        }

        return Ok(invoice);
    }

    [HttpPost]
    public Task<IActionResult> Create(
        CreateInvoiceRequest request,
        CancellationToken cancellationToken)
        => WithInvoiceErrors(async () =>
    {
        var invoice = await _invoiceService.CreateAsync(
            request,
            cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = invoice.Id },
            invoice);
    });

    private async Task<IActionResult> WithInvoiceErrors(Func<Task<IActionResult>> action)
    {
        try { return await action(); }
        catch (InvoiceValidationException exception)
        {
            var result = BadRequest(new ValidationProblemDetails(exception.Errors.ToDictionary())
            {
                Status = StatusCodes.Status400BadRequest,
                Title = exception.Message
            });
            result.ContentTypes.Add("application/problem+json");
            return result;
        }
        catch (DocumentNotFoundException exception) { return Problem(statusCode: 404, detail: exception.Message); }
        catch (InvoiceNotFoundException exception) { return Problem(statusCode: 404, detail: exception.Message); }
        catch (InvoiceConflictException exception) { return Problem(statusCode: 409, detail: exception.Message); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_Invoices_DocumentId" })
        {
            return Problem(statusCode: 409, detail: "An invoice already exists for this document.");
        }
    }
}
