# CareFlow Architecture

## Decision: Modular monolith
One deployable ASP.NET Core API with separate Domain, Application and Infrastructure assemblies. A modular monolith minimizes deployment complexity for a portfolio-scale backend while preserving clear boundaries.

## Dependency direction
- Domain: entities, appointment status and rules. No EF Core dependency.
- Application: use cases and interfaces (implementation planned in phase 2).
- Infrastructure: EF Core SQL Server context, persistence mapping and ASP.NET Core Identity.
- API: composition root, HTTP surface, configuration and middleware. Does not contain business rules.

## Security design
No unsecured patient endpoints until login, authorization and clinic scope are implemented. Use Identity for password hashes and roles. Environment variables or local secrets for connections. Logs must avoid patient details and credentials.

## Booking concurrency plan
Use a transaction that serializes booking operations per practitioner before checking overlap. SQL Server rowversion guards updates but not concurrent inserts. Implement and integration-test concurrency safeguards before exposing booking endpoints.

## Caution
Identity is registered for persistence, but login/JWT/cookie authentication and authorization policies are intentionally not implemented during phase 1. Avoid deploying this scaffold publicly as a functional health application.
