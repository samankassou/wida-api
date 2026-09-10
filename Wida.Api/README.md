# Wida API

[Project overview](../README.md) · [API reference](../docs/api.md) · [Architecture](../docs/architecture.md) · [MIT licence](../LICENSE)

.NET 10 API for uploading documents, storing invoices and their line items, and tracking document processing. Data is stored in PostgreSQL through Entity Framework Core; uploaded files are stored on the API filesystem. Azure Document Intelligence's `prebuilt-invoice` model extracts selected invoice header fields.

## Project structure

- `Wida.Api`: controllers, application startup, configuration, and OpenAPI/Scalar.
- `Wida.Bll`: services, request/response DTOs, and invoice validation.
- `Wida.Dal`: entities, EF configurations and migrations, repositories, analysis contracts/models, and the Azure analyzer implementation.
- `Wida.Tests`: Azure response fixtures, SQLite transactional processing tests, EF InMemory service fixtures, and simulated Google HTTP authentication tests.

## Authentication

All data endpoints now require a Google-backed Wida session. Configure the OAuth client, invited addresses and frontend callback using [Google sign-in and pilot access](../docs/authentication.md). New uploads are owned by the signed-in user. The ownership migration preserves existing documents without assigning them to an account.

## Local setup

Install the .NET 10 SDK and use a local or remote PostgreSQL server. Azure processing also requires an Azure Document Intelligence resource endpoint and API key.

Run the following commands from the `Wida.Api` directory. From the repository root:

```sh
cd Wida.Api
```

The examples use a POSIX shell, such as Bash or Zsh.

1. Create a PostgreSQL database named `wida` with a login that can manage its tables, or use an existing database and login.
2. Configure the connection with .NET User Secrets:

   ```sh
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=wida;Username=wida;Password=YOUR_LOCAL_PASSWORD"
   ```

3. Configure [Google sign-in and pilot access](../docs/authentication.md#local-configuration), including `Authentication:Google:ClientId`, `Authentication:Google:ClientSecret`, `Authentication:AllowedEmails:0`, and `Authentication:PublicOrigin`. For the standard local frontend use `http://localhost:3000` and register its `/api/wida/auth/callback` URI in Google. The API starts without Google credentials but keeps data routes protected, so live use requires this step.

4. Configure the Azure analyzer if you want to analyze invoices:

   ```sh
   dotnet user-secrets set "AzureDocumentIntelligence:Endpoint" "https://YOUR_RESOURCE.cognitiveservices.azure.com/"
   dotnet user-secrets set "AzureDocumentIntelligence:Key" "YOUR_API_KEY"
   ```

   These settings are only needed for invoice analysis. Reading processing runs and creating manual runs work without Azure settings, as do document and invoice endpoints. Missing or invalid Azure settings during analysis produce a persisted `Failed` run when the database save succeeds.

5. Restore dependencies and apply all database migrations:

   ```sh
   dotnet restore
   dotnet tool restore
   ASPNETCORE_ENVIRONMENT=Development dotnet ef database update --project ../Wida.Dal --startup-project .
   ```

   The local tool manifest at [.config/dotnet-tools.json](.config/dotnet-tools.json) pins `dotnet-ef` to `10.0.11`. Setting the environment explicitly makes the startup project load Development configuration and User Secrets while the EF tool runs.

6. Start the HTTP profile for the local frontend:

   ```sh
   dotnet run --launch-profile http
   ```

   The HTTP profile listens at `http://localhost:5085`. To use HTTPS, run `dotnet dev-certs https --trust` and `dotnet run --launch-profile https`. The HTTPS profile listens at `https://localhost:7127` and `http://localhost:5085`. Public TLS is handled at the frontend/proxy; the API does not redirect private proxy requests.

Start the frontend with `WIDA_API_URL=http://localhost:5085` and `WIDA_PUBLIC_ORIGIN=http://localhost:3000`, then sign in with an invited account. In Development, [Scalar](http://localhost:5085/scalar/v1) and the [OpenAPI document](http://localhost:5085/openapi/v1.json) are available on the API origin. Both require a Wida session under the default authorization policy; they are not mapped outside Development.

### Configuration

User Secrets are loaded in Development. Supply deployment values through environment variables or your secret manager:

| Configuration key | Environment variable | Purpose |
| --- | --- | --- |
| `ConnectionStrings:DefaultConnection` | `ConnectionStrings__DefaultConnection` | PostgreSQL connection; required at startup. |
| `AzureDocumentIntelligence:Endpoint` | `AzureDocumentIntelligence__Endpoint` | Azure resource endpoint; required for invoice analysis. |
| `AzureDocumentIntelligence:Key` | `AzureDocumentIntelligence__Key` | Azure API key; required for invoice analysis. |
| `Authentication:Google:ClientId` | `Authentication__Google__ClientId` | Google web OAuth client ID; required for live sign-in. |
| `Authentication:Google:ClientSecret` | `Authentication__Google__ClientSecret` | Google client secret; backend only. |
| `Authentication:PublicOrigin` | `Authentication__PublicOrigin` | Frontend origin; defaults to `http://localhost:3000` in Development and requires HTTPS outside Development. |
| `Authentication:AllowedEmails:0` | `Authentication__AllowedEmails__0` | Invited address; add indexes `1`, `2`, etc. Empty list denies sign-in. |
| `Authentication:DataProtectionKeysPath` | `Authentication__DataProtectionKeysPath` | Durable session-key directory; required outside Development. |

The web project already has `UserSecretsId` configured, so `dotnet user-secrets init` is unnecessary. Replace any sample Azure endpoint in local Development settings with your resource endpoint. An `appsettings.Local.json` file is not loaded by the current startup code.

Uploads are written beneath `<content-root>/uploads` with generated filenames and the original extension. Metadata records the exact absolute path. If copying or metadata persistence fails, the controller attempts to remove the uploaded file. The process needs write access there; preserve these files along with the database because analysis reads the stored file path. Original PDF/image files are available through `GET /api/documents/{id}/content`, with range support; add `?download=true` to download. Uploads accept PDF, PNG, JPEG and TIFF up to 20 MiB and verify extension, MIME type and format signature.

## API endpoints

JSON uses camelCase property names and string enum values such as `Uploaded`, `Pending`, and `Completed`.

| Method | Route | Behavior |
| --- | --- | --- |
| GET | `/api/documents` | List document metadata. |
| GET | `/api/documents/{id}` | Get document metadata; `404` if absent. |
| GET | `/api/documents/workspace?limit=100` | Latest document summaries with invoice and latest processing run. Limit 1–500, default 100. |
| GET | `/api/documents/{id}/content` | Inline original PDF/image bytes with ranges; `?download=true` for attachment. |
| POST | `/api/documents` | Upload multipart field `file`; returns `201` with document metadata and a Location header. Invalid files return structured `400`; files over 20 MiB return `413`. |
| POST | `/api/invoices` | Create one invoice with optional line items for an existing document; returns `201` and a Location header. |
| GET | `/api/invoices` | List saved invoices and their lines, newest first. |
| PUT | `/api/invoices/{id}` | Replace invoice fields/lines using the create request shape and unchanged document ID; returns `200`. |
| GET | `/api/invoices/{id}` | Get an invoice and its lines; `404` if absent. |
| GET | `/api/invoices/document/{documentId}` | Get the invoice for a document; `404` if absent. |
| POST | `/api/processing/documents/{documentId}` | Create a `Pending` run with processor `Manual`, version `v1`; returns `201`, or `404` if the document is absent. This does not start analysis. |
| POST | `/api/processing/documents/{documentId}/invoice` | Run Azure invoice analysis during the request and return the run with `201`, or `404` if the document is absent. |
| GET | `/api/processing/{id}` | Get a processing run and extracted fields; `404` if absent. |
| GET | `/api/processing/documents/{documentId}` | List processing runs for a document; returns an empty list if none exist. |

All route IDs are GUIDs. Processing responses contain `id`, `documentId`, `status`, `processor`, `processorVersion`, `startedAt`, `completedAt`, `errorCode`, `errorMessage`, and `extractedFields`. Each extracted field includes its raw and normalized values, confidence, source, available page/bounding data, and review flag; see the [response contract](../docs/api.md#processing-response).

## Example workflow

After signing in through the frontend, upload a document. The curl examples below show request bodies/routes; authenticated calls also need the Wida session and antiforgery cookies, plus an `X-CSRF-TOKEN` from `GET /api/auth/session` on mutations. Prefer the frontend for the interactive workflow:

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

Analysis waits for Azure to finish; there is no background worker. A run is saved as `Running`, then updated to `Completed` or `Failed`. Caught analysis errors set `errorCode` to `DOCUMENT_ANALYSIS_FAILED` and include an error message capped at 2,000 characters. A failed analysis can still return HTTP `201`, so inspect the response `status`.

The analyzer reads the first analyzed document and stores these fields when present: `InvoiceId`, `InvoiceDate`, `DueDate`, `VendorName`, `SubTotal`, `TotalTax`, and `InvoiceTotal`. An Azure response with no analyzed documents fails the run. Fields with missing confidence or confidence below `0.80` have `requiresReview: true`. Processing responses include extracted fields with typed normalized JSON values and the first bounding region's page number and polygon, when available. Raw analysis is persisted internally. Line items are not extracted.

If the request is canceled after analysis starts, the service attempts to save `Failed` with `DOCUMENT_ANALYSIS_CANCELLED` before propagating cancellation. All terminal saves use a separate 10-second token independent of request cancellation. A database outage, expired persistence timeout, or terminated process can still leave a run `Running`; there is no automatic recovery job.

Analysis sets type `Invoice` and moves an unsaved document through `Processing` to `ReviewRequired` or `Failed`. It does not create an invoice. Create the invoice separately using the document ID; saving sets document status `Saved`:

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

Invoice business-validation failures return `400` ValidationProblemDetails with camelCase field paths such as `supplierName` and `lines[0].lineAmount`. Missing documents/invoices return `404`; duplicate invoice creation or an attempt to change a saved invoice's document ID returns `409`. String lengths and four-decimal numeric precision/range are checked before saving. PUT preserves the invoice ID and creation timestamp, replaces all lines with new IDs, and refreshes `updatedAt`. `Saved` means persisted data, not approval. A saved document remains `Saved` during reanalysis even if the new run fails. If invoice saving overlaps extraction, the saved status takes precedence over the extraction status update; see [concurrency handling](../docs/architecture.md#concurrent-invoice-saving-and-extraction). Both processing creation endpoints return `404` for a missing document.

## Database migrations

Migrations live in `Wida.Dal/Migrations`:

| Migration | Tables added |
| --- | --- |
| `InitialCreate` | `Documents` |
| `AddInvoiceEntities` | `Invoices`, `InvoiceLines` |
| `AddProcessingEntities` | `ProcessingRuns`, `ExtractedFields` |
| `AddGoogleUsersAndDocumentOwnership` | `Users`, nullable document ownership and indexes |

A document has at most one invoice and can have multiple processing runs. Invoice lines belong to an invoice; extracted fields belong to a processing run. These child relationships use cascade deletion. Raw analysis, normalized extracted values, and bounding boxes use PostgreSQL `jsonb` columns.

The document-status concurrency check uses the existing `Status` column; it does not add a migration or require a schema update. The committed model snapshot includes that concurrency metadata.

Schema changes are applied explicitly, not on application startup. After changing EF models/configurations, run from `Wida.Api`:

```sh
ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add DescribeYourSchemaChange --project ../Wida.Dal --startup-project .
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update --project ../Wida.Dal --startup-project .
```

## Current limitations

- Processing extracts eight header fields and supported line items from the first analyzed document. It does not process additional analyzed documents in the same file. Shipping is entered manually; discounts can be extracted. Apply migration `20260910120000_AddInvoiceAdjustments` for the persisted shipping and discount amounts.
- Processing has no background execution, run-resume endpoint, extracted-field review endpoint, or automatic invoice creation. A repeated analysis request creates a new run. Approval/rejection and export are not implemented. The workspace endpoint returns the latest 100 documents by default (up to 500); it does not provide server search or pagination.
- Existing records with relative or duplicated storage paths are not repaired automatically. Re-upload the documents or explicitly repair their metadata to point to existing files.
- Filesystem and database writes are not transactional; process termination or unsuccessful cleanup can leave orphan uploads. A failed terminal database save can leave a processing run `Running`.
- Browser traffic uses the same-origin Next.js proxy. Authentication, ownership checks and CSRF protection apply to every data request; no cross-origin browser API policy is enabled.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Startup asks for `ConnectionStrings:DefaultConnection` | Set the secret from `Wida.Api`, or supply `ConnectionStrings__DefaultConnection` in the process environment. |
| Startup requires a public origin or Data Protection keys | Outside Development, supply the HTTPS frontend origin and a durable `Authentication:DataProtectionKeysPath`. |
| Data, Scalar, or OpenAPI returns `401` | Sign in through the frontend with an invited account. Development documentation endpoints also require a Wida session. |
| Mutation returns `400` with “Session verification failed” | Obtain a current `X-CSRF-TOKEN` from the session endpoint and send it with the matching Wida cookies. |
| Frontend mutation returns `403` | Check the request Origin against frontend `WIDA_PUBLIC_ORIGIN`; this is a proxy check. |
| Tables do not exist | Apply the committed migrations against the configured database; startup does not apply them. |
| Analysis reports a missing Azure endpoint or key | Set both analyzer settings; reading runs and creating manual runs do not require them. |
| Analysis cannot read the file | Check file existence and read access at its stored path. Re-upload or repair metadata for historical records with relative or duplicated paths. |
| Analysis returns `201` but the run is `Failed` | Inspect `errorCode` and `errorMessage`; `201` confirms that a run was created, including a failed analysis. |
| A run remains `Running` after interruption | Check API and database availability. There is no reconciliation job; retrying analysis creates a new run. |
| Scalar or OpenAPI returns `404` | Use the Development environment, as set by the supplied launch profiles. |
| HTTPS certificate is not trusted locally | Run `dotnet dev-certs https --trust` and use the supplied HTTPS launch profile. |

## Build check

From the repository root:

```sh
dotnet build Wida.slnx
dotnet test Wida.slnx
```

Verified on 10 September 2026 for API commit `2e4011e`: **81 tests passed**.

| Test area | Backing environment | What it verifies |
| --- | --- | --- |
| Processing and concurrent invoice saves | In-memory SQLite with real transactions | Run/field persistence, rollback before retry, and `Saved` precedence for both completion orders and failed/cancelled extraction. |
| Authentication HTTP tests | Simulated Google provider, real cookie/antiforgery middleware, EF InMemory | Login response validation, pilot access, CSRF, logout, and ownership at HTTP boundaries. |
| Other service/controller and extraction tests | EF InMemory, filesystem fixtures, Azure SDK response fixtures | Validation, ownership guards, upload/content behavior, and extraction mapping. |
| PostgreSQL query translation | Npgsql without opening a connection | Ownership queries translate to SQL; no live database execution. |

Tests do not require Google/Azure credentials or a running PostgreSQL server. SQLite and EF InMemory do not establish production PostgreSQL behavior or validate deployed credentials and callback URLs.

Use the example workflow or [HTTP request file](wida-api.http) to verify upload, processing status, extracted fields, and invoice creation against your configured database and Azure resource. Run the requests individually and replace their placeholder IDs with IDs returned by the API. Invoice analysis sends the uploaded document to the configured Azure resource.

A manual check should cover upload, original-file preview/range retrieval, workspace summaries, invoice creation/update/listing and retrieval by both invoice/document ID, a manual `Pending` run, and an Azure analysis response whose `status` and `extractedFields` are inspected even when HTTP is `201`. Retrieve the analysis run again to confirm its extracted values were persisted. Also save an invoice from a second request while analysis is in progress, then confirm the document remains `Saved` and the run independently reports `Completed` or `Failed`.

## Licence

Wida API is licensed under the [MIT License](../LICENSE).
