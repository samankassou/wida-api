# Wida API

Wida is an ASP.NET Core API for uploading documents, recording invoices and line items, and tracking invoice analysis with Azure Document Intelligence. It targets .NET 10, stores application data in PostgreSQL through Entity Framework Core, and saves uploaded files on the API filesystem.

## Documentation

| Guide | Contents |
| --- | --- |
| [Setup and development](Wida.Api/README.md) | Prerequisites, local configuration, running the API, migrations, example workflow, and troubleshooting. |
| [API reference](docs/api.md) | Routes, request and response fields, validation, status values, and processing behavior. |
| [Architecture](docs/architecture.md) | Project responsibilities, dependencies, data relationships, and persistence. |
| [HTTP requests](Wida.Api/wida-api.http) | Requests to run individually in an editor with `.http` support after setup. |

## Capabilities

- Upload PDF/PNG/JPEG/TIFF files up to 20 MiB, retrieve metadata, and preview/download originals with byte-range support.
- Load a bounded document workspace with saved invoices and latest extraction runs.
- Create, update, list, and retrieve one invoice per document, with optional line items and field-specific validation errors.
- Record a manual processing run or request Azure's `prebuilt-invoice` analysis.
- Retrieve processing status, extracted fields, confidence scores, and failure information.
- Explore the API through OpenAPI and Scalar in Development.

Analysis runs during the HTTP request. It records extracted fields and updates unsaved documents through processing/review/failure states. Saving invoice data sets `Saved`; reanalysis preserves that state. Analysis does not create an invoice or extract line items. Approval and export are not implemented. The API currently has no authentication or authorization configured.

## Getting started

You need the .NET 10 SDK and a PostgreSQL database. Invoice analysis also requires an Azure Document Intelligence endpoint and API key; reading processing runs and creating manual runs do not. Follow the [local setup guide](Wida.Api/README.md#local-setup) to configure User Secrets, restore dependencies, apply migrations, and start the HTTPS profile.

The development HTTPS address is `https://localhost:7127`. Once the application is running, use [Scalar](https://localhost:7127/scalar/v1) or the [request examples](Wida.Api/wida-api.http).

To build and run the automated tests from the repository root:

```sh
dotnet build Wida.slnx
dotnet test Wida.slnx
```

`Wida.Tests` exercises the analysis workflow with Azure response fixtures and an in-memory database. The [build check](Wida.Api/README.md#build-check) also describes verification against your own PostgreSQL database and Azure resource.

## Licence

This project is licensed under the [MIT License](LICENSE).
