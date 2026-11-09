# CareFlow backend requirements and scope

## Goal

Demonstrate a multi-clinic healthcare operations API with C#, ASP.NET Core, EF Core and SQL Server. All patient, practitioner and clinic data in development and demos must be fictional. This repository is a portfolio prototype, not a healthcare production system.

## Implemented capabilities

| ID | Requirement | Status |
| --- | --- | --- |
| FR-001 | Staff bearer-token authentication and current role checks | Implemented |
| FR-002 | Guarded initial administrator creation for a new clinic | Implemented; subsequent staff onboarding not built |
| FR-003 | Clinic-scoped patient registration, details and updates | Implemented |
| FR-004 | Unique patient numbers within a clinic | Enforced by SQL Server unique index |
| FR-005 | Patient search with pagination | Implemented |
| FR-006 | Practitioner profiles and explicit availability | Implemented |
| FR-007 | Book registered patients with active practitioners | Implemented |
| FR-008 | Reject practitioner or patient scheduling overlaps, including concurrent requests | Implemented and integration tested |
| FR-009 | Reschedule and cancel eligible appointments | Implemented |
| FR-010 | Check-in timestamp and appointment completion | Implemented for Administrator / Receptionist |
| FR-011 | Clinic schedule and administrator summary reports | Implemented |
| FR-012 | Record significant appointment and practitioner activity | Implemented basic audit events |

## Role access

**Administrator:** managing patients and practitioners; scheduling and lifecycle actions; clinic schedule and reporting.

**Receptionist:** managing patients, booking and appointment lifecycle; read-only practitioner access and clinic schedule.

**Practitioner:** Identity role exists and users may log in, but practitioner-specific self-service appointment endpoints are **not yet implemented**. Practitioner users cannot automatically access administrative patient/scheduling endpoints.

## Technical constraints

- Every sensitive operation must derive the clinic from the authenticated staff account.
- The database must prevent invalid cross-clinic appointment relationships.
- Timestamps are UTC at the API boundary; a scheduling interval must have an end later than its start.
- Staff secrets and database credentials must not be stored in source control.
- Appointment booking and rescheduling must guard time-range conflicts under concurrent requests.
- Integration tests must use disposable SQL Server and fictional records.
- Patient-related endpoints and operational reports are not public.
- This project does not handle prescriptions, medical diagnoses, payments or clinical records.

## Out of scope for portfolio release

Production deployment, formal health regulatory compliance, secure staff invitation and lifecycle workflows, token revocation infrastructure, clinical reporting, patient-facing portals and multi-region operational support.
