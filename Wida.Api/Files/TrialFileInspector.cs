using System.Buffers.Binary;
using UglyToad.PdfPig;

namespace Wida.Api.Files;

public static class TrialFileInspector
{
    public static int CountPages(byte[] bytes, string contentType)
    {
        if (contentType == "application/pdf")
        {
            using var pdf = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = false });
            return pdf.NumberOfPages;
        }
        if (contentType != "image/tiff") return 1;
        // Classic TIFF: each image directory represents a page. Reject cycles,
        // truncated directories and unsupported BigTIFF before submitting to Azure.
        bool little = bytes[0] == 73;
        uint U32(int offset) => little ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4))
            : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
        ushort U16(int offset) => little ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2))
            : BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(offset, 2));
        var offset = U32(4);
        var seen = new HashSet<uint>();
        int pages = 0;
        while (offset != 0)
        {
            if (!seen.Add(offset) || offset > bytes.Length - 2) throw new InvalidDataException("Invalid TIFF directory.");
            int end = checked((int)offset + 2 + U16((int)offset) * 12);
            if (end > bytes.Length - 4) throw new InvalidDataException("Truncated TIFF directory.");
            pages++;
            offset = U32(end);
        }
        return pages;
    }
}
