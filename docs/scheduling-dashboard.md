# Persisted check-in and clinic schedule

This is an educational prototype that uses fictional records only.

## Check-in persistence

Appointments now include an optional `CheckedInAtUtc` timestamp in SQL Server. Check-in sets this timestamp in the same transaction as the `AppointmentCheckedIn` audit event. The appointment remains `Scheduled` until completion, but check-in no longer needs to be inferred from the audit event table.

The `PersistAppointmentCheckIn` EF Core migration adds the nullable column and backfills existing check-in timestamps from the earliest matching audit event within the same clinic and appointment. Legacy appointments without a check-in retain a null timestamp.

Rescheduling and cancelling a checked-in appointment are rejected. Completion requires the persisted check-in timestamp. Appointment GET responses include `checkedInAtUtc`.

## Scheduling endpoint

`GET /api/v1/schedule?fromUtc=2026-12-01T00%3A00%3A00Z&toUtc=2026-12-02T00%3A00%3A00Z`

The `practitionerId` query parameter can be used to filter by a specific practitioner. Only active Administrator and Receptionist staff may read the schedule; the clinic is derived from the authenticated user. The API returns appointment IDs, patient numbers, practitioner names, UTC start and end times, current statuses and checked-in timestamps.

The endpoint accepts a UTC interval no longer than seven days and returns a maximum of 500 appointments per request. Narrow the date interval or filter by practitioner if more results exist.

## Verification

GitHub Actions runs the integration suite against disposable SQL Server, covering persisted check-ins, lifecycle transitions, clinic schedule filtering and the existing patient, practitioner and appointment features. The migration workflow must also pass, applying the new schema to SQL Server.

## Remaining work

Staff-linked practitioner views, timezone-aware presentation, appointment pagination for high-volume clinics, further concurrent lifecycle stress tests and production authorization controls are future milestones. Do not use real patient data.
