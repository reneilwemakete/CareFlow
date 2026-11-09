# CareFlow: portfolio case study

## Problem

A fictional clinic needs one controlled backend service to register patients, manage practitioners and schedule appointments. Simple CRUD operations are insufficient: staff must not see another clinic's records, appointment conflicts must be handled during simultaneous requests, and administrative reporting must avoid leaking patient-level details.

## What I built

I designed and implemented a backend-first modular monolith using **C#, ASP.NET Core 10, SQL Server, EF Core 10, ASP.NET Core Identity and xUnit**.

The project includes:
- Tenant-scoped patient and practitioner APIs.
- Staff bearer authentication and guarded initial administrator provisioning.
- Practitioner availability and appointment lifecycle operations.
- Transaction-safe conflict detection for practitioners and patients.
- SQL Server persisted check-in and operational reporting.
- GitHub Actions integration tests using a disposable SQL Server container.

## Key engineering decisions

**Modular monolith over microservices.** A small backend is easier to develop and test as one deployable unit while preserving distinct assemblies for Domain, Application, Infrastructure and API.

**Clinic isolation in both application and database.** Every sensitive operation derives the clinic from the authenticated user. Composite clinic-scoped relationships restrict invalid foreign keys at the database layer.

**Serializable transactions for appointment booking.** Checking availability before inserting an appointment is vulnerable to races unless reads and writes are synchronized. The SQL Server transaction encloses the competing booking checks and insertion.

**Optimistic concurrency for updates.** Patient details and rescheduling require a rowversion token to reject stale edits.

**Explicit data minimization for reporting.** Dashboard metrics are aggregated in SQL Server; the reporting API returns operational counts, not names or clinical information.

## Evidence

The repository has CI workflows for .NET builds and unit tests, EF Core migration validation and SQL Server integration tests. The suite exercises authenticated API workflows, cross-clinic isolation, conflicting appointment creation, concurrency, audit entries, lifecycle transitions and reporting.

See the current [GitHub Actions runs](https://github.com/reneilwemakete/CareFlow/actions) for up-to-date results.

## What I learned

Building a reliable backend requires more than defining route handlers. The trickiest parts were protecting patient resources across clinics, dealing with database concurrency and testing realistic API behavior against SQL Server rather than relying only on mocks.

## Boundaries

This is a **portfolio prototype using fictional data**. It is not a deployed clinical product. Production healthcare use requires significantly stronger identity lifecycle controls, operational security, secure deployment, clinical workflow validation and regulatory assessments.

## Interview walkthrough

1. Explain the overall modular monolith architecture and how EF Core owns persistence.
2. Trace the login-to-clinic-authorization path for a patient or appointment request.
3. Explain what can go wrong when two staff members book the same timeslot simultaneously, and how SERIALIZABLE transactions and rowversion help.
4. Show the GitHub Actions run that creates a temporary SQL Server and tests the real HTTP endpoints.
5. Discuss trade-offs, limitations and a prioritized plan for production hardening.

The goal is to demonstrate practical backend engineering and the ability to reason about data integrity, rather than claim that this educational project is a complete clinical platform.
