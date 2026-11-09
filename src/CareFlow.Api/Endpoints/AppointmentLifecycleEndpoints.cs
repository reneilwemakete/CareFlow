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

public static class AppointmentLifecycleEndpoints
{
    public sealed record RescheduleRequest(DateTime StartUtc, DateTime EndUtc, Guid PractitionerId, string RowVersion);
    public sealed record LifecycleResponse(Guid Id, Guid PractitionerId, DateTime StartUtc,
        DateTime EndUtc, AppointmentStatus Status, bool CheckedIn, string RowVersion);

    private static LifecycleResponse Response(Appointment a, bool checkedIn) =>
        new(a.Id, a.PractitionerId, a.StartUtc, a.EndUtc, a.Status, checkedIn,
            Convert.ToBase64String(a.RowVersion));

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

    private static bool SqlContention(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
            if (e is SqlException sql && sql.Number is 1205 or 1222)
                return true;
        return false;
    }

    private static void Audit(CareFlowDbContext db, ApplicationUser staff, Appointment appointment, string action)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            ClinicId = staff.ClinicId,
            ActorUserId = staff.Id,
            Action = action,
            EntityType = "Appointment",
            EntityId = appointment.Id.ToString(),
            CorrelationId = System.Diagnostics.Activity.Current?.Id
        });
    }

    public static IEndpointRouteBuilder MapAppointmentLifecycle(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/appointments").RequireAuthorization().WithTags("Appointment lifecycle");

        group.MapPost("/{id:guid}/reschedule", async Task<IResult> (
            Guid id, RescheduleRequest request, ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, CareFlowDbContext db, CancellationToken ct) =>
        {
            var staff = await StaffAsync(principal, users, db, ct);
            if (staff is null) return Results.Forbid();

            try { AppointmentRules.ValidateInterval(request.StartUtc, request.EndUtc); }
            catch (ArgumentException) { return Results.BadRequest(new { message = "A valid UTC interval is required." }); }
            if (request.StartUtc <= DateTime.UtcNow || request.EndUtc > request.StartUtc.AddHours(8))
                return Results.BadRequest(new { message = "Appointment must be in the future and at most eight hours." });

            byte[] original;
            try { original = Convert.FromBase64String(request.RowVersion ?? ""); }
            catch (FormatException) { original = []; }
            if (original.Length != 8)
                return Results.BadRequest(new { message = "Supply the latest base64 rowVersion." });

            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                var appointment = await db.Appointments.SingleOrDefaultAsync(a =>
                    a.Id == id && a.ClinicId == staff.ClinicId, ct);
                if (appointment is null) return Results.NotFound();
                if (appointment.Status != AppointmentStatus.Scheduled ||
                    appointment.EndUtc <= DateTime.UtcNow)
                    return Results.Conflict(new { message = "Only upcoming scheduled appointments may be rescheduled." });
                if (await db.AuditEvents.AnyAsync(e => e.ClinicId == staff.ClinicId &&
                    e.EntityType == "Appointment" && e.EntityId == id.ToString() &&
                    e.Action == "AppointmentCheckedIn", ct))
                    return Results.Conflict(new { message = "A checked-in appointment cannot be rescheduled." });
                if (!await db.Practitioners.AnyAsync(p => p.ClinicId == staff.ClinicId &&
                    p.Id == request.PractitionerId && p.IsActive, ct))
                    return Results.NotFound();

                var available = await db.PractitionerAvailabilities.AnyAsync(a =>
                    a.ClinicId == staff.ClinicId && a.PractitionerId == request.PractitionerId &&
                    a.IsAvailable && a.StartUtc <= request.StartUtc && a.EndUtc >= request.EndUtc, ct);
                var blocked = await db.PractitionerAvailabilities.AnyAsync(a =>
                    a.ClinicId == staff.ClinicId && a.PractitionerId == request.PractitionerId &&
                    !a.IsAvailable && a.StartUtc < request.EndUtc && a.EndUtc > request.StartUtc, ct);
                var overlapping = await db.Appointments.AnyAsync(a =>
                    a.ClinicId == staff.ClinicId && a.Id != id &&
                    a.PractitionerId == request.PractitionerId &&
                    a.Status == AppointmentStatus.Scheduled &&
                    a.StartUtc < request.EndUtc && a.EndUtc > request.StartUtc, ct);
                if (!available || blocked || overlapping)
                    return Results.Conflict(new { message = "The requested practitioner or timeslot is unavailable." });

                db.Entry(appointment).Property(a => a.RowVersion).OriginalValue = original;
                appointment.PractitionerId = request.PractitionerId;
                appointment.StartUtc = request.StartUtc;
                appointment.EndUtc = request.EndUtc;
                appointment.UpdatedAtUtc = DateTime.UtcNow;
                Audit(db, staff, appointment, "AppointmentRescheduled");
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return Results.Ok(Response(appointment, false));
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { message = "Appointment changed. Refresh before rescheduling." });
            }
            catch (Exception ex) when (SqlContention(ex))
            {
                return Results.Conflict(new { message = "Concurrent scheduling conflict. Refresh and retry." });
            }
        });

        group.MapPost("/{id:guid}/check-in", async Task<IResult> (
            Guid id, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            CareFlowDbContext db, CancellationToken ct) =>
        {
            var staff = await StaffAsync(principal, users, db, ct);
            if (staff is null) return Results.Forbid();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                var appointment = await db.Appointments.SingleOrDefaultAsync(a =>
                    a.Id == id && a.ClinicId == staff.ClinicId, ct);
                if (appointment is null) return Results.NotFound();
                var now = DateTime.UtcNow;
                if (appointment.Status != AppointmentStatus.Scheduled ||
                    now < appointment.StartUtc.AddMinutes(-30) || now >= appointment.EndUtc)
                    return Results.Conflict(new { message = "Check-in is only allowed from 30 minutes before the appointment until its end." });
                if (await db.AuditEvents.AnyAsync(e => e.ClinicId == staff.ClinicId &&
                    e.EntityType == "Appointment" && e.EntityId == id.ToString() &&
                    e.Action == "AppointmentCheckedIn", ct))
                    return Results.Conflict(new { message = "This appointment is already checked in." });

                // Until a dedicated check-in table is introduced, the durable audit entry tracks check-in.
                Audit(db, staff, appointment, "AppointmentCheckedIn");
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return Results.Ok(Response(appointment, true));
            }
            catch (Exception ex) when (SqlContention(ex))
            {
                return Results.Conflict(new { message = "Concurrent check-in. Refresh and retry." });
            }
        });

        group.MapPost("/{id:guid}/complete", async Task<IResult> (
            Guid id, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            CareFlowDbContext db, CancellationToken ct) =>
        {
            var staff = await StaffAsync(principal, users, db, ct);
            if (staff is null) return Results.Forbid();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            try
            {
                var appointment = await db.Appointments.SingleOrDefaultAsync(a =>
                    a.Id == id && a.ClinicId == staff.ClinicId, ct);
                if (appointment is null) return Results.NotFound();
                if (appointment.Status != AppointmentStatus.Scheduled ||
                    DateTime.UtcNow < appointment.StartUtc)
                    return Results.Conflict(new { message = "Only started scheduled appointments can be completed." });
                var checkedIn = await db.AuditEvents.AnyAsync(e =>
                    e.ClinicId == staff.ClinicId && e.EntityType == "Appointment" &&
                    e.EntityId == id.ToString() && e.Action == "AppointmentCheckedIn", ct);
                if (!checkedIn)
                    return Results.Conflict(new { message = "Check in the appointment before completion." });

                appointment.Status = AppointmentStatus.Completed;
                appointment.UpdatedAtUtc = DateTime.UtcNow;
                Audit(db, staff, appointment, "AppointmentCompleted");
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return Results.Ok(Response(appointment, true));
            }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { message = "Appointment changed. Refresh and retry." });
            }
            catch (Exception ex) when (SqlContention(ex))
            {
                return Results.Conflict(new { message = "Concurrent status change. Refresh and retry." });
            }
        });

        return app;
    }
}
