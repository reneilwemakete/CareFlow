using System.Security.Claims;
using CareFlow.Domain.Entities;
using CareFlow.Infrastructure.Identity;
using CareFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CareFlow.Api.Endpoints;

public static class PractitionerEndpoints
{
    public sealed record PractitionerRequest(string FirstName, string LastName, string? Specialty);
    public sealed record PractitionerResponse(Guid Id, string FirstName, string LastName,
        string? Specialty, bool IsActive);

    private static PractitionerResponse Response(Practitioner p) =>
        new(p.Id, p.FirstName, p.LastName, p.Specialty, p.IsActive);

    private static Dictionary<string, string[]> Validate(PractitionerRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.FirstName) || request.FirstName.Trim().Length > 100)
            errors["firstName"] = ["First name must contain 1 to 100 characters."];
        if (string.IsNullOrWhiteSpace(request.LastName) || request.LastName.Trim().Length > 100)
            errors["lastName"] = ["Last name must contain 1 to 100 characters."];
        if (request.Specialty?.Trim().Length > 100)
            errors["specialty"] = ["Specialty must not exceed 100 characters."];
        return errors;
    }

    private static async Task<(ApplicationUser? Staff, bool IsAdministrator)> StaffAsync(
        ClaimsPrincipal principal, UserManager<ApplicationUser> users,
        CareFlowDbContext db, CancellationToken ct)
    {
        var staff = await users.GetUserAsync(principal);
        if (staff is null || !staff.IsActive ||
            !await db.Clinics.AnyAsync(c => c.Id == staff.ClinicId && c.IsActive, ct))
            return (null, false);
        var isAdmin = await users.IsInRoleAsync(staff, "Administrator");
        var canRead = isAdmin || await users.IsInRoleAsync(staff, "Receptionist");
        return canRead ? (staff, isAdmin) : (null, false);
    }

    private static void Audit(CareFlowDbContext db, ApplicationUser actor,
        Practitioner practitioner, string action)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            ClinicId = actor.ClinicId,
            ActorUserId = actor.Id,
            Action = action,
            EntityType = "Practitioner",
            EntityId = practitioner.Id.ToString(),
            CorrelationId = System.Diagnostics.Activity.Current?.Id
        });
    }

    public static IEndpointRouteBuilder MapPractitionerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/practitioners")
            .RequireAuthorization()
            .WithTags("Practitioners");

        group.MapPost("/", async Task<IResult> (
            PractitionerRequest request, ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, CareFlowDbContext db, CancellationToken ct) =>
        {
            var (staff, isAdmin) = await StaffAsync(principal, users, db, ct);
            if (staff is null || !isAdmin) return Results.Forbid();
            var errors = Validate(request);
            if (errors.Count != 0) return Results.ValidationProblem(errors);
            var practitioner = new Practitioner
            {
                ClinicId = staff.ClinicId,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                Specialty = request.Specialty?.Trim()
            };
            db.Practitioners.Add(practitioner);
            Audit(db, staff, practitioner, "PractitionerCreated");
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/practitioners/{practitioner.Id}", Response(practitioner));
        });

        group.MapGet("/", async Task<IResult> (
            string? search, bool? includeInactive, int? page, int? pageSize,
            ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            CareFlowDbContext db, CancellationToken ct) =>
        {
            var (staff, _) = await StaffAsync(principal, users, db, ct);
            if (staff is null) return Results.Forbid();
            var currentPage = page ?? 1;
            var size = pageSize ?? 20;
            if (currentPage < 1 || size is < 1 or > 50 ||
                search?.Length > 100 || (long)(currentPage - 1) * size > int.MaxValue)
                return Results.BadRequest(new { message = "Invalid search or pagination parameters." });
            var query = db.Practitioners.AsNoTracking().Where(p => p.ClinicId == staff.ClinicId);
            if (includeInactive != true) query = query.Where(p => p.IsActive);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(p => p.FirstName.StartsWith(term) ||
                    p.LastName.StartsWith(term) ||
                    (p.Specialty != null && p.Specialty.StartsWith(term)));
            }
            var total = await query.CountAsync(ct);
            var rows = await query.OrderBy(p => p.LastName).ThenBy(p => p.FirstName)
                .ThenBy(p => p.Id).Skip((currentPage - 1) * size).Take(size)
                .Select(p => new PractitionerResponse(p.Id, p.FirstName, p.LastName,
                    p.Specialty, p.IsActive)).ToListAsync(ct);
            return Results.Ok(new { page = currentPage, pageSize = size, total, items = rows });
        });

        group.MapGet("/{id:guid}", async Task<IResult> (
            Guid id, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            CareFlowDbContext db, CancellationToken ct) =>
        {
            var (staff, _) = await StaffAsync(principal, users, db, ct);
            if (staff is null) return Results.Forbid();
            var practitioner = await db.Practitioners.AsNoTracking().SingleOrDefaultAsync(p =>
                p.Id == id && p.ClinicId == staff.ClinicId, ct);
            return practitioner is null ? Results.NotFound() : Results.Ok(Response(practitioner));
        });

        group.MapPut("/{id:guid}", async Task<IResult> (
            Guid id, PractitionerRequest request, ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, CareFlowDbContext db, CancellationToken ct) =>
        {
            var (staff, isAdmin) = await StaffAsync(principal, users, db, ct);
            if (staff is null || !isAdmin) return Results.Forbid();
            var errors = Validate(request);
            if (errors.Count != 0) return Results.ValidationProblem(errors);
            var practitioner = await db.Practitioners.SingleOrDefaultAsync(p =>
                p.Id == id && p.ClinicId == staff.ClinicId && p.IsActive, ct);
            if (practitioner is null) return Results.NotFound();
            practitioner.FirstName = request.FirstName.Trim();
            practitioner.LastName = request.LastName.Trim();
            practitioner.Specialty = request.Specialty?.Trim();
            Audit(db, staff, practitioner, "PractitionerUpdated");
            await db.SaveChangesAsync(ct);
            return Results.Ok(Response(practitioner));
        });

        group.MapPost("/{id:guid}/deactivate", async Task<IResult> (
            Guid id, ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            CareFlowDbContext db, CancellationToken ct) =>
        {
            var (staff, isAdmin) = await StaffAsync(principal, users, db, ct);
            if (staff is null || !isAdmin) return Results.Forbid();
            var practitioner = await db.Practitioners.SingleOrDefaultAsync(p =>
                p.Id == id && p.ClinicId == staff.ClinicId, ct);
            if (practitioner is null) return Results.NotFound();
            if (!practitioner.IsActive)
                return Results.Conflict(new { message = "Practitioner is already inactive." });

            var hasUpcoming = await db.Appointments.AnyAsync(a =>
                a.ClinicId == staff.ClinicId && a.PractitionerId == id &&
                a.Status == CareFlow.Domain.Enums.AppointmentStatus.Scheduled &&
                a.EndUtc > DateTime.UtcNow, ct);
            if (hasUpcoming)
                return Results.Conflict(new { message = "Reassign or cancel upcoming appointments before deactivating." });

            practitioner.IsActive = false;
            Audit(db, staff, practitioner, "PractitionerDeactivated");
            await db.SaveChangesAsync(ct);
            return Results.Ok(Response(practitioner));
        });

        return app;
    }
}
