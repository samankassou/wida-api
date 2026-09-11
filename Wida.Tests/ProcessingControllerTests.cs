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
        var controller = new ProcessingController(new MissingDocumentProcessingService(), new MissingQueue());

        var result = processInvoice
            ? await controller.ProcessInvoice(documentId, CancellationToken.None)
            : await controller.Create(documentId, CancellationToken.None);

        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, response.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal(StatusCodes.Status404NotFound, problem.Status);
        Assert.Contains(documentId.ToString(), problem.Detail);
    }

    [Fact]
    public async Task Analysis_returns_accepted_with_a_status_location()
    {
        var queue = new AcceptedQueue();
        var controller = new ProcessingController(new MissingDocumentProcessingService(), queue);
        var result = Assert.IsType<AcceptedAtActionResult>(await controller.ProcessInvoice(Guid.NewGuid(), default));
        Assert.Equal(202, result.StatusCode);
        Assert.Equal(nameof(ProcessingController.GetById), result.ActionName);
        Assert.Equal(queue.Run.Id, result.RouteValues!["id"]);
    }

    [Fact]
    public async Task Unavailable_broker_returns_503_and_retry_after()
    {
        var controller = new ProcessingController(new MissingDocumentProcessingService(), new UnavailableQueue())
        { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        var response = Assert.IsType<ObjectResult>(await controller.ProcessInvoice(Guid.NewGuid(), default));
        Assert.Equal(503, response.StatusCode);
        Assert.Equal("10", controller.Response.Headers.RetryAfter);
    }

    private sealed class UnavailableQueue : IInvoiceQueue
    {
        public Task<ProcessingRunResponse> EnqueueAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            throw new QueueUnavailableException(new IOException("Broker offline"));
    }

    private sealed class AcceptedQueue : IInvoiceQueue
    {
        public ProcessingRunResponse Run { get; } = new(Guid.NewGuid(), Guid.NewGuid(), Wida.Dal.Enums.ProcessingStatus.Pending,
            "AzureDocumentIntelligence", "prebuilt-invoice", DateTime.UtcNow, null, null, null, []);
        public Task<ProcessingRunResponse> EnqueueAsync(Guid documentId, CancellationToken cancellationToken = default) => Task.FromResult(Run);
    }

    private sealed class MissingQueue : IInvoiceQueue
    {
        public Task<ProcessingRunResponse> EnqueueAsync(Guid documentId, CancellationToken cancellationToken = default) =>
            throw new DocumentNotFoundException(documentId);
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
