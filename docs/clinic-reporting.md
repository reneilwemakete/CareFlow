# Clinic dashboard reporting

The administrator-only endpoint `GET /api/v1/reports/appointments` returns aggregate appointment statistics for a single clinic.

Required query parameters: `fromUtc` and `toUtc`, in UTC. The interval is start-inclusive and end-exclusive, with a maximum of 90 days. For example:

```http
GET /api/v1/reports/appointments?fromUtc=2026-12-01T00:00:00Z&toUtc=2027-01-01T00:00:00Z
Authorization: Bearer <staff token>
```

## Returned metrics

- Total, scheduled, completed, cancelled, no-show, checked-in appointments.
- `attendanceRatePercent` is a **completion-based attendance proxy**: 100 × completed ÷ (completed + no-show), rounded to one decimal. Scheduled and cancelled bookings are excluded from the denominator. Returns 0 if there are no completed/no-show bookings.
- Practitioner workload is the number of appointments assigned to each practitioner in the selected date range, including status breakdown. It is **not** hours worked, clinical productivity or a measure of care quality.

The endpoint uses EF Core server-side aggregates and restricts every query to the current authenticated administrator's active clinic. It does not return patient names, email addresses, phone numbers, diagnosis information or visit reasons.

## Security and interpretation

These statistics are appropriate for fictional portfolio demonstrations only. Counts might reveal operational activity and must not be exposed to anonymous users or cross-clinic staff. The prototype currently limits this endpoint to the Administrator role; Receptionists receive HTTP 403.

Scheduled appointments in the future and historical completed bookings are counted by appointment **start time**. No-show status is defined in the database but an automated no-show transition workflow has not yet been implemented.

The statistics are informational. No patient data or clinical outcomes should be inferred from appointment completion rates.
