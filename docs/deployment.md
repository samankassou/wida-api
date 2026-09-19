# Deployment checklist

Production deployment follows published releases through the `production` branch. Configure the hosting branch before relying on this behavior; see [release setup and verification](releases.md#production-branch-setup).

This guide is for operators hosting their own Wida instance. The source is available under the [MIT License](../LICENSE); Google, Azure, and hosting services require your own configuration and may incur costs. The repository does not provision infrastructure or promise a hosted service.

For the optional Render Free/Supabase deployment, use the [step-by-step configuration and secrets guide](render-free.md). Its sleep behavior and classic-queue guarantees differ from an always-running VPS.

## Choose what to expose

The companion frontend offers `/demo` without authentication or backend calls. Its sample extraction results are fictional; new demo uploads use manual entry. A connected deployment requires the Next.js server proxy, ASP.NET Core API, PostgreSQL, and Google sign-in. Automatic extraction additionally requires Azure Document Intelligence and RabbitMQ, with an API instance running the embedded worker. Continuous processing requires an always-running host; the free Render profile pauses work during sleep.

## Configure the deployment

- [ ] Build and test the intended frontend/API revisions. Follow the [API setup](../Wida.Api/README.md) and [queue guide](processing-queue.md).
- [ ] Set `Authentication__PublicBeta` explicitly. It defaults to `true`; `false` requires an allowlist.
- [ ] Override `Authentication__AdminEmail` with your own verified address or an empty value. The checked-in value is maintainer-specific. Existing persisted roles are managed separately; see [roles](public-beta.md#user-and-administrator-profiles).
- [ ] Supply database, Google, Azure, and RabbitMQ credentials through protected environment settings or a secret manager. Do not publish them in source, screenshots, or logs.
- [ ] Set the same HTTPS origin in API `Authentication__PublicOrigin` and frontend `WIDA_PUBLIC_ORIGIN`. Register its `/api/wida/auth/callback` with Google. Set server-only `WIDA_API_URL` to the complete API origin, including its scheme and without `/api`. For Render, use HTTPS and the shared proxy secret.
- [ ] Configure [trusted client-IP forwarding](public-beta.md#trusted-client-ip-required-for-live-production), including `WIDA_CLIENT_IP_HEADER` and either a shared proxy secret or API `RateLimiting__TrustedProxies__0`. Restrict access to the API and internal services to the required peers.
- [ ] Persist originals (local directory or private Supabase bucket), PostgreSQL, RabbitMQ data, and session keys (protected directory or encrypted Database provider). All workers must share storage; preserve local paths and session decryption certificates across releases.
- [ ] Back up data before applying all committed migrations. Supply production configuration to the migration command; do not use Development settings for a public server. Apply migrations before restarting the updated API/worker, or use `Database__ApplyMigrations=true` for the single-instance Render profile, which migrates before workers start.
- [ ] Keep `ProcessingQueue__Enabled=true` on at least one API instance; use continuous hosting if processing must continue without incoming traffic. Check broker permissions, durable storage, and acknowledgement timeout using the queue guide.

## Verify before opening access

- [ ] Exercise Google login, upload, original preview, Azure extraction, correction, save, reload, and CSV export on the deployed domain using fictional invoices.
- [ ] Use two ordinary accounts in separate browser profiles to verify isolation of documents, originals, invoices, and processing history.
- [ ] Test session expiry, logout, invalid uploads, exhausted credits, rejected CSRF requests, and failed analysis. Test quota behavior with a `User`, since `Admin` bypasses Wida quotas.
- [ ] Verify restart recovery with PostgreSQL and RabbitMQ, including an analysis in progress. Do not manually clear Azure submission markers to retry uncertain work.
- [ ] Restore a backup in an isolated environment and confirm both invoice data and originals are usable. Plan application rollback together with database-schema compatibility.
- [ ] Check mobile layout, keyboard navigation, loading/error states, and public `/demo` access.

Record the revisions, environment, date, and outcomes. Run the checks against the revisions being released; previous results are not a release certification.

## Operate and explain the service

Monitor availability, API/worker errors, failed queue messages, disk capacity, and Azure consumption. `GET /healthz` is an anonymous HTTP liveness probe; it does not establish database, storage, Azure or broker availability. Arrange dependency and worker monitoring separately. Configure provider spending alerts and an operator procedure for pausing analysis. Disabling every worker pauses consumption, but does not disable admission: queued work may still be accepted.

The public trial allows 4 lifetime analysis pages per ordinary account, 10 documents, and a shared 400-page monthly admission budget. Administrators bypass application quotas and can exceed that monthly budget; provider limits still apply. External calls using the same Azure resource are not included in Wida's ledger. See [budget and credit operations](public-beta.md).

Publish an instance-specific explanation of who operates the service, how users can contact them, what Google identity data is stored, and that connected analysis sends originals to Azure. Ordinary users' originals expire after 30 days, but invoice records, extracted data, account details, and processing/credit history remain. Administrators are exempt from original expiry. There is no account/document deletion endpoint; define and verify an operator deletion process before promising one. Do not describe original expiry as deletion of all personal data.

Use fictional documents for public demonstrations. Describe extraction as assistance requiring human verification; saved invoices and field checks do not constitute a formal approval or server-side review audit trail.
