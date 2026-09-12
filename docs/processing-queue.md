# RabbitMQ invoice analysis

RabbitMQ distributes analysis work. A .NET `BackgroundService` consumes messages; PostgreSQL stores processing history, ownership, results and the Azure operation ID. There is no database queue scan, database worker election, or database outbox dispatcher.

## Local development

Use a RabbitMQ service with quorum queues, publisher confirms, single active consumer, and dead lettering. From `Wida.Api`, configure your own broker credentials:

```sh
dotnet user-secrets set "RabbitMQ:Uri" "amqp://USER:URL_ENCODED_PASSWORD@127.0.0.1:5672/%2F"
```

Replace the host, port, credentials, and virtual host for your installation. `%2F` selects the default `/` virtual host. The worker is enabled by default and declares its queues automatically. If your broker runs in a container, publish its port only to the interface needed by the API and persist its data. No particular local service manager is required.

## Connect to a native RabbitMQ service

Wida connects to RabbitMQ through AMQP using `RabbitMQ__Uri`. The broker can be installed directly on the VPS or run on another server. The application does not install or manage the broker.

If RabbitMQ is already running, create/use a dedicated `wida` user and virtual host, grant that user configure/write/read permissions on that virtual host, then supply its URI through the API's protected service environment:

```dotenv
# Example only: RabbitMQ and the API run on the same server.
RabbitMQ__Uri=amqp://wida:URL_ENCODED_PASSWORD@127.0.0.1:5672/wida
ProcessingQueue__Enabled=true
```

Replace the host/port/vhost for your actual installation and percent-encode special characters in URI credentials. Use AMQPS with validated certificates for connections crossing an untrusted network. Do not commit a real URI containing credentials. The runtime reads this environment variable directly; it does not automatically load a `.env` file.

For a new installation, use RabbitMQ's instructions for the VPS operating system: [Debian/Ubuntu](https://www.rabbitmq.com/docs/install-debian) or [RPM-based Linux](https://www.rabbitmq.com/docs/install-rpm). Install a supported RabbitMQ 4.x release with a compatible Erlang version; the integration was tested against RabbitMQ 4.2. On a systemd-based server with the RabbitMQ package installed, these commands enable and check the native service:

```sh
sudo systemctl enable --now rabbitmq-server
sudo rabbitmq-diagnostics ping
```

Merge the relevant settings from [the native configuration example](../deploy/rabbitmq.conf) into your broker configuration, normally `/etc/rabbitmq/rabbitmq.conf`. The example binds AMQP to loopback for an API on the same server and sets the acknowledgement timeout to 24 hours. If the API is on another server, configure the corresponding private interface/TLS listener instead. Restart the service after changing its configuration. Optional management-plugin settings in the example bind its console to loopback; the plugin is not needed by Wida.

The API hosts the worker by default. Set `ProcessingQueue__Enabled=false` on API instances that should only publish. At least one instance must enable the worker and remain running under the VPS service manager. `RabbitMQ__QueueName` defaults to `wida.invoice-analysis.v1`; all instances must use the same broker, vhost and queue. Original files must remain accessible to every worker. Persist and back up RabbitMQ's native data directory and PostgreSQL, and monitor broker disk space and queue failures.

## Database initialization and recovery

Apply all committed migrations, including `InitialCreate`, `PublicTrial`, and `UserRoles`. They provide document ownership, analysis recovery metadata, trial budgets, and account roles.

```sh
# From wida-api/Wida.Api
dotnet tool restore
dotnet ef database update --project ../Wida.Dal --startup-project .
```

After a broker restore, an operator can explicitly republish a known active run ID using the same RabbitMQ configuration:

```sh
# From the API publish directory; RabbitMQ__Uri must already be configured.
dotnet Wida.Api.dll --republish-analysis-run PROCESSING_RUN_GUID
```

This operator-only command does not create a run or bypass ownership on API requests. The worker checks the persisted run and owner; completed duplicates are acknowledged without calling Azure. Never reset `SubmissionStartedAt`/`AzureOperationId` to force an automatic new submission. Replaying a terminal failed run routes it back to the failed queue; a new analysis must be explicitly requested through the normal API after investigating its failure.

## Admission and delivery guarantees

`POST /api/processing/documents/{documentId}/invoice` returns **202 Accepted** only after RabbitMQ confirms publication and PostgreSQL commits admission. The response includes the run and its status `Location`. Repeating an active request returns the same run without publishing another message. A broker outage returns **503** with `Retry-After: 10`; a new admission is rolled back and the previously uploaded document remains available.

For ordinary users, admission is limited to one active job per user and 100 globally. Administrators bypass these application admission checks; broker capacity still applies. A short PostgreSQL transaction/advisory lock (`73190421`) serializes these business checks; a partial unique index prohibits two active runs for one document. This lock is not used to distribute jobs or elect a worker. The same lock also protects lifetime page debits and the shared monthly budget; see [public beta](public-beta.md).

To avoid a database outbox and a lost-message gap, publication is confirmed **before** the admission transaction commits. On delivery, the consumer briefly takes the same transaction lock before looking up that one run ID. It therefore waits for admission to commit or roll back. If admission rolled back after RabbitMQ accepted the message, the orphan ID goes to the failed queue. No Azure request is made for an orphan. A network error during confirmation/commit may yield a failed HTTP response despite accepted work; retry the same document or inspect its history. This is not a distributed transaction or an exactly-once guarantee.

The main queue is a durable **quorum queue**, with persistent messages, `mandatory` publishing and publisher confirms. `x-single-active-consumer=true` coordinates standby workers across replicas; `prefetch=1` limits the active consumer to one unacknowledged delivery. An ID-only message remains unacknowledged until its persisted run is terminal. Success is acknowledged; failure or malformed/orphan messages are rejected into `<queue>.failed`. At-least-once dead-lettering is enabled with `reject-publish` overflow. Main queue capacity is 1,000 messages, accounting for duplicate deliveries as well as jobs. Monitor the failed queue separately.

The broker's automatic delivery-count limit is disabled: an infrastructure outage must not silently exhaust it while the database is unavailable. A consumer infrastructure failure closes its connection and retries after five seconds; RabbitMQ redelivers its unacknowledged message. Application/Azure retries are bounded separately. A lost channel, cancelled consumer or lost connection cancels in-flight processing before reconnecting. As with any broker and external API, an in-flight network partition cannot guarantee exactly-once effects.

## Azure tracking and frontend

The worker performs one Azure request at a time, spaces POST/GET steps by at least two seconds and honors longer `Retry-After` delays. It persists `SubmissionStartedAt` before POST and `AzureOperationId` after acceptance. Redelivery with an operation ID resumes GET polling. A submission marker without an ID is uncertain and fails with `ANALYSIS_SUBMISSION_UNCERTAIN`, without automatic resubmission. A crash before the actual POST can also leave this conservative state.

Only a definite HTTP 429 submission rejection can retry POST automatically. Polling retries transient network errors, timeouts, 429 and 5xx with exponential backoff and at most five retries; successful requests reset that retry count. Tracking expires after 20 hours. The native configuration example in `deploy/rabbitmq.conf` sets the broker acknowledgement timeout to 24 hours; apply that setting to the actual RabbitMQ service. Backoff holds the current delivery and pauses subsequent work at this small-volume stage.

The frontend retains its existing queued state and three-second status polling, with ten-second backoff on retrieval errors. Reload restores known active runs from server history. Invoice edits and saved status survive result updates. Manual history-only runs are never published.

## Tests and remaining launch work

Normal `dotnet test Wida.slnx` covers admission rollback, owner isolation, Azure restart/uncertainty handling, HTTP mapping and retry behavior without an external broker. Optional integration tests use real PostgreSQL and RabbitMQ:

```sh
WIDA_QUEUE_TEST_POSTGRES='Host=127.0.0.1;Port=55439;Database=wida_queue_test;Username=postgres;Password=TEST_PASSWORD' \
WIDA_QUEUE_TEST_RABBITMQ='amqp://wida:TEST_PASSWORD@127.0.0.1:55679/' \
dotnet test Wida.slnx
```

Use disposable services. Tests create/drop randomly named `wida_queue_test_*` databases and `wida.test.*` queues. They cover concurrent admission, two consumers, unacknowledged redelivery/restart, and a confirmed message whose database transaction rolls back. Azure is simulated; live Azure end-to-end validation remains necessary.

Public signup, lifetime credits, monthly budget enforcement, F0 file/page validation, original retention and optional Turnstile are implemented. Apply the new migration and follow the [public beta deployment guide](public-beta.md).

References: [RabbitMQ .NET client](https://www.rabbitmq.com/client-libraries/dotnet-api-guide), [publisher confirms](https://www.rabbitmq.com/tutorials/tutorial-seven-dotnet), [quorum queues and dead lettering](https://www.rabbitmq.com/docs/quorum-queues).
