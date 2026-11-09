# CareFlow backend architecture

## Scope and style

CareFlow is a modular monolith: a single ASP.NET Core 10 HTTP service with separate Domain, Application, Infrastructure and API projects. This intentionally avoids premature microservices, distributed transactions and additional operational complexity for an early-career C# backend portfolio.

\`\`\`mermaid
flowchart LR
    Staff[Authenticated Staff Client] --> HTTP[ASP.NET Core Minimal APIs]
    HTTP --> Auth[Identity Bearer Token + Role Checks]
    HTTP --> Scope[Clinic-scoped Queries and Validation]
    Scope --> Rules[Appointment Rules]
    Scope --> EF[EF Core 10]
    Auth --> EF
    EF --> DB[(SQL Server)]
    EF --> Audit[AuditEvents]
    CI[GitHub Actions] --> Unit[Domain Unit Tests]
    CI --> Integration[SQL Server Integration Tests]
    Integration --> HTTP
    Integration --> DB
\`\`\`

## Solution layers

| Project | Responsibility |
| --- | --- |
| \`CareFlow.Domain\` | Domain entities, appointment status and interval rules; does not depend on EF Core |
| \`CareFlow.Application\` | Reserved application layer for eventual extracted use cases and interfaces; currently minimal |
| \`CareFlow.Infrastructure\` | EF Core SQL Server context, mappings, migrations and ASP.NET Core Identity persistence |
| \`CareFlow.Api\` | Composition root, authentication, authorization and HTTP endpoint handlers |
| \`CareFlow.UnitTests\` | Pure domain tests |
| \`CareFlow.IntegrationTests\` | ASP.NET Core test host against disposable SQL Server; verifies end-to-end API behavior |
| \`CareFlow.Bootstrap\` | Standalone guarded first-clinic Administrator provisioning tool |

The core endpoint use cases currently live in \`CareFlow.Api/Endpoints\`. This is an intentional simplification of the reference architecture rather than a claim that all use cases have been extracted into the Application assembly.

## Tenant isolation

Staff users have a \`ClinicId\` stored in Identity. Sensitive operations retrieve the **current user from the database** and verify active clinic membership and current role. Patient, practitioner, appointment, availability, schedule and reporting data is queried using that trusted clinic ID, not a caller-supplied tenant identifier.

The schema also uses composite clinic-scoped keys for patient/practitioner references from appointments. These are important defence-in-depth safeguards and help stop cross-clinic database relationships.

## Booking concurrency

SQL Server \`SERIALIZABLE\` transactions encompass availability, blackout, patient conflict and practitioner conflict checks and inserts. This prevents two concurrent transactions from successfully claiming the same occupied period. Deadlocks/lock contention are returned as conflict responses rather than silently double booking.

Patient and appointment updates use SQL Server \`rowversion\` to detect stale versions. An HTTP 409 prompts a client to refresh before retrying.

## Appointment lifecycle

A booked appointment is initially \`Scheduled\`. Check-in sets \`CheckedInAtUtc\` while preserving the booking status until completion. Completion sets \`Status = Completed\`; cancellation uses \`Cancelled\`. A \`NoShow\` enum value exists, but the prototype does not yet implement a no-show transition endpoint.

Audit events record significant operations without copying patient contact details. Audit logging is an application feature, **not** a complete regulatory or clinical audit solution.

## Reporting

Reports use EF Core SQL aggregation and only return operational counts. Administrator-only access and clinic-scoped database predicates prevent raw patient information from appearing in the report payload. Attendance is a completion/no-show proxy, not a measure of clinical quality.

## Trade-offs and future work

The prototype uses a bearer token identity model with no production-grade token revocation service. Later phases could add staff invitations, practitioner self-service roles, explicit booking policies, observability and audited configuration changes. Before real healthcare use, a formal threat assessment, privacy evaluation, penetration testing, encryption and key management review and regulatory assessment would be mandatory.
