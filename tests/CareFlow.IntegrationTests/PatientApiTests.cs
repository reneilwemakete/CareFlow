using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CareFlow.Domain.Entities;
using CareFlow.Infrastructure.Identity;
using CareFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CareFlow.IntegrationTests;

public sealed class PatientApiTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _app = new();
    private HttpClient _client = null!;
    private Guid _firstClinic;
    private Guid _secondClinic;
    private string _adminEmail = null!;
    private const string Password = "ClinicDemo!12345";

    public async Task InitializeAsync()
    {
        _client = _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        await using var scope = _app.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<CareFlowDbContext>();
        await db.Database.MigrateAsync();
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { "Administrator", "Receptionist", "Practitioner" })
        {
            if (!await roles.RoleExistsAsync(role))
                Assert.True((await roles.CreateAsync(new IdentityRole(role))).Succeeded);
        }

        _firstClinic = Guid.NewGuid();
        _secondClinic = Guid.NewGuid();
        db.Clinics.AddRange(
            new Clinic { Id = _firstClinic, Code = "A" + Guid.NewGuid().ToString("N")[..12], Name = "Fictional Clinic A" },
            new Clinic { Id = _secondClinic, Code = "B" + Guid.NewGuid().ToString("N")[..12], Name = "Fictional Clinic B" });
        await db.SaveChangesAsync();

        _adminEmail = $"admin-{Guid.NewGuid():N}@example.test";
        var user = new ApplicationUser
        {
            Email = _adminEmail, UserName = _adminEmail,
            ClinicId = _firstClinic, EmailConfirmed = true, LockoutEnabled = true
        };
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, "Administrator")).Succeeded);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private static object NewPatient(string number) => new
    {
        patientNumber = number,
        firstName = "Fictional",
        lastName = "Patient",
        dateOfBirth = "2001-01-22",
        phoneNumber = (string?)null,
        email = (string?)null
    };

    private async Task AuthenticateAsync()
    {
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = _adminEmail,
            password = Password
        });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [Fact]
    public async Task UnauthenticatedPatientRequestsAreRejected()
    {
        var response = await _client.GetAsync("/api/v1/patients");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RegisterSearchReadAndUpdatePatient()
    {
        await AuthenticateAsync();
        var number = "P-" + Guid.NewGuid().ToString("N")[..12];
        var created = await _client.PostAsJsonAsync("/api/v1/patients", NewPatient(number));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = createdBody.RootElement.GetProperty("id").GetGuid();
        var version = createdBody.RootElement.GetProperty("rowVersion").GetString();

        var found = await _client.GetAsync($"/api/v1/patients/{id}");
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var search = await _client.GetAsync($"/api/v1/patients?search={number}");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        using var searchBody = JsonDocument.Parse(await search.Content.ReadAsStringAsync());
        Assert.Equal(1, searchBody.RootElement.GetProperty("total").GetInt32());

        var changed = await _client.PutAsJsonAsync($"/api/v1/patients/{id}", new
        {
            firstName = "Updated",
            lastName = "Patient",
            dateOfBirth = "2001-01-22",
            phoneNumber = (string?)null,
            email = (string?)null,
            rowVersion = version
        });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        using var changedBody = JsonDocument.Parse(await changed.Content.ReadAsStringAsync());
        Assert.Equal("Updated", changedBody.RootElement.GetProperty("firstName").GetString());

        var stale = await _client.PutAsJsonAsync($"/api/v1/patients/{id}", new
        {
            firstName = "Stale",
            lastName = "Patient",
            dateOfBirth = "2001-01-22",
            phoneNumber = (string?)null,
            email = (string?)null,
            rowVersion = version
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CareFlowDbContext>();
        Assert.Equal(2, await db.AuditEvents.CountAsync(a => a.ClinicId == _firstClinic &&
            a.EntityType == "Patient" && a.EntityId == id.ToString()));
    }

    [Fact]
    public async Task DuplicatePatientNumberIsRejected()
    {
        await AuthenticateAsync();
        var number = "D-" + Guid.NewGuid().ToString("N")[..12];
        var first = await _client.PostAsJsonAsync("/api/v1/patients", NewPatient(number));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var duplicate = await _client.PostAsJsonAsync("/api/v1/patients", NewPatient(number));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task InvalidPasswordRecordsFailedLogin()
    {
        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = _adminEmail, password = "WrongPassword!12345"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);

        await using var scope = _app.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var staff = await users.FindByEmailAsync(_adminEmail);
        Assert.NotNull(staff);
        Assert.Equal(1, await users.GetAccessFailedCountAsync(staff));
    }

    [Fact]
    public async Task PractitionerCannotManagePatients()
    {
        var email = $"practitioner-{Guid.NewGuid():N}@example.test";
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                Email = email, UserName = email, ClinicId = _firstClinic,
                EmailConfirmed = true, LockoutEnabled = true
            };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Practitioner")).Succeeded);
        }

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("accessToken").GetString();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var result = await _client.GetAsync("/api/v1/patients");
        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
    }

    [Fact]
    public async Task PatientsFromOtherClinicsAreInvisible()
    {
        Guid foreignId = Guid.NewGuid();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CareFlowDbContext>();
            db.Patients.Add(new Patient
            {
                Id = foreignId, ClinicId = _secondClinic,
                PatientNumber = "X-" + Guid.NewGuid().ToString("N")[..12],
                FirstName = "Other", LastName = "Clinic",
                DateOfBirth = new DateOnly(2000, 1, 1)
            });
            await db.SaveChangesAsync();
        }
        await AuthenticateAsync();
        var read = await _client.GetAsync($"/api/v1/patients/{foreignId}");
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        var search = await _client.GetAsync("/api/v1/patients?search=Other");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        using var body = JsonDocument.Parse(await search.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("total").GetInt32());
    }
    [Fact]
    public async Task UnconfirmedStaffCannotLogIn()
    {
        var email = $"unconfirmed-{Guid.NewGuid():N}@example.test";
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser
            {
                ClinicId = _firstClinic, UserName = email, Email = email,
                EmailConfirmed = false, LockoutEnabled = true
            };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Administrator")).Succeeded);
        }
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email, password = Password
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SensitiveApiResponsesDisallowCaching()
    {
        await AuthenticateAsync();
        var response = await _client.GetAsync("/api/v1/patients");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "",
            StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Headers.TryGetValues("X-Content-Type-Options", out var values));
        Assert.Contains("nosniff", values!);
    }

}
