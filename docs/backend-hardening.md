# Backend hardening review

CareFlow is a portfolio prototype using fictional clinic and patient records only.

## Changes in this milestone

- A patient cannot be booked into overlapping scheduled appointments with different practitioners, including concurrent requests. The booking and reschedule checks run inside existing SQL Server SERIALIZABLE transactions.
- Login requires an active staff user with a confirmed email address, an active clinic and a valid staff role.
- The login rate limiter is partitioned by the connecting peer IP rather than pooling every staff member globally. Production deployments must configure trusted reverse proxies carefully and assess distributed rate limiting if more than one server is running.
- All `/api/v1` responses receive `Cache-Control: no-store` and `X-Content-Type-Options: nosniff`.

## Regression tests

The integration suite checks conflicting patient bookings across practitioners, refusal of unconfirmed email logins, sensitive response headers, prior role restrictions and clinic isolation. The SQL Server tests are run in GitHub Actions on pull requests.

## Remaining production work

This project is not certified for healthcare use. Before any real patient data, obtain a security and privacy assessment and implement appropriate data protection and audit retention policies. Also add session/token revocation, organization-wide configuration, vulnerability scanning, structured request tracing and a deployment-specific threat model. The current portfolio API remains intended only for fictional data.
