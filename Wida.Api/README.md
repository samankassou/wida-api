# API setup and development

[Project overview](../README.md) · [API reference](../docs/api.md) · [Architecture](../docs/architecture.md) · [Deployment](../docs/deployment.md)

The API targets .NET 10. PostgreSQL stores accounts, invoices and processing state; originals use local storage or a private Supabase bucket. Google handles sign-in, while Azure analysis runs through an embedded RabbitMQ worker. For Render and Vercel, use the [deployment and secrets guide](../docs/render-free.md).

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

3. Configure [Google sign-in and pilot access](../docs/authentication.md#local-configuration), including Google credentials, the public origin, access mode, and your own administrator identity. The linked local example explicitly disables public beta and configures an invitation allowlist. For the standard local frontend use `http://localhost:3000` and register its `/api/wida/auth/callback` URI in Google. The API starts without Google credentials but keeps data routes protected, so live use requires this step.

4. Configure the Azure analyzer if you want to analyze invoices:

   ```sh
   dotnet user-secrets set "AzureDocumentIntelligence:Endpoint" "https://YOUR_RESOURCE.cognitiveservices.azure.com/"
   dotnet user-secrets set "AzureDocumentIntelligence:Key" "YOUR_API_KEY"
   ```

   These settings are only needed for invoice analysis. Reading processing runs and creating manual runs work without Azure settings, as do document and invoice endpoints. Missing or invalid Azure settings during analysis produce a persisted `Failed` run when the database save succeeds.

5. Configure RabbitMQ for extraction using the [queue setup guide](../docs/processing-queue.md#local-development). The worker runs inside the API by default. For manual-entry-only development, set `ProcessingQueue:Enabled=false`; automatic analysis still needs a broker and an enabled worker.

6. Restore dependencies and apply all database migrations:

   ```sh
   dotnet restore
   dotnet tool restore
   ASPNETCORE_ENVIRONMENT=Development dotnet ef database update --project ../Wida.Dal --startup-project .
   ```

   The local tool manifest at [.config/dotnet-tools.json](.config/dotnet-tools.json) pins `dotnet-ef` to `10.0.11`. Setting the environment explicitly makes the startup project load Development configuration and User Secrets while the EF tool runs.

7. Start the HTTP profile for the local frontend:

   ```sh
   dotnet run --launch-profile http
   ```

   The HTTP profile listens at `http://localhost:5085`. To use HTTPS, run `dotnet dev-certs https --trust` and `dotnet run --launch-profile https`. The HTTPS profile listens at `https://localhost:7127` and `http://localhost:5085`. Public TLS is handled at the frontend/proxy; the API does not redirect private proxy requests.

Start the frontend with `WIDA_API_URL=http://localhost:5085` and `WIDA_PUBLIC_ORIGIN=http://localhost:3000`, then sign in with an invited account. In Development, [Scalar](http://localhost:5085/scalar/v1) and the [OpenAPI document](http://localhost:5085/openapi/v1.json) are available on the API origin. Both require a Wida session under the default authorization policy; they are not mapped outside Development.

## Configuration

Development loads User Secrets. Production uses environment variables or a secret manager; ASP.NET does not automatically read `.env` or `appsettings.Local.json`. The web project already has a `UserSecretsId`.

- PostgreSQL: `ConnectionStrings__DefaultConnection`, in Npgsql `Host=…;Port=…;Database=…;Username=…;Password=…` format.
- Sign-in and session protection: [authentication settings](../docs/authentication.md#deployment).
- Originals: `Storage__Provider=Local` (default) or `Supabase`. Local storage requires a writable, persistent `<content-root>/uploads` directory. Supabase requires its project URL, private bucket and server key; temporary uploads still need local write access.
- Worker: `ProcessingQueue__Enabled=true` by default, plus `RabbitMQ__Uri`. [Queue settings and recovery](../docs/processing-queue.md).
- Azure: endpoint, key and actual resource tier. F0 limits apply even to administrators; changing the tier setting does not upgrade the resource.
- Public beta, credits, roles and client-IP trust: [operator guide](../docs/public-beta.md).

The complete [production environment example](../.env.example) covers the Render profile, including encrypted database session keys. Do not reuse production credentials in disposable tests.

## Database migrations

Apply **all** migrations in [Wida.Dal/Migrations](../Wida.Dal/Migrations):

| Migration | Purpose |
| --- | --- |
| `InitialCreate` | Ownership, documents, invoices/lines and processing/extracted fields |
| `PublicTrial` | Credits, monthly budget and file page/hash metadata |
| `UserRoles` | Persisted user/admin roles |
| `RemoteStorageAndSessionKeys` | Original byte size and Data Protection keys |

From `Wida.Api` for local development:

```sh
dotnet tool restore
ASPNETCORE_ENVIRONMENT=Development dotnet ef database update --project ../Wida.Dal --startup-project .
```

After changing the EF model, create a migration from that same directory:

```sh
ASPNETCORE_ENVIRONMENT=Development dotnet ef migrations add DescribeYourSchemaChange --project ../Wida.Dal --startup-project .
```

Production commands must use production configuration. Back up before changing an existing database. `Database__ApplyMigrations=true` applies migrations at startup before workers; it is enabled in the single-instance Render profile. Otherwise migrations are explicit. Do not use `EnsureCreated` to upgrade an existing schema.

Changing storage provider does not transfer existing originals or repair their locations. Follow the migration notes in the [Render guide](../docs/render-free.md).

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Startup requires database configuration | Supply `ConnectionStrings__DefaultConnection` in Npgsql format. |
| Production requires origin or session keys | Set a complete HTTPS frontend origin and either persistent filesystem keys or the Database provider with a PFX/password. |
| Session/proxy fails in deployment | Check HTTPS URLs, matching proxy secrets, ingress IP header and redeployment. See [Render troubleshooting](../docs/render-free.md#dépannage). |
| Protected data or Development docs return `401` | A Wida session is required. Sign in through the frontend; direct API tools need the matching cookie and proxy credentials where configured. |
| Mutation returns `400` for session verification | Send a fresh session CSRF token with its matching cookies. |
| Mutation returns `403` | Check frontend request Origin and proxy trust. |
| Tables do not exist | Apply all migrations or enable startup migrations for the single-instance profile. |
| Analysis fails or remains active | Inspect its error fields, worker/broker logs and [recovery procedure](../docs/processing-queue.md). Do not clear Azure submission markers. |
| Original unavailable | Check retention, the configured storage provider, object/file existence and permissions. |
| Scalar/OpenAPI returns `404` | These routes exist only in Development. |
| Local HTTPS certificate is not trusted | Run `dotnet dev-certs https --trust`, or use the HTTP launch profile locally. |

The [API reference](../docs/api.md) is the source for routes, payloads, validation and status codes; [wida-api.http](wida-api.http) contains request examples.

## Build check

From the repository root:

```sh
dotnet build Wida.slnx
dotnet test Wida.slnx
```

Tests cover validation, ownership, local/remote storage, session persistence, simulated Google callbacks, queue recovery and Azure response mapping. SQLite exercises transactions; EF InMemory fixtures do not establish PostgreSQL behavior. PostgreSQL/RabbitMQ integration tests require disposable services configured through `WIDA_QUEUE_TEST_POSTGRES` and `WIDA_QUEUE_TEST_RABBITMQ`; see [queue verification](../docs/processing-queue.md#verification).

For the actual container startup/restart check with temporary PostgreSQL and RabbitMQ:

```sh
python3 deploy/smoke-test.py
```

Docker, Python 3 and OpenSSL are required. This test builds the image and checks migrations, encrypted session keys, proxy protection and worker reconnection. It uses temporary local originals and does not contact Google, Supabase or Azure. See [test details](../docs/render-free.md#test-docker-local-reproductible).

Verify the deployed Google → workspace → upload → analysis → save → reload flow with fictional documents and two accounts. Automated fixtures and a liveness probe alone do not validate that complete integration.
