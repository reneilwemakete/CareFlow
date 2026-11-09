# CareFlow

A backend-first healthcare appointment management **portfolio project** built with C#, ASP.NET Core, EF Core and Microsoft SQL Server.

**Status: Phase 1 foundation scaffold.** This is not a deployed healthcare system. Use fictional patient and clinic data only.

## Included

- .NET 10 solution with API, Domain, Application and Infrastructure projects
- SQL Server EF Core DbContext and mappings for clinics, patients, practitioners, availability, appointments and audit events
- ASP.NET Core Identity persistence model for staff users and roles
- Appointment interval rule and xUnit unit tests
- OpenAPI document in Development and a public process liveness endpoint at `/health/live`
- SQL Server Docker Compose configuration
- GitHub Actions build and test workflow

**Not yet built:** Login endpoints, role policies, patient and appointment APIs, SQL transaction concurrency controls, reporting, audit event emission and integration tests. They are future phases.

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

5. Generate, **inspect**, and apply the initial EF migration:

   ```sh
   dotnet tool install --global dotnet-ef --version 10.0.0
   dotnet ef migrations add InitialCreate --project src/CareFlow.Infrastructure --startup-project src/CareFlow.Api --context CareFlowDbContext --output-dir Persistence/Migrations
   dotnet ef database update --project src/CareFlow.Infrastructure --startup-project src/CareFlow.Api --context CareFlowDbContext
   ```

6. Start the API:

   ```sh
   dotnet run --project src/CareFlow.Api --launch-profile http
   ```

   Liveness: `http://localhost:5084/health/live`
   OpenAPI document: `http://localhost:5084/openapi/v1.json`

The SQL Server SA login and `TrustServerCertificate=True` are **local development only**. Use separate limited-privilege credentials and proper certificate validation for non-local environments. SQL Server container images may require an x86-64 compatible Docker environment on Apple Silicon.

## Database and architecture

See [requirements](docs/requirements.md), [architecture](docs/architecture.md) and [database design](docs/database-design.md).

## Verification status

This scaffold was authored without an installed .NET SDK or SQL Server in the creation environment. It has **not** been compiled, run, or migrated. The first GitHub Actions run is required to validate compilation and tests after uploading the code. No migration file has been generated yet: generate it with the EF CLI as instructed above.
