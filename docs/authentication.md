# Google sign-in and access modes

Wida uses Google OpenID Connect authorization-code flow with PKCE. The API validates Google's response, requires a verified email and, when public beta is disabled, checks the invitation allowlist, and creates a local user linked to Google's stable `sub` identifier. It issues its own HttpOnly session cookie. Google tokens are not saved in the browser or session.

## Local configuration

1. In [Google Auth Platform](https://console.cloud.google.com/auth/clients), create a **Web application** OAuth client. Configure branding/audience and any test users requested by Google.
2. Register this exact authorized redirect URI:

   ```text
   http://localhost:3000/api/wida/auth/callback
   ```

   Set this in **Authorized redirect URIs**, not **Authorized JavaScript origins**, and save the OAuth client. The public callback goes through Next.js to the API's internal `/api/auth/callback` route. A Google `redirect_uri_mismatch` error means the requested callback does not match a registered URI for that client; compare the scheme, host, port and full path shown in Google's error details.

3. From `wida-api/Wida.Api`, configure User Secrets (replace sample values):

   ```sh
   dotnet user-secrets set "Authentication:Google:ClientId" "YOUR_CLIENT_ID.apps.googleusercontent.com"
   dotnet user-secrets set "Authentication:Google:ClientSecret" "YOUR_CLIENT_SECRET"
   dotnet user-secrets set "Authentication:PublicOrigin" "http://localhost:3000"
   dotnet user-secrets set "Authentication:PublicBeta" "false"
   dotnet user-secrets set "Authentication:AllowedEmails:0" "YOUR_INVITED_EMAIL"
   dotnet user-secrets set "Authentication:AdminEmail" "YOUR_ADMIN_EMAIL"
   ```

   Add invited addresses using indexes `1`, `2`, etc. The application does not send invitation emails. When `Authentication:PublicBeta=false`, an empty allowlist denies all sign-ins and removing an address invalidates its session. Public beta mode (default) accepts all verified Google emails; restart after changing User Secrets.

4. Apply the migrations and launch the API:

   ```sh
   dotnet tool restore
   ASPNETCORE_ENVIRONMENT=Development dotnet ef database update --project ../Wida.Dal --startup-project .
   dotnet run --launch-profile http
   ```

5. Configure and restart the frontend:

   ```dotenv
   WIDA_API_URL=http://localhost:5085
   WIDA_PUBLIC_ORIGIN=http://localhost:3000
   ```

6. Open `http://localhost:3000` in a regular browser and select **Continuer avec Google** with the invited account. The app requests `openid`, `email`, and `profile`.

Without Google credentials the API starts with data endpoints protected and the frontend explains that sign-in is not configured. There is no anonymous live mode. The browser-only `/demo` route remains available even when `WIDA_API_URL` is configured.

## Deployment

| API setting | Environment variable |
| --- | --- |
| `Authentication:Google:ClientId` | `Authentication__Google__ClientId` |
| `Authentication:Google:ClientSecret` | `Authentication__Google__ClientSecret` |
| `Authentication:PublicOrigin` | `Authentication__PublicOrigin` |
| `Authentication:PublicBeta` | `Authentication__PublicBeta` |
| `Authentication:AdminEmail` | `Authentication__AdminEmail` |
| `Authentication:AllowedEmails:0` | `Authentication__AllowedEmails__0` |
| `Authentication:DataProtectionKeysPath` | `Authentication__DataProtectionKeysPath` |

Use the same HTTPS frontend origin in API `Authentication:PublicOrigin` and frontend `WIDA_PUBLIC_ORIGIN`. Register its `/api/wida/auth/callback` URL in Google. The API can use private HTTP behind the frontend: public request scheme and Secure cookies derive from server configuration, never untrusted forwarded headers. Keep the API private behind the frontend in deployment.

Outside Development, startup requires an HTTPS public origin and persistent Data Protection key directory. Restrict its filesystem permissions and protect it with encrypted storage. Replicas must share compatible persistent keys. Uploads, PostgreSQL, and RabbitMQ also require durable storage. Configure [trusted client-IP forwarding](public-beta.md#trusted-client-ip-required-for-live-production); it is required for live production. See the [deployment checklist](deployment.md).

The session lasts eight hours with sliding renewal. Cookies use HttpOnly and SameSite, plus Secure under HTTPS. Every authenticated request rechecks the user and, in invitation mode, the invitation. Logout removes the browser's Wida session; it does not sign the person out of Google.

## API contract

| Method | Route | Behavior |
| --- | --- | --- |
| GET | `/api/auth/session` | Public uncached `{authenticated, googleConfigured, user: {id,email,displayName,role} or null, csrfToken}`; sets antiforgery cookie. |
| GET | `/api/auth/login?returnUrl=/` | Public Google challenge; accepts only root workspace return paths. |
| GET | `/api/auth/callback` | Internal callback handled by OIDC middleware. Public callback is `/api/wida/auth/callback`. |
| POST | `/api/auth/logout` | Requires session cookie and `X-CSRF-TOKEN`; clears Wida session. |

The Development-only Scalar and OpenAPI routes are also protected by the default authenticated-user policy.

All document, original-file, invoice and processing routes require a Wida session; unauthenticated requests return `401`. POST/PUT controller routes validate `X-CSRF-TOKEN` against the cookie and user; missing/invalid tokens return `400`. The Next.js proxy also rejects mutations whose Origin is missing or different from the configured public origin. Clients obtain the token from the session endpoint after sign-in.

The proxy forwards only `Wida.*` cookies and preserves separate Set-Cookie headers. It returns allowed Google navigation redirects without following them, rejecting other redirects. Session and data responses are uncached.

## Document ownership

New uploads receive the authenticated Wida user ID on the server. Query filters cover documents, invoices, invoice lines, processing runs and extracted fields. Write guards reject foreign owners, foreign parent references and ownership reassignment. Other users' IDs reveal no record, including downloads and analysis requests.

Every document requires an `OwnerUserId` foreign key. There is no ownership-transfer endpoint.

Live drafts are scoped to the user ID and browser-tab session. Expiry preserves them for the same user. Explicit logout warns about unsaved drafts and clears them in the current tab. Other tabs hide their workspace while retaining owner-scoped recovery data. Edited values survive extraction retries, but checks are tied to a specific run and reset when it changes. Stored drafts must match the current format, including an explicit extraction run ID or null before a completed extraction.

## Verification

`dotnet test Wida.slnx` includes HTTP tests with a simulated OIDC provider, signed ID tokens, real cookie middleware and antiforgery. Coverage includes PKCE/state/nonce, verified-email/invitation checks, anonymous denial, logout, revoked invitations, foreign document access and user-bound CSRF. Domain tests cover child queries and write guards.

These authentication HTTP tests use EF InMemory and simulated Google responses. The separate processing/concurrency suite uses SQLite transactions; see [test coverage](../Wida.Api/README.md#build-check). Real login requires Google credentials and a verified account (invited when public beta is disabled). Verify the deployed callback and isolation with two real accounts before opening the pilot.

References: [Google OpenID Connect](https://developers.google.com/identity/openid-connect/openid-connect), [ASP.NET Core OIDC](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication?view=aspnetcore-10.0).

Public beta is enabled by default through `Authentication:PublicBeta=true`. See [trial quotas and deployment](public-beta.md).
