# Wida API

[Project overview](../README.md) · [API reference](../docs/api.md) · [Architecture](../docs/architecture.md) · [MIT licence](../LICENSE)

.NET 10 API for uploading documents, storing invoices and their line items, and tracking document processing. Data is stored in PostgreSQL through Entity Framework Core; uploaded files are stored on the API filesystem. The Azure Document Intelligence integration under development uses `prebuilt-invoice`.

## Project structure

- `Wida.Api`: controllers, application startup, configuration, and OpenAPI/Scalar.
- `Wida.Bll`: services, request/response DTOs, invoice validation, and document analysis contracts.
- `Wida.Dal`: entities, EF configurations and migrations, repositories, and the Azure analyzer implementation.

See [current limitations](#current-limitations) before running the current working tree, which contains an unfinished Azure integration.

## Local setup

Install the .NET 10 SDK and use a local or remote PostgreSQL server. Azure processing also requires an Azure Document Intelligence resource endpoint and API key.

Run the following commands from the `Wida.Api` directory. From the repository root:

```sh
cd Wida.Api
```

The examples use a POSIX shell, such as Bash or Zsh. The current [build blocker](#current-limitations) must be resolved before migrations or application startup can succeed.

1. Create a PostgreSQL database named `wida` with a login that can manage its tables, or use an existing database and login.
2. Configure the connection with .NET User Secrets:

   ```sh
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=wida;Username=wida;Password=YOUR_LOCAL_PASSWORD"
   ```

3. Configure the Azure analyzer:

   ```sh
   dotnet user-secrets set "AzureDocumentIntelligence:Endpoint" "https://YOUR_RESOURCE.cognitiveservices.azure.com/"
   dotnet user-secrets set "AzureDocumentIntelligence:Key" "YOUR_API_KEY"
   ```

   The analyzer is injected into the processing service, so these settings are currently needed for every processing endpoint, including reads and manual run creation. Document and invoice endpoints do not require the analyzer.

4. Restore dependencies and apply all database migrations:

   ```sh
   dotnet restore
   dotnet tool restore
   ASPNETCORE_ENVIRONMENT=Development dotnet ef database update --project ../Wida.Dal --startup-project .
   ```

   The local tool manifest at [.config/dotnet-tools.json](.config/dotnet-tools.json) pins `dotnet-ef` to `10.0.11`. Setting the environment explicitly makes the startup project load Development configuration and User Secrets while the EF tool runs.

5. Trust the development HTTPS certificate and start the HTTPS profile:

   ```sh
   dotnet dev-certs https --trust
   dotnet run --launch-profile https
   ```

   The HTTPS profile listens at `https://localhost:7127` and `http://localhost:5085`, with HTTPS redirection enabled. The `http` launch profile listens only at `http://localhost:5085`.

In Development, open [Scalar](https://localhost:7127/scalar/v1) to explore the API or inspect the [OpenAPI document](https://localhost:7127/openapi/v1.json). These routes are only mapped in Development.

### Configuration

User Secrets are loaded in Development. Supply deployment values through environment variables or your secret manager:

| Configuration key | Environment variable | Purpose |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | PostgreSQL connection; required at startup. |
| `AzureDocumentIntelligence:Endpoint` | `AzureDocumentIntelligence__Endpoint` | Azure resource endpoint; required when resolving the processing service. |
| `AzureDocumentIntelligence:Key` | `AzureDocumentIntelligence__Key` | Azure API key; required when resolving the processing service. |

The web project already has `UserSecretsId` configured, so `dotnet user-secrets init` is unnecessary. Replace any sample Azure endpoint in local Development settings with your resource endpoint. An `appsettings.Local.json` file is not loaded by the current startup code.

Uploads are written beneath `<content-root>/uploads` with generated filenames and the original extension. The process needs write access there; preserve these files along with the database because analysis reads the stored file path. Files are not served by a download endpoint.

## API endpoints

JSON uses camelCase property names and string enum values such as `Uploaded`, `Pending`, and `Completed`.

| Method | Route | Behavior |
| --- | --- | --- |
| GET | `/api/documents` | List document metadata. |
| GET | `/api/documents/{id}` | Get document metadata; `404` if absent. |
| POST | `/api/documents` | Upload multipart field `file`; returns `201` with document metadata and a Location header. Empty files return `400`. |
| POST | `/api/invoices` | Create one invoice with optional line items for an existing document; returns `201` and a Location header. |
| GET | `/api/invoices/{id}` | Get an invoice and its lines; `404` if absent. |
| GET | `/api/invoices/document/{documentId}` | Get the invoice for a document; `404` if absent. |
| POST | `/api/processing/documents/{documentId}` | Create a `Pending` run with processor `Manual`, version `v1`; returns `201`. This does not start analysis. |
| POST | `/api/processing/documents/{documentId}/invoice` | Run Azure invoice analysis during the request and return the run with `201`. |
| GET | `/api/processing/{id}` | Get processing run metadata; `404` if absent. |
| GET | `/api/processing/documents/{documentId}` | List processing runs for a document; returns an empty list if none exist. |

All route IDs are GUIDs. Processing responses contain `id`, `documentId`, `status`, `processor`, `processorVersion`, `startedAt`, `completedAt`, `errorCode`, and `errorMessage`.

## Example workflow

After setup and resolving the integration issues below, upload a document:

```sh
curl --fail-with-body https://localhost:7127/api/documents \
  -F 'file=@/absolute/path/invoice.pdf;type=application/pdf'
```

Copy the returned `id` into `DOCUMENT_ID`:

```sh
DOCUMENT_ID="REPLACE_WITH_DOCUMENT_GUID"
curl --fail-with-body -X POST \
  "https://localhost:7127/api/processing/documents/$DOCUMENT_ID/invoice"
curl --fail-with-body \
  "https://localhost:7127/api/processing/documents/$DOCUMENT_ID"
```

Analysis waits for Azure to finish; there is no background worker. A run is saved as `Running`, then updated to `Completed` or `Failed`. Caught analysis errors set `errorCode` to `DOCUMENT_ANALYSIS_FAILED` and include an error message. A failed analysis can still return HTTP `201`, so inspect the response `status`.

The analyzer reads the first analyzed document and stores these fields when present: `InvoiceId`, `InvoiceDate`, `DueDate`, `VendorName`, `SubTotal`, `TotalTax`, and `InvoiceTotal`. Fields with missing confidence or confidence below `0.80` are marked `RequiresReview`. Raw analysis and extracted fields are persisted, but are not included in the processing response or exposed by a separate endpoint. Line items are not extracted by the current implementation.

Analysis does not create an invoice or update the document's type/status. Create the invoice separately using the document ID:

```sh
curl --fail-with-body https://localhost:7127/api/invoices \
  -H 'Content-Type: application/json' \
  --data "{
    \"documentId\": \"$DOCUMENT_ID\",
    \"supplierName\": \"Example Supplier\",
    \"invoiceNumber\": \"INV-001\",
    \"invoiceDate\": \"2026-09-06\",
    \"dueDate\": \"2026-10-06\",
    \"currency\": \"USD\",
    \"subtotalAmount\": 100,
    \"taxAmount\": 10,
    \"totalAmount\": 110,
    \"lines\": [{
      \"lineNumber\": 1,
      \"description\": \"Example item\",
      \"quantity\": 2,
      \"unitPrice\": 50,
      \"lineAmount\": 100
    }]
  }"
```

### Invoice validation

An invoice requires a supplier name, invoice number, invoice date, total amount, and an existing document with no invoice already attached. Dates use `YYYY-MM-DD`. Optional fields include supplier address, supplier tax ID, purchase order number, currency, amounts, and lines.

- Due date cannot precede invoice date.
- When all relevant amounts are supplied, subtotal plus tax must equal total, and quantity times unit price must equal line amount.
- If a subtotal and a nonempty set of lines with `lineAmount` on every line are supplied, those line amounts must sum to the subtotal.
- Amount comparisons allow an absolute difference of `0.01`.

Business validation failures, missing documents during creation/processing, and duplicate invoices currently throw exceptions without an API exception handler; they are not mapped to structured `400`, `404`, or `409` responses.

## Database migrations

Migrations live in `Wida.Dal/Migrations`:

| Migration | Tables added |
| --- | --- |
| `InitialCreate` | `Documents` |
| `AddInvoiceEntities` | `Invoices`, `InvoiceLines` |
| `AddProcessingEntities` | `ProcessingRuns`, `ExtractedFields` |

A document has at most one invoice and can have multiple processing runs. Invoice lines belong to an invoice; extracted fields belong to a processing run. These child relationships use cascade deletion. Raw analysis, normalized extracted values, and bounding boxes use PostgreSQL `jsonb` columns.

Schema changes are applied explicitly, not on application startup. After changing EF models/configurations, run from `Wida.Api`:

```sh
ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add DescribeYourSchemaChange --project ../Wida.Dal --startup-project .
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update --project ../Wida.Dal --startup-project .
```

## Current limitations

- The Azure analyzer and DAL dependency registration reference `Wida.Bll` types, but `Wida.Dal` has no project reference to `Wida.Bll`. Since BLL already references DAL, adding the reverse reference would introduce a cycle. The analysis contracts need an appropriate shared location before the integration can build.
- Source inspection also shows an Azure SDK mismatch: field normalization calls `field.Value.AsString()` and similar methods, while the referenced Azure Document Intelligence SDK `1.0.0` exposes typed properties such as `ValueString`, `ValueDate`, `ValueCurrency`, and `ValueDouble`.
- Upload currently saves `StoragePath` by appending the generated filename to `physicalPath`, which already includes that filename. This produces an invalid analysis path and needs correction before newly uploaded documents can be analyzed.
- Processing has no background execution, retry endpoint, extracted-field review endpoint, or automatic invoice creation. New documents remain `Unknown` / `Uploaded` through these flows.
- Authentication, authorization, and a CORS policy are not configured. Uploads only explicitly reject empty files; there is no application-specific file type allowlist or size limit configured beyond the server/framework defaults.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Build cannot resolve `Wida.Bll` types in DAL | See the existing project dependency issue above; configuration changes cannot resolve it. |
| Startup asks for `ConnectionStrings:DefaultConnection` | Set the secret from `Wida.Api`, or supply `ConnectionStrings__DefaultConnection` in the process environment. |
| Tables do not exist | Apply the committed migrations against the configured database; startup does not apply them. |
| Processing reports a missing Azure endpoint or key | Set both analyzer settings, including when only reading runs or creating a manual run. |
| Upload succeeds but analysis cannot read the file | Check the duplicated filename in the current persisted `StoragePath` implementation and read access to the stored file. |
| Scalar or OpenAPI returns `404` | Use the Development environment, as set by the supplied launch profiles. |
| HTTPS certificate is not trusted locally | Run `dotnet dev-certs https --trust` and use the supplied HTTPS launch profile. |

## Build check

From the repository root:

```sh
dotnet build Wida.slnx
```

No automated test project is currently included. Once the integration builds, use the example workflow or [HTTP request file](wida-api.http) to check upload, processing status, and invoice creation against a configured database and Azure resource. Run the requests individually and replace their placeholder IDs with IDs returned by the API. Invoice analysis sends the uploaded document to the configured Azure resource.

A manual check should cover upload and retrieval, invoice creation and retrieval by both invoice/document ID, a manual `Pending` run, and an Azure analysis response whose `status` is inspected even when HTTP is `201`.

## Licence

Wida API is licensed under the [MIT License](../LICENSE).
