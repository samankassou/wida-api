using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Wida.Api.Controllers;
using Wida.Bll.Dtos.Processing;
using Wida.Bll.Exceptions;
using Wida.Bll.Services.Interfaces;

namespace Wida.Tests;

public sealed class ProcessingControllerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProcessingAnUnknownDocument_ReturnsNotFound(bool processInvoice)
    {
        var documentId = Guid.NewGuid();
        var controller = new ProcessingController(new MissingDocumentProcessingService());

        var result = processInvoice
            ? await controller.ProcessInvoice(documentId, CancellationToken.None)
            : await controller.Create(documentId, CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Contains(documentId.ToString(), problem.Detail);
    }

    private sealed class MissingDocumentProcessingService : IProcessingService
    {
        public Task<ProcessingRunResponse> CreateAsync(
            Guid documentId, string processor, string? processorVersion = null,
            CancellationToken cancellationToken = default) =>
            throw new DocumentNotFoundException(documentId);

        public Task<ProcessingRunResponse> ProcessInvoiceAsync(
            Guid documentId, CancellationToken cancellationToken = default) =>
            throw new DocumentNotFoundException(documentId);

        public Task<ProcessingRunResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ProcessingRunResponse>> GetByDocumentIdAsync(
            Guid documentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
