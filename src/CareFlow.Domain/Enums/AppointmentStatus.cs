namespace CareFlow.Domain.Enums;

public enum AppointmentStatus : byte
{
    Scheduled = 1,
    Completed = 2,
    Cancelled = 3,
    NoShow = 4
}
