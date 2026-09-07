# API reference

This reference describes the controllers, DTOs, and services in the current working tree. See the [project README](../README.md) for configuration and startup instructions.

## Conventions

- The local HTTPS launch profile uses `https://localhost:7127`. The application enables HTTPS redirection.
- JSON properties use camelCase. Enum responses use their named string values, such as `Uploaded` and `Completed`.
- IDs are GUID strings. All ID route segments have a `:guid` constraint; malformed IDs do not match these routes.
- Invoice dates use `YYYY-MM-DD`. Server timestamps are generated in UTC and serialized as ISO 8601 timestamps.
- Successful creation returns `201 Created`, a response body, and a `Location` header pointing to the corresponding get-by-ID endpoint.
- Collection endpoints return JSON arrays, including `[]` when empty. They do not support pagination or filtering.
- No authentication or authorization is configured in the application.
- In Development, interactive documentation is available at `/scalar/v1`, and OpenAPI JSON is available at `/openapi/v1.json`.

## Routes

| Method | Route | Request | Successful response |
| --- | --- | --- | --- |
| `GET` | `/api/documents` | None | `200`, array of documents, newest upload first |
| `GET` | `/api/documents/{id}` | None | `200`, document |
| `POST` | `/api/documents` | Multipart form field `file` | `201`, document |
| `GET` | `/api/invoices/{id}` | None | `200`, invoice with lines |
| `GET` | `/api/invoices/document/{documentId}` | None | `200`, invoice with lines |
| `POST` | `/api/invoices` | Invoice JSON | `201`, invoice with lines |
| `POST` | `/api/processing/documents/{documentId}` | No body | `201`, manual processing run |
| `POST` | `/api/processing/documents/{documentId}/invoice` | No body | `201`, analysis run, including runs whose status is `Failed` |
| `GET` | `/api/processing/{id}` | None | `200`, processing run |
| `GET` | `/api/processing/documents/{documentId}` | None | `200`, array of runs, newest start first |

The individual document, invoice, and processing-run reads return `404` when no matching record exists. Both processing creation endpoints also return `404` for a missing document. Listing processing runs does not check whether the document exists: a missing document also returns `200` with `[]`. There are no update, delete, file-download, or invoice-list endpoints.

## Documents

Upload the original file as multipart form data, using the exact field name `file`:

```sh
curl --fail-with-body https://localhost:7127/api/documents \
  -F 'file=@/absolute/path/invoice.pdf;type=application/pdf'
```

The controller rejects a zero-byte file with `400` and the message `The uploaded file is empty.` The application does not define a file-extension or MIME-type allowlist, inspect the file contents, or configure a custom upload-size limit. Framework and hosting limits still apply. The reported content type comes from the upload.

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
| `contentType` | string | Client-supplied MIME type |
| `documentType` | string enum | `Unknown` for newly uploaded documents |
| `status` | string enum | `Uploaded` for newly uploaded documents |
| `uploadedAt` | timestamp | Time the document entity was created |

The storage path is not exposed. Upload does not create an invoice or start analysis. Creating an invoice or processing run also does not change the document's type or status.

## Invoices

`POST /api/invoices` stores a manually supplied invoice for an existing document. A document can have at most one invoice. An invoice does not require a processing run.

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
| `documentId` | GUID string | Must identify an existing document without an invoice |
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

There is no automatic amount calculation, currency normalization, line numbering, or line sorting. Do not rely on a particular line order when reading an invoice.

### Validation and storage constraints

The invoice validator checks the following rules before looking up the document:

1. `supplierName`, `invoiceNumber`, `invoiceDate`, and `totalAmount` are required.
2. If supplied, `dueDate` cannot be earlier than `invoiceDate`.
3. When subtotal, tax, and total are all supplied, `subtotalAmount + taxAmount` must equal `totalAmount`.
4. For each line where quantity, unit price, and line amount are supplied, `quantity × unitPrice` must equal `lineAmount`.
5. When a subtotal and at least one line are supplied, and every line has a line amount, the sum of those amounts must equal the subtotal.

All amount comparisons accept an absolute difference of up to and including `0.01`. These checks do not enforce positive amounts, supported currency codes, unique invoice numbers, or a relationship between tax rates and tax amounts.

The database additionally limits string lengths: supplier name 255, supplier address 500, supplier tax ID 100, invoice number 100, purchase order number 100, currency 3, line description 1000, and unit of measure 50 characters. Invoice amounts, line quantities, and line amounts/prices use precision `(18,4)`; line tax rates use `(8,4)`. These are persistence constraints, not request-validation rules with dedicated API error responses.

### Response

The response contains all fields from the request, with omitted optional values represented as `null` and omitted lines as `[]`. It also adds:

| Field | JSON type | Meaning |
| --- | --- | --- |
| `id` | GUID string | Generated invoice ID |
| `createdAt` | timestamp | Invoice creation time |
| `updatedAt` | timestamp | Initialized at creation; no update endpoint exists |
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

This endpoint creates a separate `Running` record with processor `AzureDocumentIntelligence` and version `prebuilt-invoice`, reads the stored file, and waits for Azure analysis to complete within the HTTP request. It then saves and returns the run as `Completed` or `Failed`. Calling it again creates another run; it does not resume an earlier manual or failed run.

The analyzer reads only the first document returned by Azure. It extracts these fields when present: `InvoiceId`, `InvoiceDate`, `DueDate`, `VendorName`, `SubTotal`, `TotalTax`, and `InvoiceTotal`. It does not extract invoice line items. No analyzed documents results in a `Failed` run. An analyzed document without any of the selected fields can complete with an empty `extractedFields` array.

Raw analysis and extracted fields are stored in the database. An extracted field has `requiresReview: true` when confidence is missing or below `0.80`; confidence equal to `0.80` does not require review. This flag does not change the run or document status. When available, the first bounding region supplies the field's page number and polygon.

Analysis does not create an invoice. To store an invoice, submit its values separately to `POST /api/invoices`.

Request cancellation after a run starts is handled separately from other analysis failures: the service attempts to save `Failed` with `DOCUMENT_ANALYSIS_CANCELLED`, then propagates cancellation. Terminal persistence uses a separate 10-second token independent of the request token, for both successful and failed analysis. A disconnected client may receive no response; retrieve the run or list the document's runs to inspect persisted status. Database failure, expiration of the persistence timeout, or process termination can still leave a run `Running`.

### Processing response

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
| `errorCode` | string or null | `DOCUMENT_ANALYSIS_FAILED` on a caught analysis error; `DOCUMENT_ANALYSIS_CANCELLED` when request cancellation interrupts analysis |
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
| Document `status` | `Uploaded`, `Queued`, `Processing`, `ReviewRequired`, `Approved`, `Rejected`, `Failed` |
| Processing run `status` | `Pending`, `Running`, `Completed`, `Failed` |

The current API creates documents with `Unknown` / `Uploaded` and does not transition them to the other declared values. Extracted field `source` uses the `ExtractionSource` enum with `Ocr`, `DocumentIntelligence`, `Llm`, `Rule`, and `Human`; the Azure analyzer returns `DocumentIntelligence`.

## Error behavior

| Condition | Current behavior |
| --- | --- |
| Invalid JSON, incompatible field type, or other model-binding failure | Framework-generated `400` response under `[ApiController]` |
| Zero-byte upload | Explicit `400` with `The uploaded file is empty.` |
| Record absent on a single-record GET | Explicit `404` |
| Malformed route GUID | Route does not match; normally `404` |
| Invoice business-validation failure | `InvalidOperationException`; messages are joined with ` \| ` |
| Missing document during invoice creation | `InvalidOperationException` with `Document not found.` |
| Missing document during manual run creation or invoice analysis | Explicit `404` |
| A document already has an invoice | `InvalidOperationException` with `An invoice already exists for this document.` |
| Analysis failure, including missing Azure configuration, unreadable stored file, or no analyzed documents | Run is marked `Failed` with `DOCUMENT_ANALYSIS_FAILED`; endpoint still returns `201` if saving the failure succeeds |
| Request cancellation during analysis | Attempt to persist `Failed` with `DOCUMENT_ANALYSIS_CANCELLED` using a separate 10-second token, then propagate cancellation |
| Startup configuration, file-upload, or persistence failure outside the analysis handler | Unhandled exception |

There is no custom exception middleware mapping service or persistence exceptions to a stable error contract. These unhandled failures normally become HTTP `500`; the response body depends on the environment and host. Invoice validation failures are therefore not currently structured `400` responses, and missing documents or duplicate invoices during invoice creation are not mapped to `404`/`409`.

Always inspect the analysis run's `status`; `curl --fail-with-body` alone cannot detect a failed analysis returned with `201`. Request cancellation can prevent a response from reaching the client even after the terminal state is saved. A failed final database save prevents the updated state from being persisted.

## Current limitations

- Historical document records with relative or duplicated storage paths are not repaired automatically. Re-upload those documents or explicitly repair their metadata to point to existing files.
- File writes and database writes are not atomic: a process interruption or failed cleanup can leave an orphan upload.
- Only seven selected header fields from the first analyzed document are extracted; line items and additional analyzed documents are not processed.
- There is no background worker, run-resume endpoint, extraction-review workflow, automatic invoice creation, or document-status transition in the current API.

## Source map

- Routes and HTTP results: [DocumentsController](../Wida.Api/Controllers/DocumentsController.cs), [InvoicesController](../Wida.Api/Controllers/InvoicesController.cs), [ProcessingController](../Wida.Api/Controllers/ProcessingController.cs).
- JSON configuration and middleware: [Program.cs](../Wida.Api/Program.cs).
- Request and response contracts: [DTOs](../Wida.Bll/Dtos).
- Invoice rules: [InvoiceValidator](../Wida.Bll/Validators/InvoiceValidator.cs) and [InvoiceService](../Wida.Bll/Services/Implementations/InvoiceService.cs).
- Run lifecycle and field persistence: [ProcessingService](../Wida.Bll/Services/Implementations/ProcessingService.cs).
- Azure extraction: [AzureDocumentAnalyzer](../Wida.Dal/Services/AzureDocumentAnalyzer.cs).
- Database constraints and query ordering: [configurations](../Wida.Dal/Configurations) and [repositories](../Wida.Dal/Repositories/Implementations).
