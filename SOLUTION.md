# Solution notes

## Overview

The service accepts payment requests from the Zapper app, resolves the customer, stored card token, and sub-merchant from PostgreSQL, sends an Acme Pay request, and persists the resulting state and provider details. It accepts Acme Payment Advice and transaction-status notifications, maintains a lifecycle history, and exposes a daily settlement summary grouped by sub-merchant and currency.

The repository runs locally with Docker Compose. The stack includes the API, a separate Acme simulator, PostgreSQL, and Seq. The Acme hostnames in the assignment end in `.example`; they are placeholders. Local payment traffic is directed to the stub, and no live gateway or real card data is used.

## Architecture

The solution uses a layered Clean Architecture structure. The domain and application layers do not depend on ASP.NET Core or EF Core. HTTP transport, PostgreSQL, and outbound gateway communication are implemented at the edge of the solution.

I chose this multi-project structure because I prefer clear boundaries between the core rules, services, persistence, and HTTP API. Keeping these responsibilities separate makes dependencies easier to understand and limits how much one part of the solution needs to know about another. It also makes it easier to replace an integration or persistence detail without moving payment rules into controllers. The trade-off is more projects, interfaces, and navigation than a single-project API; for this assignment, that structure makes the responsibilities and dependencies explicit.

| Project | Responsibility |
| --- | --- |
| `Zapper.Domain` | Customer, stored card, sub-merchant, payment, lifecycle event, and inbound notification entities; payment and notification states |
| `Zapper.Application` | Payment submission and lookup, provider and repository contracts, callback validation and state changes, SAST date handling, and settlement-summary rules |
| `Zapper.Infrastructure` | EF Core/PostgreSQL configuration and migrations, repositories, Acme HTTP client, request spacing, and development seeding |
| `Zapper.Api.Controllers` | HTTP endpoints for payments, callbacks, reporting, and their public request/response contracts |
| `Zapper.Api` | ASP.NET Core composition root, configuration, health checks, correlation middleware, Seq logging, Swagger, and Development migration/seed orchestration |
| `AcmeStub.Api` | Separate implementation of the documented Acme payment/query behavior, deterministic outcomes, and local notification controls |

Controllers translate HTTP input to application calls and map application results back to HTTP. Application workflows own validation and state transitions. Infrastructure owns database and HTTP details. The stub is a separate service with no project reference to Zapper, so it can be built and run independently.

I used SOLID principles as guidance when defining these project boundaries, particularly single responsibility, interface segregation, and dependency inversion. Controllers handle HTTP concerns, application services coordinate payment workflows, and infrastructure provides database and provider communication implementations behind interfaces. This keeps dependencies easier to follow and makes infrastructure implementations easier to replace. The trade-off is additional projects and interfaces. `PaymentService` currently coordinates several related payment responsibilities, so I would revisit its boundaries if the payment workflow grew.

The assignment allowed PostgreSQL, SQL Server, or SQLite. I chose PostgreSQL because I actively work with it and can apply that practical experience to the schema, migrations, and local operations. PostgreSQL provides relational constraints and transactions that support consistent payment records and concurrent idempotency checks. It works with EF Core migrations and runs locally as a persistent Docker service, matching the containerized setup used for the assignment.

SQL Server would also be a capable relational database for this workload, but PostgreSQL better matches the database I chose to use and demonstrate. SQLite would reduce local setup because it can run without a separate database service, but it is less representative of the concurrent, multi-service deployment being modeled here. PostgreSQL's trade-off is the extra service and volume to run and maintain locally; Docker Compose makes that setup repeatable for reviewers.

For local diagnostics, I chose Seq alongside structured application logs. Console logs are simple and have fewer components to configure, but they are less convenient for searching and visually exploring related payment, callback, and provider events. Seq provides a lightweight local service with a searchable interface, making those events easier to inspect during development. The trade-off is another service to start and maintain. Seq is used for local development; the production approach described later sends structured logs to CloudWatch Logs and uses operational metrics and alarms.

## Payment flow

1. The app sends `customerId`, `subMerchantId`, `amount`, `appPaymentId`, and an optional `currencyCode` to `POST /api/payments`. Amounts are positive integers in the currency's lowest unit; `1250` in ZAR is R12.50. The API does not accept a card number or token.
2. The application resolves the customer and sub-merchant, then chooses a stored token belonging to that customer for card services. EFT requests do not include card details. The local sample uses deterministic ordering for the customer's saved cards.
3. Before contacting Acme, the API stores a pending payment with a new Acme `TransactionID`, an alphanumeric `OrderNumber` whose first 12 characters are its RRN, and a `Submitted` lifecycle event.
4. The request carries a unique message ID, configured channel and payment service, customer email, merchant fields, and the token when the payment service requires a card. The FNB card-acceptor field is exactly 40 characters: a facilitator/sub-merchant name in positions 1–23, city padded to 13 characters in positions 24–36, the first two region characters in positions 37–38, and country code in positions 39–40. The local facilitator name is `ZAP`, so the descriptor starts `ZAP*Cape Demo Coffee`; the asterisk is at position 4 as required.
5. Acme response details and the raw response are persisted. The API response returns structured provider details but does not expose the raw payload.
6. Later provider notifications are authenticated, recorded, matched to the payment, and applied as lifecycle events. `GET /api/payments/{appPaymentId}` returns the current state and reconciles a processing or uncertain payment using the stored Acme identifiers.

The app-facing payment API intentionally has no caller authentication, as permitted by the assignment. Acme-facing requests use a bearer API key. The payment-service, card-on-file scenario, and channel can be supplied through environment settings; defaults are `CardNotPresent`, `CIT`, and `App`.

## Idempotency, timeout, and provider behavior

The app's `appPaymentId` is a globally unique GUID in this implementation. A request replay with the same ID and matching payment details returns the existing payment. Reusing the ID with different details returns a conflict. A database unique index protects this rule when requests race.

Acme `TransactionID` and RRN are separately generated and unique. The RRN is the first 12 alphanumeric characters of `OrderNumber`, which follows the provider's contract. Database uniqueness constraints protect both identifiers. Lifecycle events also have a unique payment/external-event identity where the provider supplies one.

An ambiguous timeout is not retried as a new payment. The original transaction ID and order number are queried through `GET /apirttq/query/Merchant/{MerchantID}`. If Acme reports that the transaction is still unresolved, Zapper stores `Processing` or `OutcomeUnknown` and a later status request can query again. The development timeout is eight seconds for a quick local demonstration; the production default is 35 seconds to allow for the provider's documented issuer delay of up to 30 seconds, with the same bounded reconciliation strategy.

The Acme client uses a process-wide request-spacing gate of 67 ms, keeping one API process below the provider's 20 requests-per-second limit. It performs at most one automatic retry for an HTTP 429/E29 response when `Retry-After` is no more than five seconds. The retry uses the same transaction/order identifiers and a fresh unique message ID and timestamp. If a payment remains persisted as E29 submission failure after that bounded retry, a replay of the same app payment ID resubmits the stored payment with the original Acme identifiers. A real multi-instance deployment must enforce the provider cap across all instances; the in-process gate alone does not provide a distributed limit.

The stub enforces a fixed 20-request-per-second window and supports deterministic approval, decline, EFT, delay, timeout, throttle, and gateway-failure outcomes. It stores transactions in memory for local demonstrations; PostgreSQL payment and notification data is persistent.

## Acme response and error handling

Acme's `TransactionStatus` describes gateway processing, while `ResponseDetail.Code` describes the acquirer result. The integration preserves the response source, code, description, cardholder-safe client message, Acme response message ID, `ReconID`, `AuthCode`, any conflicting transaction ID, and raw response JSON for audit.

| Acme result | Zapper behavior |
| --- | --- |
| `Success` with code `00` | `Approved` |
| `Success` with code `05`, `51`, `54`, or `91` | `Declined`; the provider's message and code are retained |
| `Complete` with code `00` or `COMPL` | `Approved` (completed EFT) |
| `Processing` or code `PEND` | `Processing` |
| `Failure` | `SubmissionFailed`, except a pending code remains `Processing` |
| `Reversed` | `Reversed` |
| `Refunded` | `Refunded` |
| Unrecognized response state | `OutcomeUnknown` pending reconciliation |
| `E01`, `E02`, `E09`, `E29`, `E50` | Envelope and details are parsed and preserved; HTTP and retry behavior follows the provider response |

The stub returns the documented JSON message envelope for its payment, query, and error responses. It models missing/invalid input (`E01`), invalid bearer credentials (`E02`), RRN conflicts (`E09` with the existing transaction ID), throttling (`E29` and `Retry-After`), and degraded gateway behavior (`E50`). Unrecognized JSON fields are ignored so additive provider fields do not break deserialization.

## Notifications

The assignment permits publicly accessible notification endpoints but requires Acme authentication. `POST /api/notifications/payment-advice` uses the configured HTTP Basic username/password. `POST /api/notifications/transaction-status` verifies the Base64 HMAC-SHA256 signature in `x-signature` against the exact request-body bytes and configured webhook secret. Both reject invalid authentication and acknowledge a recorded, safely handled notification with HTTP 200.

Each accepted delivery is recorded with source, provider identifiers, receipt/occurrence timestamps, a payload SHA-256, raw body, processing state, and processing note. The raw notification is retained for audit and is not written to application logs. `SourceName` and `ProcessingStatusName` are generated database columns beside their integer codes, so database queries show names such as `PaymentAdvice` and `Processed` without storing a second, potentially inconsistent value. A notification starts with the in-memory `Received` value, then its handler sets the final processing result before saving the delivery and payment changes in one transaction.

Duplicate deliveries are recorded as duplicates without applying the event twice. PostgreSQL advisory and row locks serialize duplicate identities and concurrent updates for the same payment. Payment Advice updates the current payment state and the corresponding Acme response details, including transaction status, source, description, reconciliation ID, authorization code, and raw callback payload. Older Payment Advice cannot roll back a newer terminal payment outcome. Unmatched, stale, rejected, and non-terminal deliveries remain inspectable. A reconciliation query that returns the same outcome as the latest lifecycle event refreshes the payment's provider details but does not add a redundant lifecycle row.

Reversal and refund webhooks are matched by RRN and use Acme's `transactionUUID` as the event identity. Refunds are stored as separate amount-bearing lifecycle events; multiple partial refunds may accumulate up to the authorized amount. Duplicate event IDs, over-refunds, conflicting reversal/refund events, and events for payments without an approved original do not change payment state.

## Data model and trade-offs

PostgreSQL is used with EF Core migrations in `Zapper.Infrastructure`. This choice reflects my experience with PostgreSQL and its fit for relational payment data, transactional updates, and database-enforced uniqueness.

- `Customers` owns stored card tokens and email addresses. The API stores provider tokens, not primary account numbers.
- `SubMerchants` stores Zapper's merchant ID, trading name, city, region, country, and MCC needed to construct a payment request.
- `Payments` stores the app ID, customer/sub-merchant relationship, requested and authorized amounts, currency, current state, Acme transaction/order/RRN identifiers, provider response detail, timestamps, and raw response JSON.
- `PaymentLifecycleEvents` stores append-only state and financial events with amount, currency, source, occurrence/recording times, provider event ID, response code, and description. Reports aggregate these events so an approval remains represented after a later reversal or refund.
- `PaymentStatuses` and `PaymentLifecycleEventTypes` translate the integer codes stored by `Payments.Status` and `PaymentLifecycleEvents.Type` into readable names and descriptions. Foreign keys keep those stored codes aligned with the lookup rows.
- `PaymentOutcomeCodeMappings` explains provider response codes in their transaction-status context and links each known outcome to its resulting payment status and lifecycle event type. Context is part of the key because a response code such as `00` can mean different things for an approval, reversal, or refund. Zapper-generated transport outcomes such as `TIMEOUT` are identified by their origin.
- `InboundNotifications` stores each delivery and its deduplication identity, external event ID, related provider IDs, raw body, payload hash, timestamps, handling status, and processing note. Its generated `SourceName` and `ProcessingStatusName` columns make the integer enum values readable while keeping the numeric codes as the persisted source of truth.

Unique indexes protect app payment IDs, Acme transaction IDs, and RRNs. Supporting indexes cover customer/sub-merchant payment timelines, lifecycle event dates/types, and notification lookup fields. The notification data model retains duplicate deliveries for audit rather than enforcing a unique row for each delivery. Advisory locking and repository checks decide whether a duplicate delivery is applied. Lifecycle history records meaningful outcomes and state changes; a repeated reconciliation response with no change is not added as another event.

The current-state `Payment` row makes lookup efficient; lifecycle events provide the historical record and reporting source. This duplicates some information by design: current-state reads remain simple while the event history explains how the state was reached. The stored raw provider response improves troubleshooting but increases retention and privacy obligations; production retention, encryption, access auditing, and redaction policy should be explicit.

As volume and product scope grow, schema evolution could add explicit payment-attempt entities, provider/account configuration tables, currency metadata, stronger state-transition constraints, partitioning for high-volume event/notification tables, and an outbox or queue for asynchronous delivery. Migrations should be backward compatible and deployed as a controlled release step. Financial reporting would also need reconciliation against Acme settlement files, not only API outcomes and notifications.

In Development, the API applies migrations at startup and runs an idempotent seeder. The seed contains two synthetic customers, three stored-card tokens, two sub-merchants, and stable approved/declined example payments with lifecycle events. It inserts only records whose stable IDs are missing and never resets existing records. Restarting the API does not wipe or duplicate records. The seed does not run outside Development.

Startup migration behavior is controlled by `Database:ApplyMigrations`. It defaults to enabled in Development and disabled in other environments; local Compose sets `Database__ApplyMigrations=true` explicitly. Production release automation should build a versioned EF Core migration bundle and run it as one controlled, one-off database task before deploying new API instances. Production API instances then start with startup migrations disabled and do not need schema-change privileges. Removing the PostgreSQL Compose volume remains an explicit destructive local reset.

## Daily settlement summary

`GET /api/reports/daily-summary?date=YYYY-MM-DD` groups approved, declined, reversed, and refunded lifecycle events by sub-merchant and currency. It returns event counts and totals as minor units. For example, an approval of 1250 ZAR minor units contributes R12.50 to approved totals; a 300-unit partial refund contributes R3.00 to refunds.

The query uses an inclusive UTC start and exclusive UTC end derived from the requested date in `Africa/Johannesburg`. Provider timestamps without a timezone offset are interpreted as SAST at millisecond precision, then stored as UTC instants. This makes day boundaries explicit and avoids relying on the host/container's local timezone. Partial authorizations use the actual `AuthAmount` where supplied; refunds use each callback's actual transaction amount.

## Configuration and local operation

Runtime configuration is supplied through environment variables in Compose. The local Compose values are intentionally convenient demo settings. The API uses container DNS names `postgres`, `seq`, and `acme-stub`; the callback simulator uses `http://api:8080`. Host processes use `localhost` mappings instead.

`ACME_PAY_BASE_URL` can override the API's Compose `AcmePay__BaseUrl`, which defaults to the stub. The assignment's sandbox and production base URLs (`https://sandbox.acmepay.example` and `https://gateway.acmepay.example`) are example placeholders and are not reachable payment environments. Payment calls append `/api/payment`; transaction queries append `/apirttq/query/Merchant/{MerchantID}`. A real deployment would use the provider-issued URL, API key, and merchant GUID.

## Verification strategy

The automated suite is intentionally focused on payment correctness and the assignment's main failure risks rather than testing every small method. Application-level tests cover Acme result mapping (including `00`, `05`, `COMPL`, and `PEND`), payment-ID replay/conflict behavior, descriptor positions, callback Basic authentication and exact-body HMAC verification, duplicate and timestamp-less Payment Advice handling, refund bounds and duplicate delivery, and merchant/currency settlement totals. A PostgreSQL Testcontainers integration test applies the real EF Core migrations and verifies the unique `AppPaymentId` constraint through the production repository implementation. Run all checks with `dotnet test .\Zapper\Zapper.sln` from the repository root; Docker is required for the PostgreSQL integration test. The suite does not make calls to Acme's placeholder external hosts.

The Compose stack includes health checks for PostgreSQL, Seq, the stub, and API liveness; API readiness additionally checks PostgreSQL. PostgreSQL and Seq use named volumes. Seq receives structured console/API diagnostics through its container endpoint; locally its UI is exposed at `localhost:5341` and its ingestion port at `localhost:5342`. Correlation IDs flow through HTTP responses and structured logs. Logs contain diagnostic identifiers and outcomes, not card tokens, credentials, or notification bodies.

## Production deployment approach on AWS

This repository is not deployed to AWS and does not include infrastructure as code. A reasonable production design would use the following managed/container services:

1. Build and scan the API image in CI, push the immutable image to Amazon ECR, and run the API as an Amazon ECS service on Fargate in private subnets. Put an Application Load Balancer in front of the API. Only required payment and callback routes would be reachable from outside; the Acme stub would not be deployed.
2. Use Amazon RDS for PostgreSQL in private subnets with Multi-AZ failover, encryption at rest, automated backups and point-in-time recovery, restricted security groups, and monitored connection limits. Build a versioned EF Core migration bundle in CI and run it once as a release task before rolling out application tasks. Set `Database__ApplyMigrations=false` on the API service so each application instance does not attempt schema changes at startup. Do not run the Development seed in production.
3. Store the database connection string, Acme API key, MerchantID, Payment Advice credentials, and webhook secret in AWS Secrets Manager. Grant the ECS task role least-privilege access to only the required secret ARNs. Inject values into the task at runtime or retrieve them through the AWS SDK; do not bake secrets into the image, task definition source, or repository. Rotate keys with a coordinated provider and service update.
4. Keep non-secret settings such as URLs, timeout limits, payment service, channel, and log levels in ECS task environment configuration or AWS Systems Manager Parameter Store. Use separate AWS accounts or isolated environments for development, staging, and production.
5. Scale the ECS service horizontally behind the load balancer using CPU/memory and request/latency signals. Protect RDS with connection pooling, bounded concurrency, and explicit maximum connection budgets. The current request limiter is process-local; production horizontal scaling must add a shared distributed limiter (for example, an ElastiCache Redis token bucket) or a centrally coordinated outbound worker so the aggregate Acme traffic stays below 20 requests per second.
6. Configure ALB deregistration delay and ECS health/drain behavior so a task stops receiving new requests before shutdown. The host has a 45-second shutdown timeout and propagates cancellation tokens to outbound work. Persist the payment and provider IDs before sending; on ambiguous provider outcomes, reconcile by the same identifiers. Callback handlers acknowledge only after the notification is durably recorded and safely processed.
7. Terminate TLS at the load balancer using ACM certificates, enforce HTTPS, apply WAF/rate rules where appropriate, and restrict database/provider egress and administrative access. Because callbacks are public, keep Basic credentials and HMAC secrets separate, rotateable, and validated in constant time. Do not expose stub control routes.

### Scaling behavior

The API is stateless between requests except for PostgreSQL, so multiple ECS tasks can serve traffic. PostgreSQL advisory/row-level locking protects important concurrent payment and callback operations. The Acme throttle is not distributed today and therefore must be replaced or coordinated before running multiple production API tasks. If callback volume or downstream latency grows, a durable queue/outbox can separate receipt acknowledgement from slower processing while preserving event identity and ordering rules.

### Graceful shutdown

The application configures a 45-second host shutdown window and logs when shutdown starts. In production, ECS should first remove the task from load-balancer rotation, allow in-flight requests to finish within the deregistration and shutdown windows, and then terminate it. Long-running outbound requests receive cancellation tokens. A request interrupted after its payment has been reserved must not create a new Acme transaction on retry; the persisted identifiers are used for later status reconciliation.

### Logs, metrics, and diagnostics

Locally, the API emits structured logs to the console and Seq. In AWS, send structured application and access logs to CloudWatch Logs, and use CloudWatch metrics/alarms or an OpenTelemetry collector and metrics backend for service-level visibility. Useful signals include:

- Request count, latency percentiles, 4xx/5xx rates, and health-check status by route.
- Acme call latency, timeouts, 429/E29 counts, `Retry-After` duration, 5xx/E50 counts, and transaction-query outcomes.
- Payment counts by final status and response code, unresolved payments, duplicate/stale/unmatched callbacks, reversal/refund totals, and callback acknowledgement failures.
- Database connection utilization, query latency, migration failures, deadlocks, and connection errors.
- Container CPU/memory, restart count, task count, and load-balancer target health.

Propagate a correlation ID and log payment ID, app payment ID, Acme message/transaction ID, merchant, route, result code, and elapsed time. Never log API keys, callback secrets, card tokens, full payment credentials, or raw callback/request bodies. Dashboards and alarms should detect increasing unresolved payments and callback failures, not only process health.

## Assumptions and simulator boundaries

- The seeded customers, card tokens, merchants, and example payments are synthetic. The stub does not connect to an acquirer and does not move money.
- The stub simulates approvals, common card declines, completed and pending EFTs, processing and delayed outcomes, timeouts, RRN conflicts, throttling, and gateway failures. It can return a stored transaction from a query or a not-found response when the supplied `TransactionID` or `OrderNumber` does not match a transaction. Its local controls can deliver duplicate or stale Payment Advice and send signed reversal or refund callbacks, including partial refunds. These controls and the stub's transaction state are for local demonstrations only; outcomes are held in memory and are lost when the stub restarts.
- The provider's `.example` sandbox/production URLs are illustrative placeholders. A real environment requires Acme-issued credentials, merchant identity, callback registration, and network access.
- The payment service defaults to card-not-present, CIT, App, and ZAR when the optional currency is omitted. The settings can be changed to demonstrate documented payment service and channel values.
- `appPaymentId` is globally unique in this implementation. If the business later allows the same app ID for different customers or merchants, its uniqueness scope and database index must be changed deliberately.
- Payment Advice is treated as an outcome notification when it carries `Success` or `Failure`; `Other` and older outcomes are recorded without rolling back a newer result. Reversals and refunds are separate signed transaction-status events.
- The app's payment endpoints do not enforce caller authentication because the assignment explicitly says it is not required. Public notification endpoints do enforce the provider-specific authentication described above.
- Local Compose credentials are public development examples and are not suitable for a deployed environment.

## AI-assisted development

### Tools and workflow

I used OpenAI Codex as an AI development assistant to analyze the assignment and repository, help plan the implementation, carry out scoped code and documentation tasks, audit the solution against the requirements, and run builds, tests, and Docker checks.

I gave Codex the assignment document and repository so it could work from the actual requirements and code. I explained the Clean Architecture structure and coding style I wanted and shared examples from personal projects I had developed manually to show how I prefer to organize a solution. I also set the technology direction, including .NET 8, PostgreSQL with EF Core, Seq, and Docker.

I broke the assignment into sequential tasks with clear scope and completion criteria. I ordered them by dependency: local infrastructure and Acme contracts first; then the stub and persistence; then payment processing, callbacks, reporting, and operational behavior; followed by containerization, documentation, audit remediation, focused tests, and final acceptance. I worked through one task at a time.

I set the solution direction, priorities, and expected behavior, then delegated bounded implementation tasks to Codex stage by stage. Those tasks included the Acme stub, persistence, payment and notification workflows, settlement reporting, operational features, containerization, focused tests, and documentation. I reviewed each stage and decided whether it met the requirements before authorizing the next.

I kept responsibility for interpreting the assignment and making the engineering decisions. I set expectations for idempotent payments, timeout reconciliation using the original Acme identifiers, duplicate and out-of-order notifications, and persistent, non-duplicating seed data. I also made the architecture and technology trade-offs and judged whether the proposed implementation met the assignment.

I reviewed Codex's work against the assignment and my constraints rather than accepting it at face value. I asked for explanations when something was unclear, requested audits to find gaps, and directed corrections when the implementation did not match the requirements. For example, an audit identified gaps in the FNB card-acceptor descriptor, handling of `COMPL` and `PEND` Payment Advice, timestamp-less callback ordering, and concurrent RRN allocation. I reviewed those findings and directed remediation; Codex updated the implementation.

During a focused review, I also considered whether the implementation followed the SOLID principles used to guide the architecture. I checked that responsibilities and dependencies aligned with the intended project boundaries. This review identified `PaymentService` as the main class to revisit if the payment workflow expands; its current responsibilities remain related to coordinating payment submission.

If I approached this work again, I would keep the task-based workflow but start a concise traceability and contribution log from the beginning. I would record each requirement, my decisions, the assigned work, review outcomes, and validation evidence as the work progressed. That would make reviews and final documentation easier to assemble accurately.

### Validation of AI-generated work

During implementation, Codex ran builds, automated tests, Docker Compose configuration and health checks, and full-stack acceptance scenarios. I independently started the project locally, checked that the containers and services were running and communicating, inspected Seq to confirm application logs were visible, and ran the test suite myself. I also compared the results with the assignment requirements.

The solution build passed with zero warnings and errors. All 20 automated tests passed. Docker Compose configuration validated, and the API, Acme stub, PostgreSQL, and Seq services reached healthy status. Manual full-stack checks covered approval and decline, idempotent replay and conflicting reuse, duplicate Payment Advice, reversal and partial refund, merchant queries, daily settlement reporting, timeout recovery using the original identifiers, E29 retry behavior, E50 gateway failure, and concurrent RRN collision handling.

These checks gave me confidence in the behavior covered by the implementation and tests, including the services working together and logging functioning. They do not establish production readiness or replace broader validation under real deployment conditions. The Acme sandbox and production hosts in the assignment are placeholders, so the integration was checked against the local stub rather than a live gateway.

### AI workflow artifacts

The `ai/` directory contains a concise implementation plan, an assignment-requirements traceability note, and focused review and correction notes. I reviewed these artifacts for accuracy, relevance, and privacy. They contain project information and verification evidence without credentials, personal details, or unrelated conversation content.

I did not create or use AI tool-specific configuration or rules files for this assignment. No individual-session export was available in this workflow, so I relied on the curated artifacts rather than including a share link or an account-level export.
