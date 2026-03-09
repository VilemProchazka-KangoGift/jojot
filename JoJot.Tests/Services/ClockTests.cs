using JoJot.Services;
using JoJot.Tests.Helpers;

namespace JoJot.Tests.Services;

public class ClockTests
{
    // ─── SystemClock ──────────────────────────────────────────────────

    [Fact]
    public void SystemClock_Now_ReturnsCurrentTime()
    {
        var before = DateTime.Now;
        var now = SystemClock.Instance.Now;
        var after = DateTime.Now;

        now.Should().BeOnOrAfter(before);
        now.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void SystemClock_UtcNow_ReturnsUtcTime()
    {
        var before = DateTime.UtcNow;
        var utcNow = SystemClock.Instance.UtcNow;
        var after = DateTime.UtcNow;

        utcNow.Should().BeOnOrAfter(before);
        utcNow.Should().BeOnOrBefore(after);
    }

    [Fact]
    public void SystemClock_Instance_IsSingleton()
    {
        SystemClock.Instance.Should().BeSameAs(SystemClock.Instance);
    }

    // ─── TestClock ────────────────────────────────────────────────────

    [Fact]
    public void TestClock_DefaultValues()
    {
        var clock = new TestClock();

        clock.Now.Should().Be(new DateTime(2025, 6, 15, 10, 30, 0));
        clock.UtcNow.Should().Be(new DateTime(2025, 6, 15, 14, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void TestClock_Advance_MovesBothTimes()
    {
        var clock = new TestClock();
        var originalNow = clock.Now;
        var originalUtc = clock.UtcNow;

        clock.Advance(TimeSpan.FromHours(2));

        clock.Now.Should().Be(originalNow + TimeSpan.FromHours(2));
        clock.UtcNow.Should().Be(originalUtc + TimeSpan.FromHours(2));
    }

    [Fact]
    public void TestClock_SetDirectly()
    {
        var clock = new TestClock();
        var custom = new DateTime(2030, 1, 1, 0, 0, 0);

        clock.Now = custom;
        clock.Now.Should().Be(custom);
    }

    [Fact]
    public void TestClock_MultipleAdvances()
    {
        var clock = new TestClock();
        var start = clock.Now;

        clock.Advance(TimeSpan.FromMinutes(10));
        clock.Advance(TimeSpan.FromMinutes(20));

        clock.Now.Should().Be(start + TimeSpan.FromMinutes(30));
    }
}
