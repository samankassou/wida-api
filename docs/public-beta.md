# Public beta

`Authentication:PublicBeta=true` (the checked-in default) admits any Google account whose email is verified. Set it to `false` to restore `Authentication:AllowedEmails`. Google OIDC credentials, HTTPS public origin, CSRF and owner isolation remain required. `/demo` on the frontend never calls the API or Azure.

## Limits

- Each Google subject receives 4 lifetime analysis pages; reconnecting does not renew them.
- The application reserves at most 400 pages per UTC calendar month, shared by all accounts. Keep the Azure F0 resource dedicated to this deployment; external calls are not visible to the application.
- Uploads: 4 MiB, PDF/PNG/JPEG/classic TIFF, 1–2 pages, 10 document records per account. PDF pages are counted with PdfPig; TIFF image directories are counted and cycles/truncation rejected. Old documents without a page count must be uploaded again before analysis.
- One queued/running analysis per account; global queue capacity remains 100. RabbitMQ single active consumer and the existing two-second interval govern Azure requests.
- Reservations and user debits commit together with queue admission, under PostgreSQL transaction advisory lock `73190421`. Broker failure rolls back admission and credits. Failed or uncertain submitted analyses remain charged conservatively; refunds are manual grants.
- A queued submission crossing a UTC month boundary reserves capacity in the new month before submitting. The old reservation is retained conservatively. Legacy unreserved queue jobs fail without calling Azure.
- SHA-256 deduplicates imports within each owner. Reupload returns the existing document and retains its invoice/history. Normal analysis POST reuses its completed result. `?reanalyze=true` requests a paid reanalysis; active jobs remain idempotent.
- Originals stop being downloadable after 30 days from upload; hourly cleanup removes expired originals unless analysis is active. Invoice records and credit/history data remain. Reuploading an expired original restores that original in the existing record.
- Per API instance: 120 reads/minute and 12 writes/minute per account, plus 600 requests/minute per connection IP. The API does not trust arbitrary forwarded IP headers. Behind the Next.js proxy, the IP limit is shared by the proxy connection: configure client-IP rate limiting at your trusted public ingress as well. Database quotas remain global across replicas.

## Deployment

Run migrations **before restarting API and worker**:

```sh
dotnet ef database update --project Wida.Dal --startup-project Wida.Api
```

Do not run `EnsureCreated` on an existing database. Existing users receive a 4-page allowance through the migration. Review existing Azure consumption before opening the beta: initialize the current `AnalysisBudgets` row with pages already spent on this resource, so the launch does not incorrectly start its global budget at zero. Back up the database and originals before applying production migrations.

## Credit requests

Users submit an idempotent request in the workspace. No email is sent. Inspect and grant from a trusted operator terminal with the normal database configuration:

```sh
dotnet run --project Wida.Api -- --list-credit-requests true
dotnet run --project Wida.Api -- --grant-credit-user USER_UUID --grant-credit-pages 4
```

A grant adds pages and clears the request. It never increases the global monthly budget. Each invocation is an additive grant: do not rerun it without checking the account balance.

## Verification

Run `dotnet test Wida.slnx`. PostgreSQL/RabbitMQ integration tests require disposable services in `WIDA_QUEUE_TEST_POSTGRES` and `WIDA_QUEUE_TEST_RABBITMQ`. Frontend: `pnpm lint`, `pnpm test`, `pnpm build`.

## Managed anti-bot challenge

Optionally configure `Turnstile:SiteKey` and `Turnstile:SecretKey` together, restricting the widget to your public hostname. The workspace displays Cloudflare’s managed widget, which decides whether interaction is needed. The API verifies success, action `trial` and the public hostname, then issues an encrypted, owner-bound HttpOnly cookie valid for 10 minutes. Imports and analysis require it when enabled; reads/manual editing remain available. Rate limits and quotas still apply. Share the existing ASP.NET Data Protection key ring across API replicas. Without these keys the challenge is disabled; rate and budget limits remain enforced. See [Cloudflare validation documentation](https://developers.cloudflare.com/turnstile/get-started/server-side-validation/).

## User and administrator profiles

New accounts default to `User`. `Authentication:AdminEmail` is set to `samankassoufoulla@gmail.com`: its verified Google login grants the persisted `Admin` role, including for an already registered account. Sign out and sign back in after deploying the role migration to activate this initial assignment. Roles are read from PostgreSQL on every authenticated request, so a role change updates existing sessions; the browser refreshes its role on focus.

Admins have no Wida page allowance, monthly admission ceiling, active-job count, file-size/page-count upload quota, document-count quota, rate limiter, CAPTCHA requirement or original expiry. File-format validation, authentication, CSRF, ownership isolation, idempotency and Azure service constraints still apply. Admins manage their own workspace; this role does not expose other accounts' private documents.

Azure consumption by admins is still recorded in the shared monthly ledger, so public users cannot reserve capacity already consumed by admins. Admin admissions may exceed 400 pages. Azure F0 itself still imposes two analyzed pages and 4 MiB: larger admin uploads can be saved and entered manually, but automatic analysis is refused before submission to prevent silent truncation. After configuring an actual S0 resource, set `AzureDocumentIntelligence:Tier=S0`. Merely changing this setting does not upgrade Azure. The existing worker cadence is retained for provider reliability.

Apply the `UserRoles` migration before running the updated API/worker. To change another registered account's role:

```sh
dotnet run --project Wida.Api -- --set-user-role person@example.com --role Admin
dotnet run --project Wida.Api -- --set-user-role person@example.com --role User
```

To demote the configured initial admin, first remove/change `Authentication:AdminEmail`, then use the second command; otherwise the next verified login grants Admin again. No HTTP signup/profile payload can assign roles. Files already purged before promotion cannot be recovered by changing a role.
