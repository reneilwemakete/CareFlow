using System.Data;
using System.Security.Claims;
using CareFlow.Domain.Entities;
using CareFlow.Domain.Enums;
using CareFlow.Domain.Rules;
using CareFlow.Infrastructure.Identity;
using CareFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CareFlow.Api.Endpoints;

public static class AppointmentEndpoints
{
    public sealed record AvailabilityRequest(Guid PractitionerId, DateTime StartUtc, DateTime EndUtc, bool IsAvailable);
    public sealed record BookAppointmentRequest(Guid PatientId, Guid PractitionerId, DateTime StartUtc, DateTime EndUtc, string? ReasonForVisit);
    public sealed record AppointmentView(Guid Id, Guid PatientId, Guid PractitionerId, DateTime StartUtc,
        DateTime EndUtc, AppointmentStatus Status, string? ReasonForVisit, DateTime? CheckedInAtUtc, string RowVersion);

    private static AppointmentView View(Appointment a) =>
        new(a.Id, a.PatientId, a.PractitionerId, a.StartUtc, a.EndUtc, a.Status,
            a.ReasonForVisit, a.CheckedInAtUtc, Convert.ToBase64String(a.RowVersion));

    private static async Task<ApplicationUser?> StaffAsync(ClaimsPrincipal principal,
        UserManager<ApplicationUser> users, CareFlowDbContext db, CancellationToken ct)
    {
        var staff = await users.GetUserAsync(principal);
        if (staff is null || !staff.IsActive || 
            !await db.Clinics.AnyAsync(c => c.Id == staff.ClinicId && c.IsActive, ct))
            return null;
        return await users.IsInRoleAsync(staff, "Administrator") ||
               await users.IsInRoleAsync(staff, "Receptionist") ? staff : null;
    }

    private static bool ValidUtc(DateTime start, DateTime end)
    {
        try { AppointmentRules.ValidateInterval(start, end); return true; }
        catch (ArgumentException) { return false; }
    }

    private static bool IsSqlConflict(Exception ex)
    {
        for (Exception? cursor = ex; cursor is not null; cursor = cursor.InnerException)
            if (cursor is SqlException sql && sql.Number is 1205 or 1222 or 2601 or 2627)
                return true;
        return false;
    }

    public static IEndpointRouteBuilder MapAppointmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1").RequireAuthorization().WithTags("Scheduling");

        group.MapPost("/availability", async Task<IResult> (
            AvailabilityRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            CareFlowDbContext db, CancellationToken ct) =>
        {
            var staff = await StaffAsync(principal, users, db, ct);
            if (staff is null) return Results.Forbid();
            if (!ValidUtc(request.StartUtc, request.EndUtc))
                return Results.BadRequest(new { message = "A valid UTC interval is required." });
            if (!await db.Practitioners.AnyAsync(p => p.Id == request.PractitionerId &&
                p.ClinicId == staff.ClinicId && p.IsActive, ct))
                return Results.NotFound();

            // Availability intervals are intentionally immutable once created in this phase.
            var availability = new PractitionerAvailability
            {
                ClinicId = staff.ClinicId,
                PractitionerId = request.PractitionerId,
                StartUtc = request.StartUtc,
                EndUtc = request.EndUtc,
                IsAvailable = request.IsAvailable
            };
            db.PractitionerAvailabilities.Add(availability);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/availability/{availability.Id}", new
            {
                availability.Id, availability.PractitionerId, availability.StartUtc,
                availability.EndUtc, availability.IsAvailable
            });
        });

        group.MapGet("/availability", async Task<IResult> (
            Guid practitionerId, DateTime fromUtc, DateTime toUtc, ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, CareFlowDbContext db, CancellationToken ct) =>
        {
            var staff = await StaffAsync(principal, users, db, ct);
            if (staff is null) return Results.Forbid();
            if (!ValidUtc(fromUtc, toUtc)) return Results.BadRequest();
            var rows = await db.PractitionerAvailabilities.AsNoTracking()
                .Where(a => a.ClinicId == staff.ClinicId && a.PractitionerId == practitionerId &&
                    a.StartUtc < toUtc && a.EndUtc > fromUtc)
                .OrderBy(a => a.StartUtc)
                .Select(a => new { a.Id, a.PractitionerId, a.StartUtc, a.EndUtc, a.IsAvailable })
                .ToListAsync(ct);
            return Results.Ok(rows);
        });

        group.MapPost("/appointments", async Task<IResult> (
            BookAppointmentRequest request, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            CareFlowDbContext db, CancellationToken ct) =>
        {
            var staff = await StaffAsync(principal, users, db, ct);
            if (staff is null) return Results.Forbid();
            if (!ValidUtc(request.StartUtc, request.EndUtc) ||
                request.StartUtc <= DateTime.UtcNow || request.EndUtc > request.StartUtc.AddHours(8) ||
                request.ReasonForVisit?.Length > 250)
                return Results.BadRequest(new { message = "Supply a future UTC booking of at most eight hours." });

            // Serializable isolation protects the range check from concurrent inserts.
            // In a contention deadlock, SQL Server aborts one transaction, returned as HTTP 409.
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                if (!await db.Patients.AnyAsync(p => p.ClinicId == staff.ClinicId &&
                    p.Id == request.PatientId && p.IsActive, ct) ||
                    !await db.Practitioners.AnyAsync(p => p.ClinicId == staff.ClinicId &&
                    p.Id == request.PractitionerId && p.IsActive, ct))
                    return Results.NotFound();

                var available = await db.PractitionerAvailabilities.AnyAsync(a =>
                    a.ClinicId == staff.ClinicId && a.PractitionerId == request.PractitionerId &&
                    a.IsAvailable && a.StartUtc <= request.StartUtc && a.EndUtc >= request.EndUtc, ct);
                var blocked = await db.PractitionerAvailabilities.AnyAsync(a =>
                    a.ClinicId == staff.ClinicId && a.PractitionerId == request.PractitionerId &&
                    !a.IsAvailable && a.StartUtc < request.EndUtc && a.EndUtc > request.StartUtc, ct);
                if (!available || blocked)
                    return Results.Conflict(new { message = "Practitioner is unavailable for the requested interval." });

                var overlaps = await db.Appointments.AnyAsync(a =>
                    a.ClinicId == staff.ClinicId && a.PractitionerId == request.PractitionerId &&
                    a.Status == AppointmentStatus.Scheduled &&
                    a.StartUtc < request.EndUtc && a.EndUtc > request.StartUtc, ct);
                if (overlaps)
                    return Results.Conflict(new { message = "Practitioner already has a booking in this interval." });

                var appointment = new Appointment
                {
                    ClinicId = staff.ClinicId, PatientId = request.PatientId,
                    PractitionerId = request.PractitionerId, StartUtc = request.StartUtc,
                    EndUtc = request.EndUtc, ReasonForVisit = request.ReasonForVisit?.Trim(),
                    CreatedByUserId = staff.Id
                };
                db.Appointments.Add(appointment);
                db.AuditEvents.Add(new AuditEvent
                {
                    ClinicId = staff.ClinicId, ActorUserId = staff.Id, Action = "AppointmentBooked",
                    EntityType = "Appointment", EntityId = appointment.Id.ToString()
                });
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return Results.Created($"/api/v1/appointments/{appointment.Id}", View(appointment));
            }
            catch (Exception ex) when (IsSqlConflict(ex))
            {
                return Results.Conflict(new { message = "Another booking changed the schedule. Retry with refreshed availability." });
            }
        });

        group.MapGet("/appointments/{id:guid}", async Task<IResult> (
            Guid id, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            CareFlowDbContext db, CancellationToken ct) =>
        {
            var staff = await StaffAsync(principal, users, db, ct);
            if (staff is null) return Results.Forbid();
            var a = await db.Appointments.AsNoTracking().SingleOrDefaultAsync(x =>
                x.ClinicId == staff.ClinicId && x.Id == id, ct);
            return a is null ? Results.NotFound() : Results.Ok(View(a));
        });

        group.MapPost("/appointments/{id:guid}/cancel", async Task<IResult> (
            Guid id, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            CareFlowDbContext db, CancellationToken ct) =>
        {
            var staff = await StaffAsync(principal, users, db, ct);
            if (staff is null) return Results.Forbid();
            var a = await db.Appointments.SingleOrDefaultAsync(x =>
                x.ClinicId == staff.ClinicId && x.Id == id, ct);
            if (a is null) return Results.NotFound();
            if (a.Status != AppointmentStatus.Scheduled)
                return Results.Conflict(new { message = "Only scheduled appointments can be cancelled." });
            if (a.CheckedInAtUtc.HasValue)
                return Results.Conflict(new { message = "Checked-in appointments cannot be cancelled." });

            a.Status = AppointmentStatus.Cancelled;
            a.UpdatedAtUtc = DateTime.UtcNow;
            db.AuditEvents.Add(new AuditEvent
            {
                ClinicId = staff.ClinicId, ActorUserId = staff.Id,
                Action = "AppointmentCancelled", EntityType = "Appointment",
                EntityId = a.Id.ToString()
            });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { message = "Appointment changed. Refresh and retry." });
            }
            return Results.Ok(View(a));
        });

        return app;
    }
}
