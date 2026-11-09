# CareFlow MVP Requirements (v1)

## Goal
Build a backend-only ASP.NET Core service for fictional clinic operations. The first milestone establishes entities, persistence configuration, tests and a health endpoint. The system is not intended for live clinical data.

## Roles
Administrator: staff account provisioning and management; Receptionist: patients and bookings; Practitioner: own schedule and appointment completion. Every future endpoint must enforce clinic and resource-level authorization.

## Core features for later milestones
- FR-001 Staff authentication and access control.
- FR-002 Administrator-provisioned accounts and role assignment.
- FR-003 Patient registration and updates.
- FR-004 Unique patient numbers per clinic.
- FR-005 Patient search with pagination and authorized filtering.
- FR-006 Practitioner profiles and explicit availability periods.
- FR-007 Appointments for registered patients and practitioners.
- FR-008 Reject overlapping active appointments for the same practitioner, including concurrent requests.
- FR-009 Reschedule and cancel scheduled appointments.
- FR-010 Practitioner-owned schedule and completion.
- FR-011 Appointment summaries.
- FR-012 Audit significant security and data actions.

## Constraints
Times stored in UTC. A patient and practitioner must belong to the same clinic as the appointment. Only administrators may provision users. No payment, prescriptions or diagnosis data. All sample data fictional.

## Acceptance requirements for phase 1
Solution structure exists; SQL Server EF configuration is present; core entities and constraint mappings are defined; clean CI build and basic domain tests are achievable once pushed to GitHub; database migration must be generated with dotnet ef, reviewed and applied in a local development environment.
