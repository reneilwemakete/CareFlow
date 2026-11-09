using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CareFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersistAppointmentCheckIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CheckedInAtUtc",
                table: "Appointments",
                type: "datetime2",
                nullable: true);

            // Preserve check-ins recorded as audit events before the dedicated field existed.
            migrationBuilder.Sql(@"
                UPDATE a
                SET a.CheckedInAtUtc = history.FirstCheckedInUtc
                FROM dbo.Appointments AS a
                INNER JOIN (
                    SELECT ClinicId, EntityId, MIN(OccurredAtUtc) AS FirstCheckedInUtc
                    FROM dbo.AuditEvents
                    WHERE EntityType = N'Appointment' AND Action = N'AppointmentCheckedIn'
                    GROUP BY ClinicId, EntityId
                ) AS history
                    ON history.ClinicId = a.ClinicId
                    AND history.EntityId = CONVERT(nvarchar(36), a.Id)
                WHERE a.CheckedInAtUtc IS NULL;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CheckedInAtUtc",
                table: "Appointments");
        }
    }
}
