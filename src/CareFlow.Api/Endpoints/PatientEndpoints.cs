using CareFlow.Domain.Entities;
using CareFlow.Infrastructure.Identity;
using CareFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CareFlow.Api.Endpoints;

public static class PatientEndpoints
{
    public sealed record CreatePatientRequest(
        string PatientNumber,
        string FirstName,
        string LastName,
        DateOnly DateOfBirth,
        string? PhoneNumber,
        string? Email);

    public sealed record UpdatePatientRequest(
        string FirstName,
        string LastName,
        DateOnly DateOfBirth,
        string? PhoneNumber,
        string? Email,
        string RowVersion);

    public sealed record PatientResponse(
        Guid Id,
        string PatientNumber,
        string FirstName,
        string LastName,
        DateOnly DateOfBirth,
        string? PhoneNumber,
        string? Email,
        bool IsActive,
        string RowVersion);

    private static PatientResponse ToResponse(Patient patient) => new(
        patient.Id, patient.PatientNumber, patient.FirstName, patient.LastName,
        patient.DateOfBirth, patient.PhoneNumber, patient.Email,
        patient.IsActive, Convert.ToBase64String(patient.RowVersion));

    private static Dictionary<string, string[]> Validate(
        string firstName, string lastName, DateOnly birthDate, string? phone, string? email)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(firstName) || firstName.Trim().Length > 100)
            errors["firstName"] = ["First name must contain 1 to 100 characters."];
        if (string.IsNullOrWhiteSpace(lastName) || lastName.Trim().Length > 100)
            errors["lastName"] = ["Last name must contain 1 to 100 characters."];
        if (birthDate < new DateOnly(1900, 1, 1) || birthDate > DateOnly.FromDateTime(DateTime.UtcNow))
            errors["dateOfBirth"] = ["Enter a valid past date of birth."];
        if (phone?.Length > 25) errors["phoneNumber"] = ["Maximum 25 characters."];
        if (email is not null && (email.Length > 254 || (email.Length > 0 && !System.Net.Mail.MailAddress.TryCreate(email, out _))))
            errors["email"] = ["Enter a valid email address of at most 254 characters."];
        return errors;
    }

    private static async Task<ApplicationUser?> AuthorizedStaff(
        System.Security.Claims.ClaimsPrincipal principal,
        UserManager<ApplicationUser> users,
        CareFlowDbContext db,
        CancellationToken token)
    {
        var staff = await users.GetUserAsync(principal);
        if (staff is null || !staff.IsActive ||
            !await db.Clinics.AnyAsync(c => c.Id == staff.ClinicId && c.IsActive, token))
            return null;
        return await users.IsInRoleAsync(staff, "Administrator") || await users.IsInRoleAsync(staff, "Receptionist")
            ? staff : null;
    }

    public static IEndpointRouteBuilder MapPatientEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/patients")
            .RequireAuthorization()
            .WithTags("Patients");

        group.MapPost("/", async Task<IResult> (
            CreatePatientRequest request, System.Security.Claims.ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, CareFlowDbContext db,
            CancellationToken token) =>
        {
            var staff = await AuthorizedStaff(principal, users, db, token);
            if (staff is null) return Results.Forbid();

            var errors = Validate(request.FirstName, request.LastName,
                request.DateOfBirth, request.PhoneNumber, request.Email);
            if (string.IsNullOrWhiteSpace(request.PatientNumber) || request.PatientNumber.Trim().Length > 30)
                errors["patientNumber"] = ["Patient number must contain 1 to 30 characters."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var number = request.PatientNumber.Trim();
            if (await db.Patients.AnyAsync(p => p.ClinicId == staff.ClinicId && p.PatientNumber == number, token))
                return Results.Conflict(new { message = "This patient number is already registered at your clinic." });

            var patient = new Patient
            {
                ClinicId = staff.ClinicId,
                PatientNumber = number,
                FirstName = request.FirstName.Trim(),
                LastName = request.LastName.Trim(),
                DateOfBirth = request.DateOfBirth,
                PhoneNumber = request.PhoneNumber?.Trim(),
                Email = request.Email?.Trim()
            };
            db.Patients.Add(patient);
            try
            {
                await db.SaveChangesAsync(token);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException sql &&
                                                  sql.Number is 2601 or 2627)
            {
                return Results.Conflict(new { message = "This patient number is already registered at your clinic." });
            }
            return Results.Created($"/api/v1/patients/{patient.Id}", ToResponse(patient));
        });

        group.MapGet("/", async Task<IResult> (
            string? search, int? page, int? pageSize,
            System.Security.Claims.ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            CareFlowDbContext db, CancellationToken token) =>
        {
            var staff = await AuthorizedStaff(principal, users, db, token);
            if (staff is null) return Results.Forbid();
            var currentPage = page ?? 1;
            var size = pageSize ?? 20;
            if (currentPage < 1 || size is < 1 or > 50 || search?.Length > 100)
                return Results.BadRequest(new { message = "Invalid search or pagination parameters." });

            var query = db.Patients.AsNoTracking()
                .Where(p => p.ClinicId == staff.ClinicId && p.IsActive);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(p =>
                    p.PatientNumber.StartsWith(term) ||
                    p.FirstName.StartsWith(term) ||
                    p.LastName.StartsWith(term));
            }
            var total = await query.CountAsync(token);
            var patients = await query.OrderBy(p => p.PatientNumber)
                .Skip((currentPage - 1) * size).Take(size)
                .Select(p => new PatientResponse(
                    p.Id, p.PatientNumber, p.FirstName, p.LastName,
                    p.DateOfBirth, p.PhoneNumber, p.Email, p.IsActive,
                    Convert.ToBase64String(p.RowVersion)))
                .ToListAsync(token);
            return Results.Ok(new { page = currentPage, pageSize = size, total, items = patients });
        });

        group.MapGet("/{id:guid}", async Task<IResult> (
            Guid id, System.Security.Claims.ClaimsPrincipal principal,
            UserManager<ApplicationUser> users, CareFlowDbContext db, CancellationToken token) =>
        {
            var staff = await AuthorizedStaff(principal, users, db, token);
            if (staff is null) return Results.Forbid();
            var patient = await db.Patients.AsNoTracking().SingleOrDefaultAsync(
                p => p.Id == id && p.ClinicId == staff.ClinicId && p.IsActive, token);
            return patient is null ? Results.NotFound() : Results.Ok(ToResponse(patient));
        });

        group.MapPut("/{id:guid}", async Task<IResult> (
            Guid id, UpdatePatientRequest request,
            System.Security.Claims.ClaimsPrincipal principal, UserManager<ApplicationUser> users,
            CareFlowDbContext db, CancellationToken token) =>
        {
            var staff = await AuthorizedStaff(principal, users, db, token);
            if (staff is null) return Results.Forbid();
            var errors = Validate(request.FirstName, request.LastName, request.DateOfBirth,
                request.PhoneNumber, request.Email);
            byte[] suppliedVersion;
            try { suppliedVersion = Convert.FromBase64String(request.RowVersion ?? ""); }
            catch (FormatException) { suppliedVersion = []; }
            if (suppliedVersion.Length != 8)
                errors["rowVersion"] = ["A valid rowVersion from the latest GET is required."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var patient = await db.Patients.SingleOrDefaultAsync(
                p => p.Id == id && p.ClinicId == staff.ClinicId && p.IsActive, token);
            if (patient is null) return Results.NotFound();
            db.Entry(patient).Property(p => p.RowVersion).OriginalValue = suppliedVersion;
            patient.FirstName = request.FirstName.Trim();
            patient.LastName = request.LastName.Trim();
            patient.DateOfBirth = request.DateOfBirth;
            patient.PhoneNumber = request.PhoneNumber?.Trim();
            patient.Email = request.Email?.Trim();
            patient.UpdatedAtUtc = DateTime.UtcNow;
            try { await db.SaveChangesAsync(token); }
            catch (DbUpdateConcurrencyException)
            {
                return Results.Conflict(new { message = "Patient details changed. Refresh the record and retry." });
            }
            return Results.Ok(ToResponse(patient));
        });

        return app;
    }
}
