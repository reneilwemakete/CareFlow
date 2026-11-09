# Appointment Lifecycle API

This remains a fictional-data-only portfolio prototype, not a production healthcare application.

All routes require a signed-in active clinic Administrator or Receptionist. The clinic is derived from the authenticated staff account, and appointment operations restrict records to that clinic.

| Method | Endpoint | Purpose |
|---|---|---|
| POST | /api/v1/appointments/{id}/reschedule | Move an upcoming scheduled appointment to a verified available interval |
| POST | /api/v1/appointments/{id}/check-in | Record a check-in for a scheduled appointment within its permitted window |
| POST | /api/v1/appointments/{id}/complete | Complete a started, checked-in appointment |

## Reschedule

Example payload:

```json
{
  "practitionerId": "00000000-0000-0000-0000-000000000002",
  "startUtc": "2026-12-01T09:00:00Z",
  "endUtc": "2026-12-01T09:30:00Z",
  "rowVersion": "COPY_BASE64_TOKEN_FROM_LATEST_GET"
}
```

The latest `rowVersion` is required to prevent stale writes. Rescheduling uses SQL Server SERIALIZABLE isolation to check practitioner activity, availability coverage, blackout intervals and overlapping appointments, excluding the booking being moved. If the interval is occupied or stale, the API returns HTTP 409. The appointment's patient remains unchanged.

## Check-in

Check-in is allowed from 30 minutes before the appointment starts until just before it ends. A second check-in returns HTTP 409.

**Current data model limitation:** A check-in is recorded as an `AppointmentCheckedIn` entry in `AuditEvents`; the appointment's persisted `Status` remains `Scheduled` until it is completed. The check-in response includes `checkedIn: true`. A future migration should add an explicit check-in timestamp or state to the appointment record and expose it in GET responses.

Checked-in appointments cannot be cancelled or rescheduled through the existing endpoints.

## Completion

Completion requires that the scheduled appointment has started and has a recorded check-in. It sets `Status = Completed` and writes an `AppointmentCompleted` audit event. Repeating completion returns HTTP 409.

## Tests

SQL Server integration tests verify rescheduling, token staleness, releasing the old slot, duplicate check-in prevention, cancellation rejection after check-in, completion gating and audit entries, alongside existing patient, practitioner and scheduling scenarios.

Outstanding: model-level check-in timestamp/state, lifecycle-specific permissions, patient-side scheduling policies, comprehensive concurrent transition load tests and production deployment controls.
