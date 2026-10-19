# Zapper Payments Integration

Zapper Payments Integration is a .NET 8 Web API that accepts payment requests from the Zapper app, looks up stored customer and sub-merchant data, submits tokenised payments to Acme Pay, records the payment lifecycle, processes provider notifications, and produces a daily settlement summary.

The local setup runs the API, a fictional Acme Pay simulator, PostgreSQL, and Seq through Docker Compose. It uses synthetic data and local-only credentials; it does not contact a real payment provider or process real card payments.

## Project structure

The solution uses clear project boundaries to separate the domain, application workflows, infrastructure, and HTTP API. These boundaries reflect SOLID principles, particularly single responsibility, focused interfaces, and dependency inversion. They make responsibilities and dependencies easier to follow as the solution grows.

- `Zapper/src/Zapper.Domain` — payment, notification, and lifecycle entities and enums.
- `Zapper/src/Zapper.Application` — payment workflows, provider contracts, notification processing, reporting rules, and application abstractions.
- `Zapper/src/Zapper.Infrastructure` — EF Core/PostgreSQL mappings and migrations, repositories, Acme HTTP client, and development seeding.
- `Zapper/src/Zapper.Api.Controllers` — payment, notification, and reporting HTTP controllers.
- `Zapper/src/Zapper.Api` — ASP.NET Core host, configuration, health checks, Seq logging, startup, migration, and seed orchestration.
- `Zapper/src/AcmeStub.Api` — standalone Acme Pay simulator, transaction query, rate limit, and local notification/scenario controls.
- `postman/` — runnable Postman collection and request-by-request walkthrough for the local demo scenarios.

For architecture decisions, database trade-offs, lifecycle behavior, and the production deployment approach, see [SOLUTION.md](SOLUTION.md).

## Prerequisites

- Docker Desktop with Docker Compose v2, running on Windows, macOS, or Linux.
- A free local copy of ports `5280`, `5291`, `5432`, `5341`, and `5342`.
- .NET 8 SDK only if building or running the solution outside Docker.

## Start the application

Clone the repository, open a terminal in its root directory, and run:

```powershell
docker compose up --build -d --wait
docker compose ps
```

Wait until all four services report `healthy`. The first run downloads the .NET and service images and builds the API and stub images. The API applies pending EF Core migrations and runs an idempotent seed check at startup.

Open Swagger at [http://localhost:5280/swagger](http://localhost:5280/swagger). The Compose environment sets `ASPNETCORE_ENVIRONMENT=Development`, which enables Swagger.

## Run locally from Visual Studio

Use this workflow when you want Visual Studio to run `Zapper.Api` and `AcmeStub.Api` as local processes while PostgreSQL and Seq run in Docker. From the repository root, start only the supporting services:

```powershell
docker compose up -d --wait postgres seq
docker compose ps
```

On first use, Docker downloads the PostgreSQL and Seq images if needed and creates their named data volumes. Wait until both services report `healthy`. This command does not start the Compose API or stub containers, so Visual Studio can use their local ports.

Open `Zapper/Zapper.sln` in Visual Studio 2022 17.11 or later. In the launch-profile dropdown beside the green start button, select the shared solution launch profile **Zapper + Acme Stub**, then click the green triangle. The profile starts both APIs; `Zapper.Api` listens on `http://localhost:5280` and calls `AcmeStub.Api` at `http://localhost:5291`. If the profile is not selected automatically, choose it from the dropdown.

When `Zapper.Api` starts in Development, it applies pending EF Core migrations and runs the idempotent seed check against PostgreSQL at `localhost:5432`. Existing demo rows are retained, missing seeded rows are inserted, and the named PostgreSQL volume preserves data across container restarts. Seq ingestion is available at `localhost:5342`, with the Seq UI at [http://localhost:5341](http://localhost:5341).

To stop the local infrastructure containers while keeping their data, run:

```powershell
docker compose stop postgres seq
```

If you previously started the full Compose stack, stop its API and stub containers before launching those projects in Visual Studio because both workflows use ports `5280` and `5291`:

```powershell
docker compose stop api acme-stub
```

To return to the fully containerized workflow, stop the Visual Studio processes and run `docker compose up --build -d --wait` from the repository root.

| Service | Local address | Purpose |
| --- | --- | --- |
| Zapper API | `http://localhost:5280` | Payment, callback, reporting, health, and Swagger endpoints |
| Acme Pay stub | `http://localhost:5291` | Local payment gateway simulator and demo controls |
| PostgreSQL | `localhost:5432` | Payment, lifecycle, notification, customer, card, and merchant data |
| Seq | `http://localhost:5341` | Structured log viewer; ingestion is on local port `5342` |

The API's liveness endpoint is `GET /health/live`. Its readiness endpoint is `GET /health/ready` and checks its PostgreSQL connection. The stub health endpoint is `GET http://localhost:5291/health`.

## Seeded data

The seed routine inserts only records whose stable IDs are missing. Restarting the API does not clear existing data or create duplicate seed rows. A new database contains:

| Record | Identifier | Details |
| --- | --- | --- |
| Customer 1 | `ea238ea1-0fd8-4c55-bfeb-27fb2ea96f30` | `alex.mokoena@example.test`; two synthetic stored card tokens |
| Customer 2 | `af0cdac8-25ea-49f2-a9b5-686c53ea350d` | `sam.naidoo@example.test`; one synthetic stored card token |
| Sub-merchant 1 | `ZAP000000000001` | Cape Demo Coffee, Cape Town, WC, ZA, MCC 5812 |
| Sub-merchant 2 | `ZAP000000000002` | Joburg Demo Books, Johannesburg, Gauteng, ZA, MCC 5942 |
| Approved sample payment | `6c713d84-0f18-4f07-b929-e05ad4681831` | R12.50 ZAR for Customer 1 at Cape Demo Coffee |
| Declined sample payment | `5c7100fb-06f8-42c7-9ceb-a8c0e4c3860b` | R25.00 ZAR for Customer 2 at Joburg Demo Books |

Payments use integer minor currency units. For ZAR, `1250` means R12.50. The seeded card values are fake provider tokens. The app request identifies the customer and sub-merchant; it does not accept card numbers or card tokens. For card payment services the API selects a stored token belonging to the customer.


## Postman collection and walkthrough

The [`postman/`](postman/) folder contains the runnable demo collection and a request-by-request guide:

- [`zapper-demo.postman_collection.json`](postman/zapper-demo.postman_collection.json) — import this file into Postman to run the local Zapper API and Acme stub scenarios.
- [`postman-walkthrough.md`](postman/postman-walkthrough.md) — explains what each request does, the order to run requests in, and which database tables and records to inspect.

For the scenario walkthrough, use Postman to run the requests and DBeaver to inspect the resulting records in PostgreSQL. Start the local services using the Docker Compose instructions above, then import the collection and run the requests in order within each folder. Folder `00` checks service readiness and resets the stub’s default outcome. The collection generates payment IDs and amounts for the relevant scenarios and carries transaction IDs between requests. Folder `07` sends requests directly to the Acme stub to demonstrate an RRN collision; it does not create Zapper database records. The stub’s `/__control` routes are local demo controls only.

## Logs and diagnostics

Open Seq at [http://localhost:5341](http://localhost:5341). On a new local Seq data volume, the initial credentials are username `admin` and password `zapper-local-seq-admin`; Seq may require a password update on first sign-in. The environment variable in Compose sets the initial password only when Seq initializes a new data volume. Existing Seq volumes retain the credentials already stored in that volume.

The API writes structured logs to both the console and Seq. Request logs and application events include correlation, payment, message, transaction, response-code, and callback identifiers where available. Card tokens, credentials, and callback payloads are not written to the application logs. The API accepts a valid `X-Correlation-ID` header and returns the accepted value in the response header; otherwise it uses the ASP.NET request trace identifier.

Useful commands:
```powershell
docker compose logs -f api
docker compose logs -f acme-stub
docker compose logs -f postgres
docker compose logs -f seq
docker compose ps
```

The API app's `appsettings.Development.json` contains localhost settings for running the API process directly against local infrastructure. In Development, startup applies migrations and inserts the idempotent synthetic seed. Compose explicitly enables migrations and overrides host settings with service DNS names. In non-Development environments startup migrations default to disabled; deploy migrations once through the release pipeline before rolling out API instances. The production API does not insert development seed records.

## Data persistence and reset

PostgreSQL data and Seq data are stored in the named `postgres_data` and `seq_data` volumes. Restarting or recreating the API container does not delete database records. The seed routine checks stable IDs and adds only missing demo records.

Stop the services while keeping their data:

```powershell
docker compose down
```

Start them again with the existing data:

```powershell
docker compose up -d --wait
```

To deliberately erase the PostgreSQL database and Seq history, remove the volumes with `docker compose down -v` and then rebuild/start the stack. This deletes all local payments, notifications, logs, and seed records; migrations and seed data will be recreated on the next API startup.

## Automated tests

Run the test suite from the repository root:

```powershell
dotnet test .\Zapper\Zapper.sln
```

Docker must be running for the PostgreSQL integration test. The test starts an isolated PostgreSQL container, applies the EF Core migrations, and verifies that the database unique index prevents duplicate `AppPaymentId` records. The remaining tests exercise payment response mapping and idempotent replay, callback authentication/signature and notification behavior, descriptor formatting, and daily settlement totals. They use controlled test data and do not contact Acme's placeholder sandbox or production hosts.

## Troubleshooting

- **A service is unhealthy:** run `docker compose ps` and inspect the relevant `docker compose logs <service>` output. The API waits for healthy PostgreSQL, Seq, and Acme stub services.
- **Port is already allocated:** stop the process using the port or change the host side of the Compose port mapping. Keep the container-side ports unchanged unless the matching service configuration is also updated.
- **API reports a connection error:** inside Compose, the API uses `postgres`, `seq`, and `acme-stub`, not `localhost`. `localhost` is used only by a process running directly on the host.
- **Seed data is missing:** confirm that the API completed startup and its migration/seed phase; inspect `docker compose logs api`.
- **An old Seq password still works:** Seq credentials are persisted in `seq_data`; changing the first-run environment variable does not reset an existing Seq account. Use Seq's own account settings, or remove the volume only if you intend to erase its data.
- **Provider callback simulation fails:** confirm all four services are healthy and that the stub is configured with `ZAPPER_CALLBACK_BASE_URL=http://api:8080`.
- **A simulated transaction disappears after a stub restart:** the Acme stub deliberately keeps demo transactions in memory; PostgreSQL payment records remain persistent. Submit a new demo payment after restarting the stub.

