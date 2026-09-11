using Wida.Api.Files;
namespace Wida.Tests;
public class TrialFileInspectorTests
{
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Counts_actual_pdf_pages(int pages) => Assert.Equal(pages,
        TrialFileInspector.CountPages(DocumentsControllerTests.ValidPdf(pages), "application/pdf"));

    [Fact]
    public void Invalid_pdf_is_rejected() => Assert.ThrowsAny<Exception>(() =>
        TrialFileInspector.CountPages("%PDF-1.7 fake"u8.ToArray(), "application/pdf"));

    [Fact]
    public void Cyclic_tiff_is_rejected() => Assert.Throws<InvalidDataException>(() =>
        TrialFileInspector.CountPages(new byte[] {73,73,42,0,8,0,0,0,0,0,8,0,0,0}, "image/tiff"));
}
