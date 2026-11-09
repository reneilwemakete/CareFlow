using Microsoft.AspNetCore.Identity;

namespace CareFlow.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public Guid ClinicId { get; set; }
    public bool IsActive { get; set; } = true;
}
