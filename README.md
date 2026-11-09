# CareFlow

A backend-first healthcare appointment management **portfolio project** built with C#, ASP.NET Core, EF Core and Microsoft SQL Server.

**Status: Database foundation and Patient Management API merged; appointment scheduling in review.** This is not a deployed healthcare system. Use fictional patient and clinic data only.

## Included

- .NET 10 solution with API, Domain, Application and Infrastructure projects
- SQL Server EF Core DbContext and mappings for clinics, patients, practitioners, availability, appointments and audit events
- ASP.NET Core Identity persistence model for staff users and roles
- Appointment interval rule and xUnit unit tests
- OpenAPI document in Development and a public process liveness endpoint at `/health/live`
- SQL Server Docker Compose configuration
- GitHub Actions build and test workflow

**Patient API branch:** Clinic-scoped patient creation, retrieval, search, update, staff bearer login and basic patient audit events are implemented. Initial administrator provisioning and SQL-backed patient integration tests are implemented. Appointment scheduling and concurrency tests are on the current feature branch; reporting and subsequent staff onboarding remain outstanding. See [patient API specification](docs/patient-api.md) and [appointment API specification](docs/appointment-api.md).

## Prerequisites

1. .NET 10 SDK
2. Docker Desktop with SQL Server-compatible architecture support **or** an existing SQL Server instance
3. `dotnet-ef` version 10 for migrations

## Run with local SQL Server

1. Copy `.env.example` to `.env` and replace its placeholder with a strong password.
2. Start SQL Server:

   ```sh
   docker compose up -d
   ```

3. Provide the **same password** in your local connection string (never commit it):

   ```sh
   export ConnectionStrings__CareFlowDb='Server=localhost,1433;Database=CareFlowDb;User Id=sa;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=True'
   ```

   For Windows PowerShell:

   ```powershell
   $env:ConnectionStrings__CareFlowDb='Server=localhost,1433;Database=CareFlowDb;User Id=sa;Password=YOUR_PASSWORD;Encrypt=True;TrustServerCertificate=True'
   ```

4. Restore and build:

   ```sh
   dotnet restore CareFlow.slnx
   dotnet build CareFlow.slnx
   dotnet test CareFlow.slnx
   ```

5. Apply the **already committed** initial EF migration:

   ```sh
   dotnet tool install --global dotnet-ef --version 10.0.14
   dotnet ef database update --project src/CareFlow.Infrastructure --startup-project src/CareFlow.Api --context CareFlowDbContext
   ```

6. Start the API:

   ```sh
   dotnet run --project src/CareFlow.Api --launch-profile http
   ```

   Liveness: `http://localhost:5084/health/live`
   OpenAPI document: `http://localhost:5084/openapi/v1.json`

The SQL Server SA login and `TrustServerCertificate=True` are **local development only**. Use separate limited-privilege credentials and proper certificate validation for non-local environments. SQL Server container images may require an x86-64 compatible Docker environment on Apple Silicon.

## Clinic scheduling and check-in

The `feature/clinic-schedule-checkin` branch adds persisted appointment check-in timestamps and an authenticated clinic schedule read endpoint, with a SQL Server migration. See [clinic scheduling documentation](docs/scheduling-dashboard.md).

## Database and architecture

See [requirements](docs/requirements.md), [architecture](docs/architecture.md) and [database design](docs/database-design.md).

## Verification status

GitHub Actions has successfully compiled the .NET solution, run the existing unit tests, and applied the committed InitialCreate migration to a disposable SQL Server instance. Patient API request flows have not yet received end-to-end integration testing against a live API and authenticated staff account. This is an educational prototype only.
