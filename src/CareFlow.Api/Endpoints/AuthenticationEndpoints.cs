using System.Security.Claims;
using CareFlow.Infrastructure.Identity;
using CareFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CareFlow.Api.Endpoints;

public static class AuthenticationEndpoints
{
    public sealed record LoginRequest(string Email, string Password);

    public static IEndpointRouteBuilder MapStaffAuthentication(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/auth/login", async Task<IResult> (
            LoginRequest request,
            HttpContext http,
            UserManager<ApplicationUser> users,
            CareFlowDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return Results.Unauthorized();

            var staff = await users.FindByEmailAsync(request.Email.Trim());
            if (staff is null || !staff.IsActive || !staff.EmailConfirmed ||
                !await db.Clinics.AnyAsync(c => c.Id == staff.ClinicId && c.IsActive, cancellationToken) ||
                await users.IsLockedOutAsync(staff))
                return Results.Unauthorized();

            if (!await users.CheckPasswordAsync(staff, request.Password))
            {
                await users.AccessFailedAsync(staff);
                return Results.Unauthorized();
            }
            await users.ResetAccessFailedCountAsync(staff);

            var roles = await users.GetRolesAsync(staff);
            if (!roles.Any(role => role is "Administrator" or "Receptionist" or "Practitioner"))
                return Results.Forbid();

            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, staff.Id),
                new(ClaimTypes.Email, staff.Email ?? string.Empty),
                new(ClaimTypes.Name, staff.UserName ?? staff.Id)
            };
            claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
            var identity = new ClaimsIdentity(claims, IdentityConstants.BearerScheme);
            await http.SignInAsync(IdentityConstants.BearerScheme, new ClaimsPrincipal(identity));
            return Results.Empty;
        })
        .AllowAnonymous()
        .RequireRateLimiting("login")
        .WithTags("Staff authentication");

        return app;
    }
}
