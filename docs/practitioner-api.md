# Practitioner Management API

Educational portfolio prototype. Use fictional practitioner details only.

All endpoints require staff authentication. The authenticated staff account determines the clinic scope; request bodies cannot choose another clinic.

| Method | Path | Access | Description |
|---|---|---|---|
| POST | /api/v1/practitioners | Administrator | Register a practitioner at the current clinic |
| GET | /api/v1/practitioners?search=&page=1&pageSize=20 | Administrator / Receptionist | Search and paginate practitioners within the current clinic |
| GET | /api/v1/practitioners/{id} | Administrator / Receptionist | Read a practitioner at the current clinic |
| PUT | /api/v1/practitioners/{id} | Administrator | Update an active practitioner's details |
| POST | /api/v1/practitioners/{id}/deactivate | Administrator | Deactivate practitioner if no upcoming scheduled appointments |

## Example request

```json
{
  "firstName": "Example",
  "lastName": "Practitioner",
  "specialty": "General Practice"
}
```

Fields are validated for required names and maximum lengths. The search endpoint returns up to 50 rows per page. By default it returns only active practitioners; use `includeInactive=true` to include all.

Deactivation does not delete practitioners or historic appointments. It is rejected with HTTP 409 when the practitioner has a scheduled appointment that has not ended. Practitioners from other clinics return HTTP 404 on scoped detail or modification operations. The API records create, update and deactivate actions in the clinic audit log.

## Verified

The SQL Server integration suite checks creation, search, update, deactivation, audit records, blocked deactivation for future bookings, clinic isolation and Receptionist read-only permissions. Standard build, existing patient and appointment integration cases also run.

## Remaining work

Implement staff-to-practitioner account linking, booking reassignment, optimistic concurrency for simultaneous edits, comprehensive appointment lifecycle management and production-ready administration. This prototype is not approved for real patient use.
