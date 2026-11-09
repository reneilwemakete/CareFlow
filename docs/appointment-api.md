# Appointment Scheduling API

Educational prototype: use fictional data only.

All scheduling routes require an authenticated, active Administrator or Receptionist at an active clinic. Clinic ID is derived from the staff account and never accepted from an API request.

| Method | Route | Purpose |
|---|---|---|
| POST | /api/v1/availability | Record an availability or blocked interval for a practitioner at the same clinic |
| GET | /api/v1/availability?practitionerId=...&fromUtc=...&toUtc=... | List clinic-specific availability intervals |
| POST | /api/v1/appointments | Book an active patient with an active practitioner |
| GET | /api/v1/appointments/{id} | Retrieve a booking within the staff member's clinic |
| POST | /api/v1/appointments/{id}/cancel | Cancel a scheduled booking |

## Booking request example

```json
{
  "patientId": "00000000-0000-0000-0000-000000000001",
  "practitionerId": "00000000-0000-0000-0000-000000000002",
  "startUtc": "2026-12-01T08:00:00Z",
  "endUtc": "2026-12-01T08:30:00Z",
  "reasonForVisit": "Fictional consultation"
}
```

These GUIDs are illustrative; use test records from the same fictional clinic.

Rules:
- Both timestamps must be UTC, future bookings may not exceed eight hours, and start must precede end.
- The booking must fit entirely within an available interval and must not overlap a blocked interval.
- Scheduled bookings for a practitioner may not overlap, including concurrent requests.
- Cancellation releases the occupied interval for new scheduling.
- SQL Server SERIALIZABLE transaction isolation protects the booking range check. SQL deadlock or timeout contention yields an HTTP 409 that the client can retry after refreshing.
- Only active patients and practitioners in the same clinic may be booked.
- The API records booking and cancellation audit events without storing patient contact details in audit events.

## Integration verification

The GitHub SQL Server integration suite includes unauthenticated access checks, staff login, patient clinic isolation, patient rowversion updates and audit events, scheduling creation, overlap prevention, cancelled-slot reuse, blocked availability, concurrent booking collisions and cross-clinic practitioner restriction. The tests run against disposable SQL Server with an ASP.NET Core test host.

## Next improvements

Practitioner CRUD, more granular staff permissions, recurring availability schedules, patient-side booking conflict policies, appointment rescheduling, check-in and completion, safe timezone display and expanded concurrent load tests are not implemented yet.
