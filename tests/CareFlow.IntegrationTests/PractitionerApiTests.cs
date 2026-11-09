using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CareFlow.Domain.Entities;
using CareFlow.Domain.Enums;
using CareFlow.Infrastructure.Identity;
using CareFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CareFlow.IntegrationTests;

public sealed class PractitionerApiTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _app = new();
    private HttpClient _client = null!;
    private Guid _clinicId;
    private Guid _foreignClinicId;
    private string _adminEmail = null!;
    private const string Password = "PractitionerTest!12345";

    public async Task InitializeAsync()
    {
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CareFlowDbContext>();
        await db.Database.MigrateAsync();

        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Administrator", "Receptionist" })
            if (!await roles.RoleExistsAsync(role))
                Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);

        _clinicId = Guid.NewGuid();
        _foreignClinicId = Guid.NewGuid();
        db.Clinics.AddRange(
            new Clinic { Id = _clinicId, Code = "PM" + Guid.NewGuid().ToString("N")[..12], Name = "Fictional Practitioner Clinic" },
            new Clinic { Id = _foreignClinicId, Code = "FX" + Guid.NewGuid().ToString("N")[..12], Name = "Fictional Foreign Clinic" });
        await db.SaveChangesAsync();

        _adminEmail = $"pm-{Guid.NewGuid():N}@example.test";
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = new ApplicationUser
        {
            Email = _adminEmail, UserName = _adminEmail, ClinicId = _clinicId,
            EmailConfirmed = true, LockoutEnabled = true
        };
        Assert.True((await users.CreateAsync(admin, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(admin, "Administrator")).Succeeded);
        await LoginAsync(_adminEmail);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private async Task LoginAsync(string email)
    {
        var result = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        using var body = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
        var token = body.RootElement.GetProperty("accessToken").GetString();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [Fact]
    public async Task AdministratorCanCreateSearchUpdateAndDeactivatePractitioner()
    {
        var lastName = "Demo" + Guid.NewGuid().ToString("N")[..10];
        var create = await _client.PostAsJsonAsync("/api/v1/practitioners", new
        {
            firstName = "Fictional", lastName, specialty = "General"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var body = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("id").GetGuid();

        var search = await _client.GetAsync("/api/v1/practitioners?search=" + lastName);
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        using var searchBody = JsonDocument.Parse(await search.Content.ReadAsStringAsync());
        Assert.Equal(1, searchBody.RootElement.GetProperty("total").GetInt32());

        var updated = await _client.PutAsJsonAsync($"/api/v1/practitioners/{id}", new
        {
            firstName = "Changed", lastName, specialty = "Cardiology"
        });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var deactivated = await _client.PostAsync($"/api/v1/practitioners/{id}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        var inactive = await _client.GetAsync($"/api/v1/practitioners/{id}");
        Assert.Equal(HttpStatusCode.OK, inactive.StatusCode);
        using var inactiveBody = JsonDocument.Parse(await inactive.Content.ReadAsStringAsync());
        Assert.False(inactiveBody.RootElement.GetProperty("isActive").GetBoolean());

        var activeSearch = await _client.GetAsync("/api/v1/practitioners?search=" + lastName);
        using var activeBody = JsonDocument.Parse(await activeSearch.Content.ReadAsStringAsync());
        Assert.Equal(0, activeBody.RootElement.GetProperty("total").GetInt32());

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CareFlowDbContext>();
        Assert.Equal(3, await db.AuditEvents.CountAsync(e => e.ClinicId == _clinicId &&
            e.EntityType == "Practitioner" && e.EntityId == id.ToString()));
    }

    [Fact]
    public async Task DifferentClinicPractitionerIsNotAccessible()
    {
        var id = Guid.NewGuid();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CareFlowDbContext>();
            db.Practitioners.Add(new Practitioner
            {
                Id = id, ClinicId = _foreignClinicId,
                FirstName = "Foreign", LastName = "Clinician"
            });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.GetAsync($"/api/v1/practitioners/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await _client.PutAsJsonAsync($"/api/v1/practitioners/{id}",
                new { firstName = "Changed", lastName = "Clinician", specialty = "General" })).StatusCode);
    }

    [Fact]
    public async Task ReceptionistMayReadButCannotModifyPractitioners()
    {
        var email = $"reception-{Guid.NewGuid():N}@example.test";
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var staff = new ApplicationUser
            {
                Email = email, UserName = email, ClinicId = _clinicId,
                EmailConfirmed = true, LockoutEnabled = true
            };
            Assert.True((await users.CreateAsync(staff, Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(staff, "Receptionist")).Succeeded);
        }
        await LoginAsync(email);
        Assert.Equal(HttpStatusCode.OK,
            (await _client.GetAsync("/api/v1/practitioners")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await _client.PostAsJsonAsync("/api/v1/practitioners",
                new { firstName = "New", lastName = "Clinician", specialty = "General" })).StatusCode);
    }

    [Fact]
    public async Task UpcomingScheduledAppointmentsBlockDeactivation()
    {
        var practitionerId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CareFlowDbContext>();
            db.Practitioners.Add(new Practitioner
            {
                Id = practitionerId, ClinicId = _clinicId,
                FirstName = "Scheduled", LastName = "Doctor"
            });
            db.Patients.Add(new Patient
            {
                Id = patientId, ClinicId = _clinicId,
                PatientNumber = "Q" + Guid.NewGuid().ToString("N")[..12],
                FirstName = "Demo", LastName = "Patient",
                DateOfBirth = new DateOnly(1999, 1, 1)
            });
            await db.SaveChangesAsync();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var admin = await users.FindByEmailAsync(_adminEmail);
            Assert.NotNull(admin);
            db.Appointments.Add(new Appointment
            {
                ClinicId = _clinicId, PatientId = patientId,
                PractitionerId = practitionerId, CreatedByUserId = admin.Id,
                StartUtc = DateTime.UtcNow.AddDays(3),
                EndUtc = DateTime.UtcNow.AddDays(3).AddMinutes(30),
                Status = AppointmentStatus.Scheduled
            });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict,
            (await _client.PostAsync($"/api/v1/practitioners/{practitionerId}/deactivate", null)).StatusCode);
    }
}
