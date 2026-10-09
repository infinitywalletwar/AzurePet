using InPolsure.SharedKernel;

namespace InPolsure.UnitTests.SharedKernel;

public sealed class SystemClockTests
{
    [Fact]
    public void UtcNow_read_returns_time_with_zero_offset()
    {
        var clock = new SystemClock();

        var now = clock.UtcNow;

        Assert.Equal(TimeSpan.Zero, now.Offset);
    }

    [Fact]
    public void UtcNow_read_returns_current_system_time()
    {
        var clock = new SystemClock();

        var before = DateTimeOffset.UtcNow;
        var now = clock.UtcNow;
        var after = DateTimeOffset.UtcNow;

        Assert.InRange(now, before, after);
    }
}
