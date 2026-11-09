namespace CareFlow.Domain.Entities;

public sealed class PractitionerAvailability
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClinicId { get; set; }
    public Guid PractitionerId { get; set; }
    public Practitioner Practitioner { get; set; } = null!;
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public bool IsAvailable { get; set; } = true;
}
