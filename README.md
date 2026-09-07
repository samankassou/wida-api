# Wida API

Wida is an ASP.NET Core API for uploading documents, recording invoices and line items, and tracking invoice analysis with Azure Document Intelligence. It targets .NET 10, stores application data in PostgreSQL through Entity Framework Core, and saves uploaded files on the API filesystem.

The current Azure integration is unfinished: DAL references analysis contracts in BLL without a project reference, which blocks compilation, and uploads persist an incorrect analysis file path. See the [known issues](Wida.Api/README.md#current-limitations) before following the setup or request examples.

## Documentation

| Guide | Contents |
| --- | --- |
| [Setup and development](Wida.Api/README.md) | Prerequisites, local configuration, running the API, migrations, example workflow, and troubleshooting. |
| [API reference](docs/api.md) | Routes, request and response fields, validation, status values, and processing behavior. |
| [Architecture](docs/architecture.md) | Project responsibilities, dependencies, data relationships, and persistence. |
| [HTTP requests](Wida.Api/wida-api.http) | Requests to run individually in an editor with `.http` support after setup. |

## Capabilities

- Upload a document and retrieve its metadata.
- Create and retrieve one invoice per document, with optional line items.
- Record a manual processing run or request Azure's `prebuilt-invoice` analysis.
- Retrieve processing status and failure information.
- Explore the API through OpenAPI and Scalar in Development.

Analysis runs during the HTTP request. It records extracted fields but does not create an invoice, extract line items, or update document status. The API currently has no authentication or authorization configured.

## Getting started

You need the .NET 10 SDK and a PostgreSQL database. Processing endpoints also require an Azure Document Intelligence endpoint and API key. Follow the [local setup guide](Wida.Api/README.md#local-setup) to configure User Secrets, restore dependencies, apply migrations, and start the HTTPS profile.

The development HTTPS address is `https://localhost:7127`. Once the application is running, use [Scalar](https://localhost:7127/scalar/v1) or the [request examples](Wida.Api/wida-api.http).

To check compilation from the repository root:

```sh
dotnet build Wida.slnx
```

There is no automated test project in the solution. The [build check](Wida.Api/README.md#build-check) describes the current build blocker and manual verification steps.

## Licence

This project is licensed under the [MIT License](LICENSE).
