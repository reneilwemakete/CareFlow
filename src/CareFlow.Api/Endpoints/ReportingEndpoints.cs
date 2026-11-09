using System.Security.Claims;
using CareFlow.Domain.Enums;
using CareFlow.Infrastructure.Identity;
using CareFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CareFlow.Api.Endpoints;

public static class ReportingEndpoints
{
    public sealed record PractitionerLoad(Guid PractitionerId, string PractitionerName,
        int Scheduled, int Completed, int Cancelled, int NoShow, int Total);
    public sealed record ClinicSummary(DateTime FromUtc, DateTime ToUtc, int Total,
        int Scheduled, int Completed, int Cancelled, int NoShow, int CheckedIn,
        decimal AttendanceRatePercent, IReadOnlyList<PractitionerLoad> Practitioners);

    public static IEndpointRouteBuilder MapClinicReporting(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/reports/appointments", async Task<IResult> (
            DateTime fromUtc, DateTime toUtc, ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, CareFlowDbContext db, CancellationToken ct) =>
        {
            var staff = await users.GetUserAsync(principal);
            if (staff is null || !staff.IsActive ||
                !await db.Clinics.AnyAsync(c => c.Id == staff.ClinicId && c.IsActive, ct) ||
                !await users.IsInRoleAsync(staff, "Administrator"))
                return Results.Forbid();

            if (fromUtc.Kind != DateTimeKind.Utc || toUtc.Kind != DateTimeKind.Utc ||
                fromUtc >= toUtc || toUtc > fromUtc.AddDays(90))
                return Results.BadRequest(new { message = "Supply a UTC interval of at most 90 days." });

            // Aggregate within SQL Server, never materialize patient-level records for reporting.
            var query = db.Appointments.AsNoTracking()
                .Where(a => a.ClinicId == staff.ClinicId &&
                    a.StartUtc >= fromUtc && a.StartUtc < toUtc);
            var counts = await query.GroupBy(_ => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    Scheduled = g.Count(a => a.Status == AppointmentStatus.Scheduled),
                    Completed = g.Count(a => a.Status == AppointmentStatus.Completed),
                    Cancelled = g.Count(a => a.Status == AppointmentStatus.Cancelled),
                    NoShow = g.Count(a => a.Status == AppointmentStatus.NoShow),
                    CheckedIn = g.Count(a => a.CheckedInAtUtc != null)
                })
                .SingleOrDefaultAsync(ct);

            var loads = await query.GroupBy(a => new
                {
                    a.PractitionerId, a.Practitioner.FirstName, a.Practitioner.LastName
                })
                .Select(g => new
                {
                    g.Key.PractitionerId,
                    g.Key.FirstName,
                    g.Key.LastName,
                    Total = g.Count(),
                    Scheduled = g.Count(a => a.Status == AppointmentStatus.Scheduled),
                    Completed = g.Count(a => a.Status == AppointmentStatus.Completed),
                    Cancelled = g.Count(a => a.Status == AppointmentStatus.Cancelled),
                    NoShow = g.Count(a => a.Status == AppointmentStatus.NoShow)
                })
                .OrderByDescending(g => g.Total)
                .ThenBy(g => g.LastName)
                .ThenBy(g => g.FirstName)
                .ToListAsync(ct);

            // Attendance: checked-in bookings divided by completed or no-show bookings.
            // Excludes scheduled and cancelled bookings from the denominator.
            var total = counts?.Total ?? 0;
            var denominator = (counts?.Completed ?? 0) + (counts?.NoShow ?? 0);
            var attendance = denominator == 0 ? 0m :
                Math.Round(100m * (counts?.Completed ?? 0) / denominator, 1);

            return Results.Ok(new ClinicSummary(
                fromUtc, toUtc, total, counts?.Scheduled ?? 0, counts?.Completed ?? 0,
                counts?.Cancelled ?? 0, counts?.NoShow ?? 0, counts?.CheckedIn ?? 0,
                attendance,
                loads.Select(g => new PractitionerLoad(g.PractitionerId,
                    g.FirstName + " " + g.LastName, g.Scheduled, g.Completed,
                    g.Cancelled, g.NoShow, g.Total)).ToList()));
        })
        .RequireAuthorization()
        .WithTags("Clinic reporting");

        return app;
    }
}
