# CareFlow demonstration runbook

This walkthrough is for a **fictional clinic only**. Do not use real patient identities, medical information or passwords from any other system.

## Prepare the application

Follow [README local setup](../README.md#run-locally) to start SQL Server, apply the committed EF migrations, run the API and provision the initial fictional clinic administrator.

The examples below assume the API is running at `http://localhost:5084` on a trusted local development machine. The login endpoint should be accessed over HTTPS in any networked or deployed environment.

Use a REST client such as Postman or curl. Never publish real bearer tokens in screenshots, terminal recordings or GitHub.

## Demo 1: Log in

```http
POST /api/v1/auth/login
Content-Type: application/json

{
  "email": "fictional.admin@example.test",
  "password": "YOUR_LOCAL_DEMO_PASSWORD"
}
```

Replace the email/password with the fictional account provisioned by the bootstrap CLI. The response contains an `accessToken` and expiration information. In subsequent requests, send the token in the Authorization header:

```http
Authorization: Bearer YOUR_LOCAL_ACCESS_TOKEN
```

## Demo 2: Register a patient

```http
POST /api/v1/patients
Authorization: Bearer YOUR_LOCAL_ACCESS_TOKEN
Content-Type: application/json

{
  "patientNumber": "DEMO-1001",
  "firstName": "Example",
  "lastName": "Patient",
  "dateOfBirth": "2001-03-15",
  "phoneNumber": null,
  "email": null
}
```

Note the returned patient `id` and `rowVersion`. Create the same patient number again to demonstrate a duplicate rejection with HTTP 409.

## Demo 3: Create a practitioner

```http
POST /api/v1/practitioners
Authorization: Bearer YOUR_LOCAL_ACCESS_TOKEN
Content-Type: application/json

{
  "firstName": "Example",
  "lastName": "Practitioner",
  "specialty": "General Practice"
}
```

Note the returned practitioner `id`.

## Demo 4: Configure availability

Choose a **future UTC date**. The following values are illustrative and must be updated to a future date before running the demonstration.

```http
POST /api/v1/availability
Authorization: Bearer YOUR_LOCAL_ACCESS_TOKEN
Content-Type: application/json

{
  "practitionerId": "REPLACE_WITH_PRACTITIONER_GUID",
  "startUtc": "2027-01-15T08:00:00Z",
  "endUtc": "2027-01-15T12:00:00Z",
  "isAvailable": true
}
```

## Demo 5: Book an appointment

```http
POST /api/v1/appointments
Authorization: Bearer YOUR_LOCAL_ACCESS_TOKEN
Content-Type: application/json

{
  "patientId": "REPLACE_WITH_PATIENT_GUID",
  "practitionerId": "REPLACE_WITH_PRACTITIONER_GUID",
  "startUtc": "2027-01-15T09:00:00Z",
  "endUtc": "2027-01-15T09:30:00Z",
  "reasonForVisit": "Fictional consultation"
}
```

Replace the GUIDs and ensure the date matches your earlier future availability period. Try booking the same practitioner at an overlapping time. The second request should return HTTP 409.

To demonstrate rescheduling, first GET the appointment and copy its latest `rowVersion`. Then POST to `/api/v1/appointments/{id}/reschedule` with a future available interval, practitioner ID and the copied token. A stale token should be rejected.

## Demo 6: View the schedule and report

```http
GET /api/v1/schedule?fromUtc=2027-01-15T00:00:00Z&toUtc=2027-01-16T00:00:00Z
Authorization: Bearer YOUR_LOCAL_ACCESS_TOKEN
```

Optional: add `practitionerId=...` to filter the schedule.

```http
GET /api/v1/reports/appointments?fromUtc=2027-01-01T00:00:00Z&toUtc=2027-02-01T00:00:00Z
Authorization: Bearer YOUR_LOCAL_ACCESS_TOKEN
```

The reporting response includes totals and practitioner workload without returning patient names or contact details. The example January timestamps should be changed when demonstrating in another period.

## Demo 7: Show automated verification

Open [CareFlow GitHub Actions](https://github.com/reneilwemakete/CareFlow/actions) and show a successful build and SQL Server integration test workflow. Explain the cross-clinic data isolation and concurrent appointment conflict tests.

## Important limitations

The check-in endpoint only accepts appointments from 30 minutes before their start until their end. To demonstrate the entire check-in and completion lifecycle without waiting for a real appointment, use the automated SQL Server integration tests, which create deliberately timed **fictional** appointments.

Do not change system clocks, use real medical records or claim that this prototype is fit for a live healthcare setting.
