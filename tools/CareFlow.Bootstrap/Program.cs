using CareFlow.Domain.Entities;
using CareFlow.Infrastructure.Identity;
using CareFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

if (args.Length != 1 || args[0] != "--confirm-initial-admin")
{
    Console.Error.WriteLine("Usage: dotnet run --project tools/CareFlow.Bootstrap -- --confirm-initial-admin");
    return 2;
}

static string Required(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException($"Missing environment variable: {name}");
    return value.Trim();
}

try
{
    var connection = Required("ConnectionStrings__CareFlowDb");
    var clinicCode = Required("CAREFLOW_BOOTSTRAP_CLINIC_CODE");
    var clinicName = Required("CAREFLOW_BOOTSTRAP_CLINIC_NAME");
    var email = Required("CAREFLOW_BOOTSTRAP_EMAIL");
    var password = Environment.GetEnvironmentVariable("CAREFLOW_BOOTSTRAP_PASSWORD")
        ?? throw new InvalidOperationException("Missing CAREFLOW_BOOTSTRAP_PASSWORD");

    if (clinicCode.Length > 20 || clinicName.Length > 150 ||
        email.Length > 256 || !System.Net.Mail.MailAddress.TryCreate(email, out _))
        throw new ArgumentException("Invalid clinic code, clinic name or email.");
    if (password.Length < 12)
        throw new ArgumentException("Administrator password must contain at least 12 characters.");

    var services = new ServiceCollection();
    services.AddLogging();
    services.AddDbContext<CareFlowDbContext>(o => o.UseSqlServer(connection));
    services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.Password.RequiredLength = 12;
            options.User.RequireUniqueEmail = true;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            options.Lockout.MaxFailedAccessAttempts = 5;
        })
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<CareFlowDbContext>();

    await using var provider = services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<CareFlowDbContext>();
    var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    await db.Database.MigrateAsync();
    if (await db.Clinics.AnyAsync(c => c.Code == clinicCode))
        throw new InvalidOperationException("Clinic already exists. This command cannot grant admin access to an existing clinic.");
    if (await users.FindByEmailAsync(email) is not null)
        throw new InvalidOperationException("Staff email already exists.");

    await using var transaction = await db.Database.BeginTransactionAsync();

    var clinic = new Clinic { Code = clinicCode, Name = clinicName };
    db.Clinics.Add(clinic);
    await db.SaveChangesAsync();

    foreach (var name in new[] { "Administrator", "Receptionist", "Practitioner" })
    {
        if (!await roles.RoleExistsAsync(name))
        {
            var created = await roles.CreateAsync(new IdentityRole(name));
            if (!created.Succeeded)
                throw new InvalidOperationException("Could not create staff roles.");
        }
    }

    var staff = new ApplicationUser
    {
        ClinicId = clinic.Id,
        Email = email,
        UserName = email,
        EmailConfirmed = true,
        LockoutEnabled = true,
        IsActive = true
    };
    var result = await users.CreateAsync(staff, password);
    if (!result.Succeeded)
        throw new InvalidOperationException("Administrator validation failed: " +
            string.Join("; ", result.Errors.Select(e => e.Description)));

    result = await users.AddToRoleAsync(staff, "Administrator");
    if (!result.Succeeded)
        throw new InvalidOperationException("Could not assign Administrator role.");

    await transaction.CommitAsync();
    Console.WriteLine($"Initial administrator provisioned for new clinic {clinicCode}.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Provisioning failed: " + ex.Message);
    return 1;
}
