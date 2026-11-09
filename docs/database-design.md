# Database Design (planned EF Core schema)

| Entity | Key | Important relationships and constraints |
|---|---|---|
| Clinic | Id (GUID) | Unique Code; tenant boundary |
| Patient | Id (GUID) | ClinicId FK; unique (ClinicId, PatientNumber); (ClinicId, Id) alternate key; rowversion |
| Practitioner | Id (GUID) | ClinicId FK; optional UserId -> AspNetUsers; (ClinicId, Id) alternate key |
| PractitionerAvailability | Id (GUID) | composite (ClinicId, PractitionerId) -> Practitioner; interval check |
| Appointment | Id (GUID) | composite patient and practitioner FKs constrained by ClinicId; interval and status checks; rowversion |
| AuditEvent | Id (BIGINT identity) | ClinicId FK and optional ActorUserId |
| AspNetUsers / AspNetRoles | Identity keys | ASP.NET Core Identity-managed authentication tables |

Indexes cover patient number uniqueness and appointment queries by practitioner and patient. Cross-clinic relationship integrity uses composite alternate keys. Restrict cascades to preserve historical records.

## UTC conventions
Use DateTime UTC at the API boundary; SQL Server datetime2 does not preserve DateTime.Kind, so rehydrate UTC explicitly in the application/database boundary before comparing business-rule dates.

## Migration workflow
From the root after configuring ConnectionStrings__CareFlowDb:

	net tool install --global dotnet-ef --version 10.0.0
	dotnet ef migrations add InitialCreate --project src/CareFlow.Infrastructure --startup-project src/CareFlow.Api --context CareFlowDbContext --output-dir Persistence/Migrations
	dotnet ef database update --project src/CareFlow.Infrastructure --startup-project src/CareFlow.Api --context CareFlowDbContext

Review the generated migration for constraints, foreign keys and indexes before applying. Do not run EnsureCreated on a database managed by migrations.
