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

public sealed class ReportingApiTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _app = new();
    private HttpClient _client = null!;
    private Guid _clinicId;
    private Guid _practitionerId;
    private string _email = null!;
    private const string Password = "ReportingDemo!12345";

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
        _practitionerId = Guid.NewGuid();
        var patientId = Guid.NewGuid();
        var foreignClinic = Guid.NewGuid();
        var foreignDoctor = Guid.NewGuid();
        var foreignPatient = Guid.NewGuid();
        db.Clinics.AddRange(
            new Clinic { Id = _clinicId, Name = "Fictional Reporting Clinic",
                Code = "R" + Guid.NewGuid().ToString("N")[..12] },
            new Clinic { Id = foreignClinic, Name = "Fictional Other Clinic",
                Code = "O" + Guid.NewGuid().ToString("N")[..12] });
        db.Practitioners.AddRange(
            new Practitioner { Id = _practitionerId, ClinicId = _clinicId,
                FirstName = "Example", LastName = "Clinician" },
            new Practitioner { Id = foreignDoctor, ClinicId = foreignClinic,
                FirstName = "Foreign", LastName = "Doctor" });
        db.Patients.AddRange(
            new Patient { Id = patientId, ClinicId = _clinicId,
                PatientNumber = "P" + Guid.NewGuid().ToString("N")[..12],
                FirstName = "Test", LastName = "Patient",
                DateOfBirth = new DateOnly(2000, 1, 1) },
            new Patient { Id = foreignPatient, ClinicId = foreignClinic,
                PatientNumber = "F" + Guid.NewGuid().ToString("N")[..12],
                FirstName = "Other", LastName = "Patient",
                DateOfBirth = new DateOnly(2000, 1, 1) });
        await db.SaveChangesAsync();

        _email = $"report-{Guid.NewGuid():N}@example.test";
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = new ApplicationUser
        {
            ClinicId = _clinicId, Email = _email, UserName = _email,
            EmailConfirmed = true, LockoutEnabled = true
        };
        Assert.True((await users.CreateAsync(admin, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(admin, "Administrator")).Succeeded);

        var first = new DateTime(2026, 12, 2, 8, 0, 0, DateTimeKind.Utc);
        db.Appointments.AddRange(
            new Appointment { ClinicId = _clinicId, PatientId = patientId,
                PractitionerId = _practitionerId, CreatedByUserId = admin.Id,
                StartUtc = first, EndUtc = first.AddMinutes(30),
                CheckedInAtUtc = first.AddMinutes(-5), Status = AppointmentStatus.Completed },
            new Appointment { ClinicId = _clinicId, PatientId = patientId,
                PractitionerId = _practitionerId, CreatedByUserId = admin.Id,
                StartUtc = first.AddHours(1), EndUtc = first.AddHours(1.5),
                Status = AppointmentStatus.NoShow },
            new Appointment { ClinicId = _clinicId, PatientId = patientId,
                PractitionerId = _practitionerId, CreatedByUserId = admin.Id,
                StartUtc = first.AddHours(2), EndUtc = first.AddHours(2.5),
                Status = AppointmentStatus.Cancelled },
            new Appointment { ClinicId = foreignClinic, PatientId = foreignPatient,
                PractitionerId = foreignDoctor, CreatedByUserId = admin.Id,
                StartUtc = first, EndUtc = first.AddMinutes(30),
                Status = AppointmentStatus.Scheduled });
        await db.SaveChangesAsync();
        await LoginAsync(_email);
    }

    private async Task LoginAsync(string email)
    {
        var result = await _client.PostAsJsonAsync("/api/v1/auth/login",
            new { email, password = Password });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        using var json = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", json.RootElement.GetProperty("accessToken").GetString());
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private static string Url =>
        "/api/v1/reports/appointments?fromUtc=2026-12-02T00%3A00%3A00Z&toUtc=2026-12-03T00%3A00%3A00Z";

    [Fact]
    public async Task ReportAggregatesOnlyOwnClinicWithoutPatientDetails()
    {
        var response = await _client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(payload);
        var root = json.RootElement;
        Assert.Equal(3, root.GetProperty("total").GetInt32());
        Assert.Equal(1, root.GetProperty("completed").GetInt32());
        Assert.Equal(1, root.GetProperty("noShow").GetInt32());
        Assert.Equal(1, root.GetProperty("cancelled").GetInt32());
        Assert.Equal(1, root.GetProperty("checkedIn").GetInt32());
        Assert.Equal(50m, root.GetProperty("attendanceRatePercent").GetDecimal());
        Assert.Equal(_practitionerId, root.GetProperty("practitioners")[0]
            .GetProperty("practitionerId").GetGuid());
        Assert.DoesNotContain("patientNumber", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("patientId", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReceptionistCannotViewAdministratorReport()
    {
        var email = $"restricted-{Guid.NewGuid():N}@example.test";
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var staff = new ApplicationUser { ClinicId = _clinicId, Email = email,
                UserName = email, EmailConfirmed = true, LockoutEnabled = true };
            Assert.True((await users.CreateAsync(staff, Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(staff, "Receptionist")).Succeeded);
        }
        await LoginAsync(email);
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.GetAsync(Url)).StatusCode);
    }

    [Fact]
    public async Task RejectsInvalidAndExcessiveReportingWindows()
    {
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.GetAsync("/api/v1/reports/appointments?fromUtc=2026-01-01T00%3A00%3A00Z&toUtc=2026-06-01T00%3A00%3A00Z")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await _client.GetAsync("/api/v1/reports/appointments?fromUtc=2026-12-03T00%3A00%3A00Z&toUtc=2026-12-02T00%3A00%3A00Z")).StatusCode);
    }
}
