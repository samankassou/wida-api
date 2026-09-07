using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Wida.Api.Controllers;
using Wida.Bll.Dtos.Documents;
using Wida.Bll.Services.Interfaces;
using Wida.Dal.Enums;
using Wida.Api.Files;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;

namespace Wida.Tests;

public sealed class DocumentsControllerTests : IDisposable
{
    private readonly string _contentRoot = Path.Combine(
        Path.GetTempPath(), "wida-upload-tests", Guid.NewGuid().ToString());

    public DocumentsControllerTests()
    {
        Directory.CreateDirectory(_contentRoot);
    }

    [Fact]
    public async Task Upload_PersistsCompleteFileAtThePathGivenToTheDocumentService()
    {
        byte[] contents = "%PDF-1.7\nexample invoice"u8.ToArray();
        using var source = new MemoryStream(contents);
        var file = new FormFile(source, 0, source.Length, "file", "invoice.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
        var document = CreateDocument();
        string? storedPath = null;
        var service = new StubDocumentService(async (fileName, contentType, path, token) =>
        {
            Assert.Equal("invoice.pdf", fileName);
            Assert.Equal("application/pdf", contentType);
            Assert.True(Path.IsPathFullyQualified(path));
            Assert.Equal(Path.Combine(_contentRoot, "uploads"), Path.GetDirectoryName(path));
            Assert.Equal(contents, await File.ReadAllBytesAsync(path, token));
            storedPath = path;
            return document;
        });

        var result = await CreateController(service).Upload(file, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Same(document, created.Value);
        Assert.Equal(nameof(DocumentsController.GetById), created.ActionName);
        Assert.Equal(document.Id, created.RouteValues!["id"]);
        Assert.Equal(storedPath, Assert.Single(Directory.GetFiles(_contentRoot, "*", SearchOption.AllDirectories)));
        Assert.Equal(1, service.CreateCalls);
    }

    [Fact]
    public async Task Upload_RemovesFileWhenSavingDocumentMetadataFails()
    {
        using var source = new MemoryStream("%PDF-1.7\nexample invoice"u8.ToArray());
        var file = new FormFile(source, 0, source.Length, "file", "invoice.pdf")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/pdf"
        };
        var failure = new InvalidOperationException("Metadata persistence failed.");
        string? storedPath = null;
        var service = new StubDocumentService((_, _, path, _) =>
        {
            Assert.True(File.Exists(path));
            storedPath = path;
            throw failure;
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateController(service).Upload(file, CancellationToken.None));

        Assert.Same(failure, exception);
        Assert.NotNull(storedPath);
        Assert.False(File.Exists(storedPath));
        Assert.Empty(Directory.GetFiles(_contentRoot, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Upload_RemovesPartialFileWhenTheCopyIsInterrupted()
    {
        var file = new InterruptedFormFile();
        var service = new StubDocumentService((_, _, _, _) => Task.FromResult(CreateDocument()));

        await Assert.ThrowsAsync<IOException>(
            () => CreateController(service).Upload(file, CancellationToken.None));

        Assert.True(file.PartialContentsWritten);
        Assert.Equal(0, service.CreateCalls);
        Assert.Empty(Directory.GetFiles(_contentRoot, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Upload_RejectsEmptyFilesWithoutSavingMetadataOrCreatingAFile()
    {
        using var source = new MemoryStream();
        var file = new FormFile(source, 0, 0, "file", "empty.pdf");
        var service = new StubDocumentService((_, _, _, _) => Task.FromResult(CreateDocument()));

        var result = await CreateController(service).Upload(file, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(0, service.CreateCalls);
        Assert.Empty(Directory.GetFiles(_contentRoot, "*", SearchOption.AllDirectories));
    }

    public void Dispose()
    {
        Directory.Delete(_contentRoot, recursive: true);
    }

    [Theory]
    [InlineData("invoice.html", "text/html", "<html>bad</html>")]
    [InlineData("invoice.pdf", "application/pdf", "<html>bad</html>")]
    [InlineData("invoice.pdf", "image/png", "%PDF-1.7")]
    public async Task Upload_rejects_unsupported_mismatched_or_disguised_files(string name, string type, string body)
    {
        using var source = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(body));
        var file = new FormFile(source, 0, source.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = type };
        var service = new StubDocumentService((_, _, _, _) => Task.FromResult(CreateDocument()));
        var result = Assert.IsType<BadRequestObjectResult>(await CreateController(service).Upload(file, default));
        Assert.Contains("file", Assert.IsType<ValidationProblemDetails>(result.Value).Errors.Keys);
        Assert.Equal(0, service.CreateCalls);
        Assert.Empty(Directory.GetFiles(_contentRoot, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Upload_rejects_oversized_files_before_copying()
    {
        using var source = new MemoryStream();
        var file = new FormFile(source, 0, DocumentFilePolicy.MaximumBytes + 1, "file", "invoice.pdf");
        var service = new StubDocumentService((_, _, _, _) => Task.FromResult(CreateDocument()));
        var result = Assert.IsType<ObjectResult>(await CreateController(service).Upload(file, default));
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, result.StatusCode);
        Assert.Equal(0, service.CreateCalls);
        Assert.Empty(Directory.GetFiles(_contentRoot, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Content_streams_original_file_inline_with_range_support_and_safe_headers()
    {
        Directory.CreateDirectory(Path.Combine(_contentRoot, "uploads"));
        var path = Path.Combine(_contentRoot, "uploads", "stored.pdf");
        await File.WriteAllBytesAsync(path, "%PDF-1.7\nexample invoice"u8.ToArray());
        var service = new StubDocumentService((_, _, _, _) => Task.FromResult(CreateDocument()))
        {
            Content = new DocumentContent("invoice.pdf", "application/pdf", path)
        };
        var controller = CreateController(service);
        controller.Request.Method = "GET";
        controller.Request.Headers.Range = "bytes=0-4";
        controller.Response.Body = new MemoryStream();

        var result = Assert.IsType<FileStreamResult>(await controller.GetContent(Guid.NewGuid(), default));
        Assert.True(result.EnableRangeProcessing);
        Assert.Equal("application/pdf", result.ContentType);
        Assert.StartsWith("inline;", controller.Response.Headers.ContentDisposition.ToString());
        Assert.Contains("invoice.pdf", controller.Response.Headers.ContentDisposition.ToString());
        Assert.Equal("nosniff", controller.Response.Headers.XContentTypeOptions.ToString());
        Assert.DoesNotContain(_contentRoot, controller.Response.Headers.ToString());

        var executor = new FileStreamResultExecutor(NullLoggerFactory.Instance);
        await executor.ExecuteAsync(controller.ControllerContext, result);
        Assert.Equal(StatusCodes.Status206PartialContent, controller.Response.StatusCode);
        Assert.Equal("bytes 0-4/24", controller.Response.Headers.ContentRange.ToString());
        Assert.Equal("%PDF-"u8.ToArray(), ((MemoryStream)controller.Response.Body).ToArray());
    }

    [Fact]
    public async Task Content_can_be_downloaded_and_missing_or_unsafe_storage_is_not_exposed()
    {
        Directory.CreateDirectory(Path.Combine(_contentRoot, "uploads"));
        var path = Path.Combine(_contentRoot, "uploads", "stored.pdf");
        await File.WriteAllBytesAsync(path, "%PDF-1.7"u8.ToArray());
        var service = new StubDocumentService((_, _, _, _) => Task.FromResult(CreateDocument()));
        var controller = CreateController(service);
        Assert.IsType<NotFoundResult>(await controller.GetContent(Guid.NewGuid(), default));
        service.Content = new DocumentContent("invoice.pdf", "application/pdf", path);
        var download = Assert.IsType<FileStreamResult>(await controller.GetContent(Guid.NewGuid(), default, download: true));
        Assert.StartsWith("attachment;", controller.Response.Headers.ContentDisposition.ToString());
        await download.FileStream.DisposeAsync();

        var outside = Path.Combine(_contentRoot, "outside.pdf");
        await File.WriteAllBytesAsync(outside, "%PDF-1.7"u8.ToArray());
        service.Content = new DocumentContent("invoice.pdf", "application/pdf", outside);
        Assert.IsType<NotFoundResult>(await controller.GetContent(Guid.NewGuid(), default));
        service.Content = new DocumentContent("invoice.pdf", "application/pdf", path + ".missing");
        Assert.IsType<NotFoundResult>(await controller.GetContent(Guid.NewGuid(), default));
        var link = Path.Combine(_contentRoot, "uploads", "link.pdf");
        File.CreateSymbolicLink(link, outside);
        service.Content = new DocumentContent("invoice.pdf", "application/pdf", link);
        Assert.IsType<NotFoundResult>(await controller.GetContent(Guid.NewGuid(), default));
    }

    private DocumentsController CreateController(IDocumentService service) =>
        new(service, new TestWebHostEnvironment { ContentRootPath = _contentRoot })
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private static DocumentResponse CreateDocument() => new(
        Guid.NewGuid(), "invoice.pdf", "application/pdf", DocumentType.Unknown,
        DocumentStatus.Uploaded, DateTime.UtcNow);

    private sealed class StubDocumentService(
        Func<string, string, string, CancellationToken, Task<DocumentResponse>> create) : IDocumentService
    {
        public int CreateCalls { get; private set; }
        public DocumentContent? Content { get; set; }

        public Task<DocumentContent?> GetContentAsync(Guid id, CancellationToken cancellationToken = default) =>
            Task.FromResult(Content);

        public Task<IReadOnlyList<DocumentWorkspaceResponse>> GetWorkspaceAsync(int limit = 100,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentResponse> CreateAsync(
            string fileName, string contentType, string storagePath,
            CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            return create(fileName, contentType, storagePath, cancellationToken);
        }

        public Task<DocumentResponse?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DocumentResponse>> GetAllAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Wida.Tests";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class InterruptedFormFile : IFormFile
    {
        public string ContentType => "application/pdf";
        public string ContentDisposition => string.Empty;
        public IHeaderDictionary Headers { get; } = new HeaderDictionary();
        public long Length => 4;
        public string Name => "file";
        public string FileName => "interrupted.pdf";
        public bool PartialContentsWritten { get; private set; }

        public async Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
        {
            await target.WriteAsync(new byte[] { 1, 2 }, cancellationToken);
            PartialContentsWritten = true;
            throw new IOException("The incoming upload was interrupted.");
        }

        public void CopyTo(Stream target) => throw new NotSupportedException();
        public Stream OpenReadStream() => throw new NotSupportedException();
    }
}
