# CareFlow SQL Server database design

CareFlow uses EF Core 10 with SQL Server. The committed migrations are the canonical schema history; do **not** regenerate `InitialCreate` from the current model.

| Table | Primary key | Main constraints |
| --- | --- | --- |
| Clinics | Id (GUID) | Unique clinic Code |
| Patients | Id (GUID) | ClinicId FK; unique (ClinicId, PatientNumber); alternate key (ClinicId, Id); SQL Server rowversion |
| Practitioners | Id (GUID) | ClinicId FK; optional Identity UserId; alternate key (ClinicId, Id) |
| PractitionerAvailability | Id (GUID) | Composite (ClinicId, PractitionerId) reference; start before end check |
| Appointments | Id (GUID) | Composite clinic-scoped Patient and Practitioner FKs; interval/status checks; SQL Server rowversion; nullable CheckedInAtUtc |
| AuditEvents | Id (BIGINT) | ClinicId FK, optional actor user FK and timestamps |
| AspNetUsers / AspNetRoles | Identity-managed | Password hashes, staff roles and clinic membership |

Indexes support patient number uniqueness and appointment queries by practitioner, patient and start time. Cross-clinic relationships are restricted through composite alternate keys. Audit events preserve minimal action metadata rather than patient contact details.

## Migration history

The repository includes:
- `InitialCreate`, which establishes the clinic, patient, practitioner, availability, appointment, audit and Identity tables.
- `PersistAppointmentCheckIn`, which adds `CheckedInAtUtc` and backfills legacy check-ins from matching historical audit events.

To apply existing migrations to a disposable development SQL Server:

```bash
dotnet tool install --global dotnet-ef --version 10.0.14
dotnet ef database update --project src/CareFlow.Infrastructure --startup-project src/CareFlow.Api --context CareFlowDbContext
```

Set `ConnectionStrings__CareFlowDb` first. Never use `EnsureCreated` on a database managed by migrations. GitHub Actions validates the model snapshot and applies the committed migrations against a disposable SQL Server instance.

## UTC and concurrency

The HTTP API requires UTC timestamps. SQL Server `datetime2` does not retain the .NET `DateTime.Kind` flag, so comparisons must be explicit and clients must send ISO 8601 UTC values ending in `Z`. Application checks and database constraints verify valid intervals.

SQL Server SERIALIZABLE transactions guard the time-range conflict check and appointment insert/reschedule. Rowversion guards stale edits. These techniques do not replace a production database and application security review.
