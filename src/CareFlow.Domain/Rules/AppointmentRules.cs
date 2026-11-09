namespace CareFlow.Domain.Rules;

public static class AppointmentRules
{
    public static bool Overlaps(DateTime startA, DateTime endA, DateTime startB, DateTime endB)
    {
        ValidateInterval(startA, endA);
        ValidateInterval(startB, endB);
        return startA < endB && endA > startB;
    }

    public static void ValidateInterval(DateTime startUtc, DateTime endUtc)
    {
        if (startUtc.Kind != DateTimeKind.Utc || endUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Appointment times must be UTC.");
        if (startUtc >= endUtc)
            throw new ArgumentException("Appointment start must precede appointment end.");
    }
}
