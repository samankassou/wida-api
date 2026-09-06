using Microsoft.AspNetCore.Mvc;
using Wida.Bll.Dtos.Invoices;
using Wida.Bll.Services.Interfaces;

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
    public async Task<IActionResult> Create(
        CreateInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        var invoice = await _invoiceService.CreateAsync(
            request,
            cancellationToken);

        return CreatedAtAction(
            nameof(GetById),
            new { id = invoice.Id },
            invoice);
    }
}