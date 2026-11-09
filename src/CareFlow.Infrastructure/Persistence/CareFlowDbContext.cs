using CareFlow.Domain.Entities;
using CareFlow.Domain.Enums;
using CareFlow.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CareFlow.Infrastructure.Persistence;

public sealed class CareFlowDbContext(DbContextOptions<CareFlowDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Clinic> Clinics => Set<Clinic>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Practitioner> Practitioners => Set<Practitioner>();
    public DbSet<PractitionerAvailability> PractitionerAvailabilities => Set<PractitionerAvailability>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(x => x.ClinicId).IsRequired();
            entity.HasOne<Clinic>().WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.ClinicId);
        });

        builder.Entity<Clinic>(entity =>
        {
            entity.ToTable("Clinics");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Code).HasMaxLength(20).IsRequired();
            entity.Property(x => x.TimeZoneId).HasMaxLength(100).IsRequired();
            entity.HasIndex(x => x.Code).IsUnique();
        });

        builder.Entity<Patient>(entity =>
        {
            entity.ToTable("Patients");
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.ClinicId, x.Id });
            entity.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.PatientNumber).HasMaxLength(30).IsRequired();
            entity.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.LastName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.PhoneNumber).HasMaxLength(25);
            entity.Property(x => x.Email).HasMaxLength(254);
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => new { x.ClinicId, x.PatientNumber }).IsUnique();
        });

        builder.Entity<Practitioner>(entity =>
        {
            entity.ToTable("Practitioners");
            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.ClinicId, x.Id });
            entity.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.FirstName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.LastName).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Specialty).HasMaxLength(100);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.UserId).IsUnique().HasFilter("[UserId] IS NOT NULL");
        });

        builder.Entity<PractitionerAvailability>(entity =>
        {
            entity.ToTable("PractitionerAvailability");
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.Practitioner).WithMany()
                .HasForeignKey(x => new { x.ClinicId, x.PractitionerId })
                .HasPrincipalKey(x => new { x.ClinicId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.ClinicId, x.PractitionerId, x.StartUtc });
            entity.ToTable(t => t.HasCheckConstraint("CK_Availability_ValidInterval", "[StartUtc] < [EndUtc]"));
        });

        builder.Entity<Appointment>(entity =>
        {
            entity.ToTable("Appointments");
            entity.HasKey(x => x.Id);
            entity.HasOne(x => x.Patient).WithMany()
                .HasForeignKey(x => new { x.ClinicId, x.PatientId })
                .HasPrincipalKey(x => new { x.ClinicId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Practitioner).WithMany()
                .HasForeignKey(x => new { x.ClinicId, x.PractitionerId })
                .HasPrincipalKey(x => new { x.ClinicId, x.Id })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.ReasonForVisit).HasMaxLength(250);
            entity.Property(x => x.Status).HasConversion<byte>();
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => new { x.ClinicId, x.PractitionerId, x.StartUtc });
            entity.HasIndex(x => new { x.ClinicId, x.PatientId, x.StartUtc });
            entity.ToTable(t => t.HasCheckConstraint("CK_Appointment_ValidInterval", "[StartUtc] < [EndUtc]"));
            entity.ToTable(t => t.HasCheckConstraint("CK_Appointment_ValidStatus", "[Status] IN (1, 2, 3, 4)"));
        });

        builder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("AuditEvents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.HasOne(x => x.Clinic).WithMany().HasForeignKey(x => x.ClinicId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
            entity.Property(x => x.Action).HasMaxLength(80).IsRequired();
            entity.Property(x => x.EntityType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.EntityId).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(100);
            entity.HasIndex(x => new { x.ClinicId, x.OccurredAtUtc });
        });
    }
}
