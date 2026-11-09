using CareFlow.Domain.Rules;
using Xunit;

namespace CareFlow.UnitTests;

public sealed class AppointmentRulesTests
{
    private static readonly DateTime Base = new(2026, 11, 18, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void OverlappingAppointmentsReturnTrue() =>
        Assert.True(AppointmentRules.Overlaps(Base, Base.AddMinutes(60), Base.AddMinutes(30), Base.AddMinutes(90)));

    [Fact]
    public void AdjacentAppointmentsDoNotOverlap() =>
        Assert.False(AppointmentRules.Overlaps(Base, Base.AddMinutes(30), Base.AddMinutes(30), Base.AddMinutes(60)));

    [Fact]
    public void InvalidIntervalIsRejected() =>
        Assert.Throws<ArgumentException>(() => AppointmentRules.ValidateInterval(Base, Base));

    [Fact]
    public void LocalTimeIsRejected() =>
        Assert.Throws<ArgumentException>(() => AppointmentRules.ValidateInterval(DateTime.Now, DateTime.Now.AddMinutes(30)));
}
