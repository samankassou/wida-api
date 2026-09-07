using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Wida.Api.Controllers;
using Wida.Bll.Dtos.Documents;
using Wida.Bll.Services.Interfaces;
using Wida.Dal.Enums;

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
        byte[] contents = [0, 1, 2, 13, 10, 127, 128, 255];
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
        using var source = new MemoryStream([1, 2, 3]);
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

    private DocumentsController CreateController(IDocumentService service) =>
        new(service, new TestWebHostEnvironment { ContentRootPath = _contentRoot });

    private static DocumentResponse CreateDocument() => new(
        Guid.NewGuid(), "invoice.pdf", "application/pdf", DocumentType.Unknown,
        DocumentStatus.Uploaded, DateTime.UtcNow);

    private sealed class StubDocumentService(
        Func<string, string, string, CancellationToken, Task<DocumentResponse>> create) : IDocumentService
    {
        public int CreateCalls { get; private set; }

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
