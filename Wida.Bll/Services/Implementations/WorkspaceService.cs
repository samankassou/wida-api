using Microsoft.EntityFrameworkCore;
using Wida.Bll.Dtos.Documents;
using Wida.Dal.Entities;
using Wida.Dal.Enums;
using Wida.Dal.Persistence;

namespace Wida.Bll.Services.Implementations;

public sealed class WorkspaceService(WidaDbContext db)
{
    // All predicates remain IQueryable: count, ordering and pagination execute in PostgreSQL.
    internal IQueryable<WorkspaceRow> Rows() => db.Documents.AsNoTracking()
        .Select(d => new { Document = d, Run = d.ProcessingRuns.OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).FirstOrDefault() })
        .Select(x => new {
            x.Document,
            Status = x.Run == null ? (ProcessingStatus?)null : x.Run.Status,
            Vendor = x.Run == null ? null : x.Run.ExtractedFields.Where(f => f.FieldName == "VendorName").Select(f => WorkspaceJson.Value(f.NormalizedValue) ?? f.RawValue).FirstOrDefault(),
            Number = x.Run == null ? null : x.Run.ExtractedFields.Where(f => f.FieldName == "InvoiceId").Select(f => WorkspaceJson.Value(f.NormalizedValue) ?? f.RawValue).FirstOrDefault(),
            Currency = x.Run == null ? null : x.Run.ExtractedFields.Where(f => f.FieldName == "InvoiceTotal" && !f.RequiresReview && f.Confidence >= 0.8m).Select(f => WorkspaceJson.Property(f.NormalizedValue, "currencyCode")).FirstOrDefault()
        })
        .Select(x => new WorkspaceRow {
            Id = x.Document.Id, UploadedAt = x.Document.UploadedAt, FileName = x.Document.OriginalFileName,
            Saved = x.Document.Invoice != null,
            Active = x.Status == ProcessingStatus.Pending || x.Status == ProcessingStatus.Running,
            Stage = x.Document.Invoice != null ? "saved" : x.Status == ProcessingStatus.Pending || x.Status == ProcessingStatus.Running ? "processing"
                : x.Status == ProcessingStatus.Completed ? "review" : x.Status == ProcessingStatus.Failed ? "failed" : "uploaded",
            Supplier = x.Document.Invoice != null && x.Document.Invoice.SupplierName != null && x.Document.Invoice.SupplierName != "" ? x.Document.Invoice.SupplierName
                : x.Vendor != null && x.Vendor != "" ? x.Vendor : x.Document.OriginalFileName,
            Number = x.Document.Invoice != null && x.Document.Invoice.InvoiceNumber != null && x.Document.Invoice.InvoiceNumber != ""
                ? x.Document.Invoice.InvoiceNumber : x.Number ?? "",
            Currency = x.Document.Invoice != null && x.Document.Invoice.Currency != null && x.Document.Invoice.Currency != ""
                ? x.Document.Invoice.Currency : x.Currency != null && System.Text.RegularExpressions.Regex.IsMatch(x.Currency.Trim(), "^[a-zA-Z]{3}$") ? x.Currency.Trim().ToUpper() : ""
        });

    public async Task<WorkspacePage> GetPageAsync(WorkspaceQuery query, CancellationToken cancellationToken)
    {
        var all = Rows();
        var counts = await all.GroupBy(x => x.Stage).Select(g => new { Stage = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Stage, x => x.Count, cancellationToken);
        foreach (var stage in new[] { "saved", "review", "processing", "failed", "uploaded" }) counts.TryAdd(stage, 0);
        var active = await all.CountAsync(x => x.Active, cancellationToken);
        var currencies = await all.Where(x => x.Currency != "").Select(x => x.Currency).Distinct().OrderBy(x => x).ToListAsync(cancellationToken);
        var months = await all.GroupBy(x => new { x.UploadedAt.Year, x.UploadedAt.Month })
            .Select(g => new WorkspaceMonth(g.Key.Year, g.Key.Month, g.Count(), g.Count(x => x.Saved))).ToListAsync(cancellationToken);
        var rows = all;
        if (query.View == "invoices") rows = rows.Where(x => x.Saved);
        if (query.Filter == "processing") rows = rows.Where(x => x.Active);
        else if (query.Filter != "all") rows = rows.Where(x => x.Stage == query.Filter);
        if (!string.IsNullOrWhiteSpace(query.Currency)) { var currency = query.Currency.ToUpperInvariant(); rows = rows.Where(x => x.Currency == currency); }
        if (query.Period != "all") { var since = DateTime.UtcNow.AddDays(-int.Parse(query.Period)); rows = rows.Where(x => x.UploadedAt >= since); }
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToLowerInvariant();
            rows = rows.Where(x => x.Supplier.ToLower().Contains(search) || x.FileName.ToLower().Contains(search) || x.Number.ToLower().Contains(search) || x.Currency.ToLower().Contains(search));
        }
        var total = await rows.CountAsync(cancellationToken);
        var page = Math.Min(query.Page, Math.Max(1, (int)Math.Ceiling(total / (double)query.PageSize)));
        var ordered = query.Sort == "supplier" ? rows.OrderBy(x => x.Supplier.ToLower()).ThenBy(x => x.Id)
            : query.Sort == "oldest" ? rows.OrderBy(x => x.UploadedAt).ThenBy(x => x.Id)
            : rows.OrderByDescending(x => x.UploadedAt).ThenBy(x => x.Id);
        var ids = await ordered.Skip((page - 1) * query.PageSize).Take(query.PageSize).Select(x => x.Id).ToListAsync(cancellationToken);
        var documents = await Details().Where(d => ids.Contains(d.Id)).ToListAsync(cancellationToken);
        var indexed = documents.ToDictionary(d => d.Id);
        return new WorkspacePage(ids.Select(id => Map(indexed[id])).ToList(), total, page, query.PageSize,
            new WorkspaceSummary(counts.Values.Sum(), counts, active, currencies, months));
    }

    public async Task<DocumentWorkspaceResponse?> GetItemAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = await Details().SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        return document is null ? null : Map(document);
    }

    private IQueryable<Document> Details() => db.Documents.AsNoTracking()
        .Include(d => d.Invoice).ThenInclude(i => i!.Lines)
        .Include(d => d.ProcessingRuns.OrderByDescending(r => r.StartedAt).ThenByDescending(r => r.Id).Take(1)).ThenInclude(r => r.ExtractedFields)
        .AsSplitQuery();

    private static DocumentWorkspaceResponse Map(Document d) => new(
        new DocumentResponse(d.Id, d.OriginalFileName, d.ContentType, d.DocumentType, d.Status, d.UploadedAt, d.PageCount),
        d.Invoice is null ? null : InvoiceService.Map(d.Invoice),
        d.ProcessingRuns.FirstOrDefault() is { } run ? ProcessingService.Map(run) : null);

    internal sealed class WorkspaceRow
    {
        public Guid Id { get; set; }
        public DateTime UploadedAt { get; set; }
        public string FileName { get; set; } = "";
        public bool Saved { get; set; }
        public bool Active { get; set; }
        public string Stage { get; set; } = "";
        public string Supplier { get; set; } = "";
        public string Number { get; set; } = "";
        public string Currency { get; set; } = "";
    }
}
