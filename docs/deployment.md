# Deployment checklist

This guide is for operators hosting their own Wida instance. The source is available under the [MIT License](../LICENSE); Google, Azure, and hosting services require your own configuration and may incur costs. The repository does not provision infrastructure or promise a hosted service.

## Choose what to expose

The companion frontend offers `/demo` without authentication or backend calls. Its sample extraction results are fictional; new demo uploads use manual entry. A connected deployment requires the Next.js server proxy, ASP.NET Core API, PostgreSQL, and Google sign-in. Automatic extraction additionally requires Azure Document Intelligence and RabbitMQ, with at least one API instance running the embedded worker continuously.

## Configure the deployment

- [ ] Build and test the intended frontend/API revisions. Follow the [API setup](../Wida.Api/README.md) and [queue guide](processing-queue.md).
- [ ] Set `Authentication__PublicBeta` explicitly. It defaults to `true`; `false` requires an allowlist.
- [ ] Override `Authentication__AdminEmail` with your own verified address or an empty value. The checked-in value is maintainer-specific. Existing persisted roles are managed separately; see [roles](public-beta.md#user-and-administrator-profiles).
- [ ] Supply database, Google, Azure, and RabbitMQ credentials through protected environment settings or a secret manager. Do not publish them in source, screenshots, or logs.
- [ ] Set the same HTTPS origin in API `Authentication__PublicOrigin` and frontend `WIDA_PUBLIC_ORIGIN`. Register its `/api/wida/auth/callback` with Google. Set server-only `WIDA_API_URL` to the private API origin.
- [ ] Configure [trusted client-IP forwarding](public-beta.md#trusted-client-ip-required-for-live-production), including `WIDA_CLIENT_IP_HEADER` and API `RateLimiting__TrustedProxies__0`. Restrict access to the API and internal services to the required peers.
- [ ] Persist the upload directory, PostgreSQL, RabbitMQ data, and `Authentication__DataProtectionKeysPath`. All workers need access to originals at their recorded absolute paths. Preserve those paths across releases; keys must be shared across API replicas.
- [ ] Back up data before applying all committed migrations. Supply production configuration to the migration command; do not use Development settings for a public server. Apply migrations before restarting the updated API/worker.
- [ ] Keep `ProcessingQueue__Enabled=true` on at least one continuously running API instance. Check broker permissions, durable storage, and acknowledgement timeout using the queue guide.

## Verify before opening access

- [ ] Exercise Google login, upload, original preview, Azure extraction, correction, save, reload, and CSV export on the deployed domain using fictional invoices.
- [ ] Use two ordinary accounts in separate browser profiles to verify isolation of documents, originals, invoices, and processing history.
- [ ] Test session expiry, logout, invalid uploads, exhausted credits, rejected CSRF requests, and failed analysis. Test quota behavior with a `User`, since `Admin` bypasses Wida quotas.
- [ ] Verify restart recovery with PostgreSQL and RabbitMQ, including an analysis in progress. Do not manually clear Azure submission markers to retry uncertain work.
- [ ] Restore a backup in an isolated environment and confirm both invoice data and originals are usable. Plan application rollback together with database-schema compatibility.
- [ ] Check mobile layout, keyboard navigation, loading/error states, and public `/demo` access.

Record the revisions, environment, date, and outcomes. Historical test counts in this repository are not a release certification.

## Operate and explain the service

Monitor availability, API/worker errors, failed queue messages, disk capacity, and Azure consumption. There is no built-in health-check endpoint; arrange host/service monitoring appropriate to your infrastructure. Configure provider spending alerts and an operator procedure for pausing analysis. Disabling every worker pauses consumption, but does not disable admission: queued work may still be accepted.

The public trial allows 4 lifetime analysis pages per ordinary account, 10 documents, and a shared 400-page monthly admission budget. Administrators bypass application quotas and can exceed that monthly budget; provider limits still apply. External calls using the same Azure resource are not included in Wida's ledger. See [budget and credit operations](public-beta.md).

Publish an instance-specific explanation of who operates the service, how users can contact them, what Google identity data is stored, and that connected analysis sends originals to Azure. Ordinary users' originals expire after 30 days, but invoice records, extracted data, account details, and processing/credit history remain. Administrators are exempt from original expiry. There is no account/document deletion endpoint; define and verify an operator deletion process before promising one. Do not describe original expiry as deletion of all personal data.

Use fictional documents for public demonstrations. Describe extraction as assistance requiring human verification; saved invoices and field checks do not constitute a formal approval or server-side review audit trail.
