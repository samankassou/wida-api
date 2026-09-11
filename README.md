# Wida API

Wida is an ASP.NET Core API for uploading documents, recording invoices and line items, and tracking invoice analysis with Azure Document Intelligence. It targets .NET 10, stores application data in PostgreSQL through Entity Framework Core, and saves uploaded files on the API filesystem.

## Documentation

| Guide | Contents |
| --- | --- |
| [Setup and development](Wida.Api/README.md) | Prerequisites, local configuration, running the API, migrations, example workflow, and troubleshooting. |
| [API reference](docs/api.md) | Routes, request and response fields, validation, status values, and processing behavior. |
| [Google sign-in](docs/authentication.md) | OAuth setup, invited users, session protection and document ownership. |
| [Architecture](docs/architecture.md) | Project responsibilities, dependencies, data relationships, and persistence. |
| [HTTP requests](Wida.Api/wida-api.http) | Requests to run individually in an editor with `.http` support after setup. |

## Capabilities

- Upload PDF/PNG/JPEG/TIFF files up to 20 MiB, retrieve metadata, and preview/download originals with byte-range support.
- Load a bounded document workspace with saved invoices and latest extraction runs.
- Create, update, list, and retrieve one invoice per document, with optional line items and field-specific validation errors.
- Record a manual processing run or request Azure's `prebuilt-invoice` analysis.
- Retrieve processing status, extracted fields, confidence scores, and failure information.
- Inspect the API through OpenAPI and Scalar in Development with an authenticated session.

Analysis is queued durably in RabbitMQ and processed by a .NET background worker. The endpoint returns 202 immediately; clients poll the run for completion. See [queue setup and recovery](docs/processing-queue.md). It records extracted fields and updates unsaved documents through processing/review/failure states. Saving invoice data sets `Saved`; reanalysis preserves that state even when saving occurs while extraction is still running. Analysis extracts invoice headers and line items but does not create an invoice. Approval and export are not implemented. The API requires a Google-backed Wida session and restricts each user to their own documents. See [Google sign-in and pilot access](docs/authentication.md).

## Getting started

You need the .NET 10 SDK and a PostgreSQL database. Invoice analysis also requires an Azure Document Intelligence endpoint and API key; reading processing runs and creating manual runs do not. Follow the [local setup guide](Wida.Api/README.md#local-setup) to configure User Secrets, restore dependencies, apply migrations, configure Google pilot access, and start the HTTP profile for the local frontend.

The local HTTP profile uses `http://localhost:5085`; the optional HTTPS profile also exposes `https://localhost:7127`. Sign in through the frontend before using protected data routes. Development API documentation is available at `/scalar/v1` and `/openapi/v1.json` on the selected API origin and is also covered by the authenticated-user policy. The [request examples](Wida.Api/wida-api.http) illustrate payloads; add session cookies and a CSRF token before sending mutations.

To build and run the automated tests from the repository root:

```sh
dotnet build Wida.slnx
dotnet test Wida.slnx
```

`Wida.Tests` exercises the analysis workflow with Azure response fixtures, SQLite for transactional processing/concurrency tests, and EF InMemory for the remaining service fixtures. The [build check](Wida.Api/README.md#build-check) also describes verification against your own PostgreSQL database and Azure resource.

## Verification baseline

On 10 September 2026, all **81 API tests** passed for commit `2e4011e`. Processing/concurrency tests use SQLite transactions so failed saves roll back before retry. Authentication HTTP tests use simulated Google responses with real cookie/antiforgery middleware, while other service fixtures use EF InMemory. No live Google, PostgreSQL, or Azure end-to-end result is implied by these tests.

## Licence

This project is licensed under the [MIT License](LICENSE).

Shipping is entered manually and discount can prefill from Azure `TotalDiscount`.
