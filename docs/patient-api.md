# Patient Management API (first implementation)

This is an educational prototype. Use **fictional data only**.

## Authentication

`POST /api/v1/auth/login` accepts `{ "email": "staff@example.test", "password": "..." }` and returns an ASP.NET Core Identity bearer token for an active, previously provisioned staff user belonging to an active clinic. Accounts must be provisioned securely; there is deliberately **no public registration or default administrator**. Login is rate limited.

### Initial administrator provisioning

The standalone `tools/CareFlow.Bootstrap` CLI creates a **new fictional clinic** and its first Administrator. It is not an HTTP endpoint and requires an explicit confirmation flag. It refuses to re-bootstrap an existing clinic and must only be run in a trusted environment with database access.

Set `ConnectionStrings__CareFlowDb` securely, then set `CAREFLOW_BOOTSTRAP_CLINIC_CODE`, `CAREFLOW_BOOTSTRAP_CLINIC_NAME`, `CAREFLOW_BOOTSTRAP_EMAIL` and `CAREFLOW_BOOTSTRAP_PASSWORD` in the environment. Never store the password in GitHub, scripts, or shell history. Run:

```sh
dotnet run --project tools/CareFlow.Bootstrap -- --confirm-initial-admin
```

This initializes only the first administrator for a new clinic. Subsequent staff onboarding is not implemented and should use a separate audited workflow. The bootstrap tool is verified against disposable SQL Server in GitHub Actions. Tokens expire after one hour; use HTTPS and never commit credentials.

## Patient endpoints

All patient routes require bearer authentication and a currently active Administrator or Receptionist account:

| Method | Path | Behavior |
|---|---|---|
| POST | /api/v1/patients | Register a patient within the authenticated staff member's clinic |
| GET | /api/v1/patients?search=&page=1&pageSize=20 | Search by patient number or name prefix, with pagination |
| GET | /api/v1/patients/{id} | Get an active patient in the staff member's clinic |
| PUT | /api/v1/patients/{id} | Update details, requiring the latest `rowVersion` |

Patients in other clinics cannot be read or updated. The clinic ID is never accepted from the request body. Duplicate patient numbers within a clinic return 409. Validation errors return 400. Successful creation returns 201. Missing or unavailable patients return 404.

The `rowVersion` property is a base64-encoded SQL Server concurrency token provided by the GET endpoint. Send it unchanged in PUT requests; stale versions return 409.

## Example registration

```json
{
  "patientNumber": "DEMO-0001",
  "firstName": "Example",
  "lastName": "Patient",
  "dateOfBirth": "2000-05-10",
  "phoneNumber": null,
  "email": null
}
```

The API logs creation and update events in AuditEvents without recording patient contact details. Logs do not constitute a complete healthcare audit or compliance solution.

## Outstanding work

- Add audited onboarding for subsequent staff; the guarded first-admin CLI is implemented.
- Continue expanding SQL Server-backed integration tests and authentication negative cases. The current suite covers staff login, failed password tracking, practitioner access denial, clinic isolation, optimistic concurrency, audit event creation and duplicate patient numbers.
- The integration suite runs the application in an in-process HTTP test host backed by real disposable SQL Server. Deployment, HTTPS ingress and production-style infrastructure still require testing.
- Revisit the authentication token revocation strategy before any production use.

Never use real patient information or expose this prototype publicly as a healthcare service.
