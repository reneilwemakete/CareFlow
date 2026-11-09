using System.Security.Claims;
using CareFlow.Domain.Enums;
using CareFlow.Infrastructure.Identity;
using CareFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CareFlow.Api.Endpoints;

public static class ClinicScheduleEndpoints
{
    public sealed record ScheduleItem(Guid AppointmentId, Guid PatientId,
        string PatientNumber, Guid PractitionerId, string PractitionerName,
        DateTime StartUtc, DateTime EndUtc, AppointmentStatus Status, DateTime? CheckedInAtUtc);

    public static IEndpointRouteBuilder MapClinicSchedule(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/schedule", async Task<IResult> (
            DateTime fromUtc, DateTime toUtc, Guid? practitionerId,
            ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            CareFlowDbContext db, CancellationToken ct) =>
        {
            var staff = await users.GetUserAsync(principal);
            if (staff is null || !staff.IsActive ||
                !await db.Clinics.AnyAsync(c => c.Id == staff.ClinicId && c.IsActive, ct) ||
                !(await users.IsInRoleAsync(staff, "Administrator") ||
                  await users.IsInRoleAsync(staff, "Receptionist")))
                return Results.Forbid();

            if (fromUtc.Kind != DateTimeKind.Utc || toUtc.Kind != DateTimeKind.Utc ||
                fromUtc >= toUtc || toUtc > fromUtc.AddDays(7))
                return Results.BadRequest(new { message = "Supply a UTC date range of at most seven days." });

            var query = db.Appointments.AsNoTracking().Where(a =>
                a.ClinicId == staff.ClinicId && a.StartUtc < toUtc && a.EndUtc > fromUtc);
            if (practitionerId.HasValue)
                query = query.Where(a => a.PractitionerId == practitionerId.Value);

            var items = await query.OrderBy(a => a.StartUtc).ThenBy(a => a.Id)
                .Select(a => new ScheduleItem(a.Id, a.PatientId, a.Patient.PatientNumber,
                    a.PractitionerId, a.Practitioner.FirstName + " " + a.Practitioner.LastName,
                    a.StartUtc, a.EndUtc, a.Status, a.CheckedInAtUtc))
                .Take(501).ToListAsync(ct);
            if (items.Count > 500)
                return Results.BadRequest(new { message = "More than 500 appointments. Narrow the date range or filter by practitioner." });
            return Results.Ok(new { fromUtc, toUtc, practitionerId, items });
        })
        .RequireAuthorization()
        .WithTags("Clinic schedule");
        return app;
    }
}
