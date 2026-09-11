# API reference

This reference describes the controllers, DTOs, and services in the current working tree. See the [project README](../README.md) for configuration and startup instructions.

## Authentication and ownership

All document, invoice, processing and original-file endpoints require a Wida session; anonymous requests return `401`. POST/PUT requests also require the user-bound `X-CSRF-TOKEN` and antiforgery cookie. Queries and writes are restricted to the current user. See [Google sign-in](authentication.md) for the session endpoints and setup. The curl examples below show request payloads; add your session cookie and CSRF token when calling protected endpoints.

## Conventions

- The local HTTP launch profile uses `http://localhost:5085` behind the frontend proxy at `http://localhost:3000`. The HTTPS launch profile remains available at `https://localhost:7127`; the private API does not redirect HTTP requests.
- JSON properties use camelCase. Enum responses use their named string values, such as `Uploaded` and `Completed`.
- IDs are GUID strings. All ID route segments have a `:guid` constraint; malformed IDs do not match these routes.
- Invoice dates use `YYYY-MM-DD`. Server timestamps are generated in UTC and serialized as ISO 8601 timestamps.
- Successful resource creation returns `201 Created`; analysis admission returns `202 Accepted`. Both include a response body and a `Location` header.
- Collection endpoints return JSON arrays, including `[]` when empty. They do not support pagination or filtering.
- Authentication is required for data routes. Every record is scoped to its document owner; see [session setup](authentication.md).
- In Development, interactive documentation is available at `/scalar/v1`, and OpenAPI JSON is available at `/openapi/v1.json`.

## Routes

| Method | Route | Request | Successful response |
| --- | --- | --- | --- |
| `GET` | `/api/documents` | None | `200`, array of documents, newest upload first |
| `GET` | `/api/documents/{id}` | None | `200`, document |
| `GET` | `/api/documents/workspace?limit=100` | Optional limit, 1–500 | `200`, array of document/invoice/latest-run summaries |
| `GET` | `/api/documents/{id}/content` | Optional `download=true` | `200` original bytes; `206` for a valid byte range |
| `POST` | `/api/documents` | Multipart form field `file` | `201`, document |
| `GET` | `/api/invoices` | None | `200`, saved invoices with lines |
| `GET` | `/api/invoices/{id}` | None | `200`, invoice with lines |
| `GET` | `/api/invoices/document/{documentId}` | None | `200`, invoice with lines |
| `POST` | `/api/invoices` | Invoice JSON | `201`, invoice with lines |
| `PUT` | `/api/invoices/{id}` | Same invoice JSON; unchanged document ID | `200`, updated invoice with replacement lines |
| `POST` | `/api/processing/documents/{documentId}` | No body | `201`, manual processing run |
| `POST` | `/api/processing/documents/{documentId}/invoice` | No body | `202`, queued/active analysis run; `429` if queue capacity is exhausted; `503` if RabbitMQ is unavailable |
| `GET` | `/api/processing/{id}` | None | `200`, processing run |
| `GET` | `/api/processing/documents/{documentId}` | None | `200`, array of runs, newest start first |

### Workspace and original files

- `GET /api/documents/workspace?limit=100` returns an array of `{ "document": DocumentResponse, "invoice": InvoiceResponse|null, "latestRun": ProcessingRunResponse|null }`. The default limit is 100; supported limits are 1–500. Documents are ordered by upload time descending, with ID as a stable tie-breaker. The latest run is selected by start time, then ID descending. An empty workspace is `[]`.
- The workspace loads invoices, their lines, and only the latest run with its extracted fields through a bounded EF split query. It does not query each document separately. Filtering/search/paging beyond the latest loaded documents is not implemented.
- `GET /api/documents/{id}/content` streams original bytes with the canonical PDF/image content type, inline Content-Disposition, byte-range support, `X-Content-Type-Options: nosniff`, and `Cache-Control: private, no-store`. Add `?download=true` for attachment disposition. Filenames are header-encoded; storage paths are never returned. Browser support for TIFF previews varies; download is available.
- Retrieval returns `404` for missing metadata/files, unsupported or invalid file signatures, symlink files, and paths outside the current upload directory.
- `GET /api/invoices` returns all saved invoices with lines, newest creation first. `PUT /api/invoices/{id}` updates a saved invoice as described below.

The individual document, invoice, and processing-run reads return `404` when no matching record exists. Both processing creation endpoints also return `404` for a missing document. Listing processing runs does not check whether the document exists: a missing document also returns `200` with `[]`. There are no delete, approval, or export endpoints.

## Documents

Upload the original file as multipart form data, using the exact field name `file`:

```sh
curl --fail-with-body https://localhost:7127/api/documents \
  -F 'file=@/absolute/path/invoice.pdf;type=application/pdf'
```

Uploads accept PDF (`.pdf`), PNG (`.png`), JPEG (`.jpg`, `.jpeg`), and TIFF (`.tif`, `.tiff`) files up to 4 MiB (4 × 1024 × 1024 bytes). Empty, unsupported, mismatched, and disguised files return structured `400` errors under `errors.file`. Oversized files return `413`. Extension, reported MIME type, and format signature are checked; empty or `application/octet-stream` MIME values are accepted when extension and signature agree. This is format identification, not complete document decoding. The application allows 5 MiB of multipart request data to leave room for multipart overhead; host/proxy limits may reject large requests earlier. The stored content type is canonical for the verified format. Original filenames are reduced to a basename and must have at most 255 characters.

Files are written to `<content-root>/uploads` under a generated GUID filename with the original extension. The exact absolute file path is saved in document metadata. If copying the file or saving metadata fails, the controller attempts to remove the uploaded file before propagating the error. The document response contains metadata only:

```json
{
  "id": "cdd3f108-a769-4462-823d-f00553bff991",
  "originalFileName": "invoice.pdf",
  "contentType": "application/pdf",
  "documentType": "Unknown",
  "status": "Uploaded",
  "uploadedAt": "2026-09-07T10:00:00Z"
}
```

| Field | Type | Meaning |
| --- | --- | --- |
| `id` | GUID | Document identifier used by invoice and processing requests |
| `originalFileName` | string | Original client-supplied filename |
| `contentType` | string | Canonical MIME type for the verified file format |
| `documentType` | string enum | `Unknown` for newly uploaded documents |
| `status` | string enum | `Uploaded` for newly uploaded documents |
| `uploadedAt` | timestamp | Time the document entity was created |

The storage path is not exposed. Upload does not create an invoice or start analysis. Analysis sets type `Invoice` and, for an unsaved document, moves status through `Processing` to `ReviewRequired` or `Failed`. Creating or updating a structured invoice sets status `Saved`. `Saved` means data was saved, not approved. Reanalysis preserves a document that was already `Saved`. If an invoice is saved while analysis is running, `Saved` also takes precedence over the analysis status update. The new run still reports its own progress or failure. Manual pending runs do not change document status.

## Invoices

`POST /api/invoices` stores a manually supplied invoice for an existing document. A document can have at most one invoice. An invoice does not require a processing run. `GET /api/invoices` lists saved invoices. `PUT /api/invoices/{id}` accepts the same request shape as creation, requires the original `documentId`, and replaces the editable header values and entire line collection. Omitted optional fields are cleared and omitted lines become an empty list. Replacement lines receive new IDs. The invoice ID and `createdAt` remain unchanged; `updatedAt` is refreshed. This is a full update, not a partial PATCH. Moving an invoice to another document returns `409`.

### Request

Send `Content-Type: application/json`. Replace the example `documentId` with an uploaded document's ID:

```json
{
  "documentId": "cdd3f108-a769-4462-823d-f00553bff991",
  "supplierName": "Example Supplies",
  "supplierAddress": "10 Example Street, Douala",
  "supplierTaxId": "EXAMPLE-TAX-ID",
  "invoiceNumber": "INV-2026-001",
  "invoiceDate": "2026-09-07",
  "dueDate": "2026-10-07",
  "purchaseOrderNumber": "PO-001",
  "currency": "XAF",
  "subtotalAmount": 10000,
  "taxAmount": 0,
  "totalAmount": 10000,
  "lines": [
    {
      "lineNumber": 1,
      "description": "Example item",
      "quantity": 2,
      "unitOfMeasure": "item",
      "unitPrice": 5000,
      "taxRate": 0,
      "taxAmount": 0,
      "lineAmount": 10000
    }
  ]
}
```

| Field | JSON type | Requirement |
| --- | --- | --- |
| `documentId` | GUID string | POST: existing document without an invoice. PUT: the invoice's current document ID. |
| `supplierName` | string | Required; cannot be blank or whitespace |
| `supplierAddress` | string or null | Optional |
| `supplierTaxId` | string or null | Optional |
| `invoiceNumber` | string | Required; cannot be blank or whitespace |
| `invoiceDate` | date string | Required |
| `dueDate` | date string or null | Optional; cannot precede `invoiceDate` |
| `purchaseOrderNumber` | string or null | Optional |
| `currency` | string or null | Optional; database maximum length is 3 |
| `subtotalAmount` | number or null | Optional; participates in amount validation when supplied |
| `taxAmount` | number or null | Optional; participates in amount validation when supplied |
| `totalAmount` | number | Required |
| `lines` | array of line objects | Optional; defaults to `[]` when omitted. Send an array, not `null`. |

Every field in a line object is nullable and optional:

| Field | JSON type | Meaning |
| --- | --- | --- |
| `lineNumber` | integer or null | Client-supplied line number |
| `description` | string or null | Item description |
| `quantity` | number or null | Quantity |
| `unitOfMeasure` | string or null | Unit label |
| `unitPrice` | number or null | Price per unit |
| `taxRate` | number or null | Stored as supplied; no rate convention or calculation is enforced |
| `taxAmount` | number or null | Line tax amount; not included in the line arithmetic check |
| `lineAmount` | number or null | Line amount checked against quantity multiplied by unit price |

There is no automatic amount calculation, currency normalization, or line numbering. Invoice responses sort lines by `lineNumber`, then ID for deterministic ties.

### Validation and storage constraints

The invoice validator checks the following rules before looking up the document:

1. `supplierName`, `invoiceNumber`, `invoiceDate`, and `totalAmount` are required.
2. If supplied, `dueDate` cannot be earlier than `invoiceDate`.
3. When subtotal, tax, and total are all supplied, `subtotalAmount + taxAmount + shippingAmount - discountAmount` must equal `totalAmount`. Missing shipping and discount amounts count as zero. Shipping is an additional charge and discount is a deduction from the stated subtotal; do not enter adjustments already included in that subtotal.
4. For each line where quantity, unit price, and line amount are supplied, `quantity × unitPrice` must equal `lineAmount`.
5. When a subtotal and at least one line are supplied, and every line has a line amount, the sum of those amounts must equal the subtotal.

All amount comparisons accept an absolute difference of up to and including `0.01`. These checks do not enforce positive amounts, supported currency codes, unique invoice numbers, or a relationship between tax rates and tax amounts.

The database additionally limits string lengths: supplier name 255, supplier address 500, supplier tax ID 100, invoice number 100, purchase order number 100, currency 3, line description 1000, and unit of measure 50 characters. Invoice amounts, line quantities, and line amounts/prices use precision `(18,4)`; line tax rates use `(8,4)`. These string lengths and numeric ranges are validated before persistence. Numbers with more than four decimal places are rejected with field errors instead of being silently rounded by storage. `lines: null` or null line objects are rejected.

### Response

The response contains all fields from the request, with omitted optional values represented as `null` and omitted lines as `[]`. It also adds:

| Field | JSON type | Meaning |
| --- | --- | --- |
| `id` | GUID string | Generated invoice ID |
| `createdAt` | timestamp | Invoice creation time |
| `updatedAt` | timestamp | Initialized at creation and refreshed on each successful PUT |
| `lines[].id` | GUID string | Generated line ID |

Both invoice GET routes return the same response shape as creation. Entity navigation properties and each line's internal `invoiceId` are not included.

## Processing

Invoice analysis requires both `AzureDocumentIntelligence:Endpoint` and `AzureDocumentIntelligence:Key`. The analyzer creates its Azure client only when analysis is requested, so reading processing runs and creating manual runs work without these settings. Missing or invalid Azure settings during analysis are recorded as `DOCUMENT_ANALYSIS_FAILED` when the failure can be persisted.

### Create a manual run

```sh
DOCUMENT_ID="cdd3f108-a769-4462-823d-f00553bff991"
curl --fail-with-body -X POST \
  "https://localhost:7127/api/processing/documents/$DOCUMENT_ID"
```

This creates a `Pending` record with processor `Manual` and version `v1`. It does not enqueue work, start analysis, or progress automatically. The caller cannot choose the processor or version through this endpoint.

### Analyze an invoice

```sh
curl --fail-with-body -X POST \
  "https://localhost:7127/api/processing/documents/$DOCUMENT_ID/invoice"
```

This endpoint enqueues a durable `Pending` job and returns `202 Accepted` with its processing response and a status `Location`. A repeated request for the same document returns the existing active job. The background worker performs Azure analysis and transitions it to `Running`, then `Completed` or `Failed`. Poll `GET /api/processing/{id}` for results. Admission is limited to one active job per user and 100 globally; a full queue returns `429` with `Retry-After: 10`. New admission requires RabbitMQ publisher confirmation; broker unavailability returns `503` and rolls back the new run, preserving the upload. See [queue deployment and recovery](processing-queue.md).

The analyzer reads only the first document returned by Azure. It extracts these fields when present: `InvoiceId`, `InvoiceDate`, `DueDate`, `VendorName`, `SubTotal`, `TotalTax`, `InvoiceTotal`, and `TotalDiscount`. It also extracts `Items` fields as `Items[0].Description`, `Items[0].Quantity`, etc., using zero-based Azure array indices. Supported line fields are `Description`, `Quantity`, `Unit`, `UnitPrice`, `TaxRate`, `Tax`, and `Amount`. Each field retains its typed normalized value, raw text, confidence, page, bounding polygon, and review flag in the `extractedFields` collection. Empty or non-object rows and absent fields are skipped; no missing values are invented. No analyzed documents results in a `Failed` run. An analyzed document without any of the selected fields can complete with an empty `extractedFields` array.

Raw analysis and extracted fields are stored in the database. An extracted field has `requiresReview: true` when confidence is missing or below `0.80`; confidence equal to `0.80` does not require review. This flag does not decide whether data is valid. Every completed analysis of an unsaved document moves it to `ReviewRequired`, including empty extraction or high-confidence results. When available, the first bounding region supplies the field's page number and polygon.

Analysis does not create an invoice. To store an invoice, submit its values separately to `POST /api/invoices`.

Once admission is committed, disconnecting the browser does not cancel analysis. A restarted worker resumes polling a persisted Azure operation ID. If submission may have succeeded but its ID was not saved, the run fails with `ANALYSIS_SUBMISSION_UNCERTAIN` instead of automatically resubmitting. An explicit new analysis after a terminal run can consume additional pages.

### Processing response

New submissions return `Pending` with no fields and no completion time; the example below is a later completed status response.

```json
{
  "id": "0f9eba4f-3129-47d6-a6b1-d118c7d28172",
  "documentId": "cdd3f108-a769-4462-823d-f00553bff991",
  "status": "Completed",
  "processor": "AzureDocumentIntelligence",
  "processorVersion": "prebuilt-invoice",
  "startedAt": "2026-09-07T10:01:00Z",
  "completedAt": "2026-09-07T10:01:05Z",
  "errorCode": null,
  "errorMessage": null,
  "extractedFields": [
    {
      "id": "443a3805-b282-4e7b-911c-5a0c128bfab6",
      "fieldName": "InvoiceId",
      "rawValue": "INV-2026-001",
      "normalizedValue": "INV-2026-001",
      "confidence": 0.98,
      "source": "DocumentIntelligence",
      "pageNumber": 1,
      "boundingBox": [1.0, 1.0, 2.0, 1.0, 2.0, 1.2, 1.0, 1.2],
      "requiresReview": false
    }
  ]
}
```

| Field | JSON type | Meaning |
| --- | --- | --- |
| `id` | GUID string | Processing-run ID |
| `documentId` | GUID string | Associated document ID |
| `status` | string enum | `Pending`, `Running`, `Completed`, or `Failed` |
| `processor` | string | `Manual` or `AzureDocumentIntelligence` in current creation flows |
| `processorVersion` | string or null | `v1` for manual runs; `prebuilt-invoice` for analysis |
| `startedAt` | timestamp | Run-record creation time, including for a pending manual run |
| `completedAt` | timestamp or null | Set when analysis completes or a caught analysis error occurs |
| `errorCode` | string or null | `DOCUMENT_ANALYSIS_FAILED`, `ANALYSIS_SUBMISSION_UNCERTAIN`, `ANALYSIS_EXPIRED`, or `ANALYSIS_RETRY_EXHAUSTED`; see queue recovery guide |
| `errorMessage` | string or null | Error message limited to 2,000 characters |
| `extractedFields` | array | Persisted fields; `[]` for a manual run or an analysis with no extracted fields |

Creation and both processing GET routes use this response shape. Each extracted field contains:

| Field | JSON type | Meaning |
| --- | --- | --- |
| `id` | GUID string | Extracted-field ID |
| `fieldName` | string | Azure field name, such as `InvoiceId` or `InvoiceTotal` |
| `rawValue` | string or null | Text recognized in the document; analysis fails if a selected field exceeds 4,000 characters |
| `normalizedValue` | JSON value or null | Typed value: a string, `YYYY-MM-DD` date string, number, or currency object as appropriate |
| `confidence` | number or null | Azure confidence score; stored with four decimal places |
| `source` | string enum | `DocumentIntelligence` for this analyzer |
| `pageNumber` | integer or null | Page number from the first bounding region, when present |
| `boundingBox` | JSON array or null | Polygon coordinates from the first bounding region, when present |
| `requiresReview` | boolean | True when confidence is missing or below `0.80` |

Normalized values and bounding data are JSON values, not strings containing JSON. Raw analysis remains internal and has no retrieval endpoint. There is no endpoint to edit extracted fields or record a review decision.

## Enums

| Response field | Defined values |
| --- | --- |
| Document `documentType` | `Unknown`, `Invoice` |
| Document `status` | `Uploaded`, `Queued`, `Processing`, `ReviewRequired`, `Approved`, `Rejected`, `Failed`, `Saved` |
| Processing run `status` | `Pending`, `Running`, `Completed`, `Failed` |

Upload creates `Unknown` / `Uploaded`; analysis sets `Invoice` and moves unsaved documents through `Processing` to `ReviewRequired` or `Failed`; invoice creation/update sets `Invoice` / `Saved`. Existing `Saved` status survives reanalysis, and saving during analysis likewise preserves `Saved`. `Queued` marks admitted analysis jobs; `Approved` and `Rejected` remain reserved. Extracted field `source` uses the `ExtractionSource` enum with `Ocr`, `DocumentIntelligence`, `Llm`, `Rule`, and `Human`; the Azure analyzer returns `DocumentIntelligence`.

## Error behavior

| Condition | Current behavior |
| --- | --- |
| Missing or expired Wida session on a data route | `401` |
| Missing or invalid user-bound antiforgery token on a mutation | `400` with title `Session verification failed` |
| Missing or foreign Origin on a Next.js proxy mutation | Proxy returns `403` before contacting the API |
| Invalid JSON, incompatible field type, or other model-binding failure | Framework-generated `400` response under `[ApiController]` |
| Empty, unsupported, disguised, or MIME-mismatched upload | Structured `400` with `errors.file` |
| Upload exceeds 4 MiB | `413`; application-handled failures contain `errors.file`, while earlier host rejections may differ |
| Workspace limit outside 1–500 | `400` with `errors.limit` |
| Record absent on a single-record GET | Explicit `404` |
| Malformed route GUID | Route does not match; normally `404` |
| Invoice business-validation failure | `400` ValidationProblemDetails with field-keyed `errors` |
| Missing document during invoice creation or missing invoice during update | `404` ProblemDetails |
| Missing document during manual run creation or invoice analysis | Explicit `404` |
| A document already has an invoice, or PUT attempts reassociation | `409` ProblemDetails |
| Background analysis failure | Status polling returns a `Failed` run with an error code and message. Submission itself returns `202`. |
| Client disconnects after admission | The durable job continues independently of the browser. |
| Startup configuration, file-upload, or persistence failure outside the analysis handler | Unhandled exception |

Invoice create/update errors are mapped by the invoice controller to `application/problem+json`. Validation keys use camelCase paths such as `supplierName`, `dueDate`, `totalAmount`, and `lines[0].lineAmount`:

```json
{
  "title": "Some invoice fields need attention.",
  "status": 400,
  "errors": {
    "supplierName": ["This field is required."],
    "totalAmount": ["Subtotal + tax + shipping − discount does not match total amount."]
  }
}
```

Document status uses optimistic concurrency. The async persistence path reconciles a conflict involving `Saved` and retries once; unrelated or repeated conflicts remain errors. This is not general conflict merging for simultaneous invoice edits. See [concurrency handling](architecture.md#concurrent-invoice-saving-and-extraction).

The existing document uniqueness constraint also maps concurrent duplicate creation to `409`. Unexpected persistence and infrastructure failures still become HTTP `500`; the response body depends on the environment and host. There is no global exception middleware.

Poll the analysis run until `Completed` or `Failed`; HTTP `202` only confirms admission. Database interruptions leave a job for recovery by the worker. A known Azure operation is polled again; an uncertain submission is never automatically sent again.

## Current limitations

- File writes and database writes are not atomic: a process interruption or failed cleanup can leave an orphan upload.
- Eight selected header fields and supported line-item fields from the first analyzed document are extracted; additional analyzed documents are not processed.
- Background jobs recover automatically as described in the queue guide. There is no public run-resume endpoint, extraction-review workflow, automatic invoice creation, or approval/rejection action.

## Source map

- Routes and HTTP results: [DocumentsController](../Wida.Api/Controllers/DocumentsController.cs), [InvoicesController](../Wida.Api/Controllers/InvoicesController.cs), [ProcessingController](../Wida.Api/Controllers/ProcessingController.cs).
- JSON configuration and middleware: [Program.cs](../Wida.Api/Program.cs).
- Request and response contracts: [DTOs](../Wida.Bll/Dtos).
- Invoice rules: [InvoiceValidator](../Wida.Bll/Validators/InvoiceValidator.cs) and [InvoiceService](../Wida.Bll/Services/Implementations/InvoiceService.cs).
- Run lifecycle and field persistence: [ProcessingService](../Wida.Bll/Services/Implementations/ProcessingService.cs).
- Azure extraction: [AzureDocumentAnalyzer](../Wida.Dal/Services/AzureDocumentAnalyzer.cs).
- Database constraints and query ordering: [configurations](../Wida.Dal/Configurations) and [repositories](../Wida.Dal/Repositories/Implementations).

### Shipping and discount adjustments

Invoice requests and responses include nullable decimal `shippingAmount` and `discountAmount` (precision 18, scale 4). Both are persisted on create/update and cleared by omission on a full update.

The analyzer extracts `TotalDiscount` when returned by Azure. Shipping is entered manually: the [Azure invoice schema](https://github.com/Azure-Samples/document-intelligence-code-samples/blob/main/schema/2024-11-30-ga/invoice.md) has no dedicated shipping-charge field. Shipping is never inferred from a difference between totals.

Tax-inclusive line amounts with net unit prices are accepted when the line's tax amount or percentage reconciles the difference within 0.01. If both tax values are supplied, both must agree. Subtotal validation uses the net quantity × unit price for these lines; stored source amounts are unchanged. Lines without tax evidence retain the existing net-amount validation. The review form identifies recognized tax-inclusive amounts and shows the net amount used for the subtotal check.

## Trial endpoints

All trial routes require the normal user session; POST also requires CSRF.

- `GET /api/trial`: lifetime pages remaining, total granted, public monthly availability, credit request state, retention and storage limits, optional CAPTCHA public site key.
- `POST /api/trial/credits`: records one pending request for manual review; repeating it does not create another request.
- `POST /api/trial/challenge` with `{ "token": "…" }`: validates the configured managed Turnstile challenge and sets an owner-bound HttpOnly cookie.
- `POST /api/processing/documents/{id}/invoice?reanalyze=true`: deliberately charges another analysis. Without the parameter, a completed result is reused.

Page or storage exhaustion returns `429` with an explanatory `detail`; missing/unacceptable page counts return `400`; expired originals return `410` on content reads. Limits and reservations are detailed in [public beta](public-beta.md).
