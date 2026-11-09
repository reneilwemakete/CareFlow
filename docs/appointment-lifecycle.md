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

**Current implementation:** A successful check-in persists `CheckedInAtUtc` on the appointment and records a separate `AppointmentCheckedIn` audit event in the same transaction. The appointment's `Status` remains `Scheduled` until completion. GET responses expose `checkedInAtUtc`; lifecycle responses also include `checkedIn`. The `PersistAppointmentCheckIn` migration backfills prior check-ins recorded in audit history.

Checked-in appointments cannot be cancelled or rescheduled through the existing endpoints.

## Completion

Completion requires that the scheduled appointment has started and has a persisted `CheckedInAtUtc` value. It sets `Status = Completed` and writes an `AppointmentCompleted` audit event. Repeating completion returns HTTP 409.

## Tests

SQL Server integration tests verify rescheduling, token staleness, releasing the old slot, duplicate check-in prevention, cancellation rejection after check-in, completion gating and audit entries, alongside existing patient, practitioner and scheduling scenarios.

Outstanding: lifecycle-specific permissions, robust no-show transitions, comprehensive concurrent transition load tests and production deployment controls.
