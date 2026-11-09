namespace CareFlow.Domain.Entities;

public sealed class Clinic
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string TimeZoneId { get; set; } = "Africa/Johannesburg";
    public bool IsActive { get; set; } = true;
}
