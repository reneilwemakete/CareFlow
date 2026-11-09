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

public sealed class AppointmentApiTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _app = new();
    private HttpClient _client = null!;
    private Guid _clinicId;
    private Guid _patientId;
    private Guid _practitionerId;
    private string _email = null!;
    private const string Password = "AppointmentTest!12345";

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
        if (!await roles.RoleExistsAsync("Administrator"))
            Assert.True((await roles.CreateAsync(new IdentityRole("Administrator"))).Succeeded);

        _clinicId = Guid.NewGuid();
        _patientId = Guid.NewGuid();
        _practitionerId = Guid.NewGuid();
        db.Clinics.Add(new Clinic { Id = _clinicId, Name = "Fictional Appointments Clinic",
            Code = "AP" + Guid.NewGuid().ToString("N")[..12] });
        db.Patients.Add(new Patient
        {
            Id = _patientId, ClinicId = _clinicId, PatientNumber = "A" + Guid.NewGuid().ToString("N")[..10],
            FirstName = "Example", LastName = "Patient", DateOfBirth = new DateOnly(2000, 1, 1)
        });
        db.Practitioners.Add(new Practitioner
        {
            Id = _practitionerId, ClinicId = _clinicId,
            FirstName = "Example", LastName = "Practitioner"
        });
        await db.SaveChangesAsync();

        _email = $"appointment-{Guid.NewGuid():N}@example.test";
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = new ApplicationUser
        {
            ClinicId = _clinicId, UserName = _email, Email = _email,
            EmailConfirmed = true, LockoutEnabled = true
        };
        Assert.True((await users.CreateAsync(admin, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(admin, "Administrator")).Succeeded);

        var login = await _client.PostAsJsonAsync("/api/v1/auth/login", new { email = _email, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var json = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        var token = json.RootElement.GetProperty("accessToken").GetString();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private object Booking(DateTime start, DateTime end) => new
    {
        patientId = _patientId, practitionerId = _practitionerId,
        startUtc = start, endUtc = end, reasonForVisit = "Fictional consultation"
    };

    private async Task AllowAsync(DateTime start, DateTime end, bool isAvailable = true)
    {
        var result = await _client.PostAsJsonAsync("/api/v1/availability", new
        {
            practitionerId = _practitionerId, startUtc = start, endUtc = end, isAvailable
        });
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
    }

    [Fact]
    public async Task BookingRequiresAvailabilityAndPreventsOverlaps()
    {
        var start = DateTime.UtcNow.AddDays(10);
        var end = start.AddMinutes(30);
        var withoutAvailability = await _client.PostAsJsonAsync("/api/v1/appointments", Booking(start, end));
        Assert.Equal(HttpStatusCode.Conflict, withoutAvailability.StatusCode);

        await AllowAsync(start.AddHours(-1), end.AddHours(1));
        var created = await _client.PostAsJsonAsync("/api/v1/appointments", Booking(start, end));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var json = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("id").GetGuid();

        var overlap = await _client.PostAsJsonAsync("/api/v1/appointments",
            Booking(start.AddMinutes(10), end.AddMinutes(10)));
        Assert.Equal(HttpStatusCode.Conflict, overlap.StatusCode);

        var read = await _client.GetAsync($"/api/v1/appointments/{id}");
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        var cancelled = await _client.PostAsync($"/api/v1/appointments/{id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);

        var afterCancel = await _client.PostAsJsonAsync("/api/v1/appointments", Booking(start, end));
        Assert.Equal(HttpStatusCode.Created, afterCancel.StatusCode);

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CareFlowDbContext>();
        Assert.Equal(3, await db.AuditEvents.CountAsync(a => a.ClinicId == _clinicId &&
            a.EntityType == "Appointment"));
    }

    [Fact]
    public async Task BlockedAvailabilityPreventsBooking()
    {
        var start = DateTime.UtcNow.AddDays(11);
        var end = start.AddMinutes(30);
        await AllowAsync(start.AddHours(-1), end.AddHours(1));
        await AllowAsync(start.AddMinutes(10), end.AddMinutes(10), false);
        var response = await _client.PostAsJsonAsync("/api/v1/appointments", Booking(start, end));
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ConcurrentOverlappingBookingsCannotBothSucceed()
    {
        var start = DateTime.UtcNow.AddDays(12);
        var end = start.AddMinutes(45);
        await AllowAsync(start.AddHours(-1), end.AddHours(1));
        var requests = await Task.WhenAll(
            _client.PostAsJsonAsync("/api/v1/appointments", Booking(start, end)),
            _client.PostAsJsonAsync("/api/v1/appointments", Booking(start.AddMinutes(15), end.AddMinutes(15))));
        Assert.Equal(1, requests.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, requests.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CareFlowDbContext>();
        Assert.Equal(1, await db.Appointments.CountAsync(a =>
            a.ClinicId == _clinicId && a.PractitionerId == _practitionerId));
    }

    [Fact]
    public async Task OtherClinicPractitionerCannotBeBooked()
    {
        var start = DateTime.UtcNow.AddDays(13);
        var end = start.AddMinutes(30);
        var foreignPractitionerId = Guid.NewGuid();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CareFlowDbContext>();
            var foreignClinicId = Guid.NewGuid();
            db.Clinics.Add(new Clinic { Id = foreignClinicId,
                Code = "Z" + Guid.NewGuid().ToString("N")[..12], Name = "Foreign Fictional Clinic" });
            db.Practitioners.Add(new Practitioner { Id = foreignPractitionerId,
                ClinicId = foreignClinicId, FirstName = "Foreign", LastName = "Clinician" });
            await db.SaveChangesAsync();
        }
        var response = await _client.PostAsJsonAsync("/api/v1/appointments", new
        {
            patientId = _patientId, practitionerId = foreignPractitionerId,
            startUtc = start, endUtc = end
        });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
    [Fact]
    public async Task RescheduleChecksAvailabilityAndRejectsStaleRowVersion()
    {
        var start = DateTime.UtcNow.AddDays(16);
        await AllowAsync(start.AddHours(-1), start.AddHours(4));
        var created = await _client.PostAsJsonAsync("/api/v1/appointments",
            Booking(start, start.AddMinutes(30)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var body = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var id = body.RootElement.GetProperty("id").GetGuid();
        var version = body.RootElement.GetProperty("rowVersion").GetString();

        var moved = await _client.PostAsJsonAsync($"/api/v1/appointments/{id}/reschedule", new
        {
            startUtc = start.AddHours(2), endUtc = start.AddHours(2).AddMinutes(30),
            practitionerId = _practitionerId, rowVersion = version
        });
        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        using var newBody = JsonDocument.Parse(await moved.Content.ReadAsStringAsync());
        Assert.NotEqual(version, newBody.RootElement.GetProperty("rowVersion").GetString());

        var stale = await _client.PostAsJsonAsync($"/api/v1/appointments/{id}/reschedule", new
        {
            startUtc = start.AddHours(1), endUtc = start.AddHours(1).AddMinutes(30),
            practitionerId = _practitionerId, rowVersion = version
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        var oldSlot = await _client.PostAsJsonAsync("/api/v1/appointments",
            Booking(start, start.AddMinutes(30)));
        Assert.Equal(HttpStatusCode.Created, oldSlot.StatusCode);
    }

    [Fact]
    public async Task ClinicScheduleFiltersByPractitionerAndIncludesPersistedCheckIn()
    {
        var from = DateTime.UtcNow.Date.AddDays(25);
        var end = from.AddMinutes(45);
        var other = Guid.NewGuid();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CareFlowDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var staff = await users.FindByEmailAsync(_email);
            Assert.NotNull(staff);
            db.Practitioners.Add(new Practitioner
            {
                Id = other, ClinicId = _clinicId,
                FirstName = "Second", LastName = "Clinician"
            });
            db.Appointments.AddRange(
                new Appointment
                {
                    ClinicId = _clinicId, PatientId = _patientId,
                    PractitionerId = _practitionerId, CreatedByUserId = staff.Id,
                    StartUtc = from, EndUtc = end, CheckedInAtUtc = from.AddMinutes(3)
                },
                new Appointment
                {
                    ClinicId = _clinicId, PatientId = _patientId,
                    PractitionerId = other, CreatedByUserId = staff.Id,
                    StartUtc = from, EndUtc = end
                });
            await db.SaveChangesAsync();
        }

        var query = $"/api/v1/schedule?fromUtc={Uri.EscapeDataString(from.ToString("O"))}" +
            $"&toUtc={Uri.EscapeDataString(from.AddDays(1).ToString("O"))}" +
            $"&practitionerId={_practitionerId}";
        var response = await _client.GetAsync(query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = json.RootElement.GetProperty("items");
        Assert.Single(items.EnumerateArray());
        Assert.True(items[0].GetProperty("checkedInAtUtc").ValueKind != JsonValueKind.Null);

        var unauthorized = await _app.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        }).GetAsync(query);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
    }

    [Fact]
    public async Task CheckInAndCompletionEnforceLifecycleAndAuditHistory()
    {
        var id = Guid.NewGuid();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CareFlowDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var staff = await users.FindByEmailAsync(_email);
            Assert.NotNull(staff);
            db.Appointments.Add(new Appointment
            {
                Id = id, ClinicId = _clinicId, PatientId = _patientId,
                PractitionerId = _practitionerId, CreatedByUserId = staff.Id,
                StartUtc = DateTime.UtcNow.AddMinutes(-5),
                EndUtc = DateTime.UtcNow.AddMinutes(25)
            });
            await db.SaveChangesAsync();
        }

        Assert.Equal(HttpStatusCode.Conflict,
            (await _client.PostAsync($"/api/v1/appointments/{id}/complete", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsync($"/api/v1/appointments/{id}/check-in", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await _client.PostAsync($"/api/v1/appointments/{id}/check-in", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await _client.PostAsync($"/api/v1/appointments/{id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await _client.PostAsync($"/api/v1/appointments/{id}/complete", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await _client.PostAsync($"/api/v1/appointments/{id}/complete", null)).StatusCode);

        await using var verifyScope = _app.Services.CreateAsyncScope();
        var database = verifyScope.ServiceProvider.GetRequiredService<CareFlowDbContext>();
        var stored = await database.Appointments.AsNoTracking().SingleAsync(a => a.Id == id);
        Assert.Equal(AppointmentStatus.Completed, stored.Status);
        Assert.NotNull(stored.CheckedInAtUtc);
        Assert.Equal(1, await database.AuditEvents.CountAsync(e => e.ClinicId == _clinicId &&
            e.EntityType == "Appointment" && e.EntityId == id.ToString() &&
            e.Action == "AppointmentCheckedIn"));
        Assert.Equal(1, await database.AuditEvents.CountAsync(e => e.ClinicId == _clinicId &&
            e.EntityType == "Appointment" && e.EntityId == id.ToString() &&
            e.Action == "AppointmentCompleted"));
    }

    [Fact]
    public async Task PatientCannotBeDoubleBookedAcrossDifferentPractitioners()
    {
        var start = DateTime.UtcNow.AddDays(20);
        var end = start.AddMinutes(30);
        var secondPractitioner = Guid.NewGuid();
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CareFlowDbContext>();
            db.Practitioners.Add(new Practitioner
            {
                Id = secondPractitioner, ClinicId = _clinicId,
                FirstName = "Another", LastName = "Practitioner"
            });
            db.PractitionerAvailabilities.Add(new PractitionerAvailability
            {
                ClinicId = _clinicId, PractitionerId = secondPractitioner,
                StartUtc = start.AddHours(-1), EndUtc = end.AddHours(1),
                IsAvailable = true
            });
            await db.SaveChangesAsync();
        }
        await AllowAsync(start.AddHours(-1), end.AddHours(1));
        Assert.Equal(HttpStatusCode.Created,
            (await _client.PostAsJsonAsync("/api/v1/appointments", Booking(start, end))).StatusCode);

        var conflicting = await _client.PostAsJsonAsync("/api/v1/appointments", new
        {
            patientId = _patientId, practitionerId = secondPractitioner,
            startUtc = start.AddMinutes(5), endUtc = end.AddMinutes(5),
            reasonForVisit = "Fictional appointment conflict test"
        });
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
    }

}
