using JPOGRaptor.src;
using Xunit;

namespace JPOGRaptor.Tests;

public class RaptorRulesTests
{
    [Fact]
    public void IsAlone_NoOtherPlayers_ReturnsTrue()
    {
        Assert.True(RaptorRules.IsAlone([], 10f));
    }

    [Fact]
    public void IsAlone_OnlyFarPlayers_ReturnsTrue()
    {
        Assert.True(RaptorRules.IsAlone([25f, 40f], 10f));
    }

    [Fact]
    public void IsAlone_PlayerInsideRadius_ReturnsFalse()
    {
        Assert.False(RaptorRules.IsAlone([25f, 9.9f], 10f));
    }

    [Fact]
    public void IsAlone_PlayerExactlyOnRadius_CountsAsAlone()
    {
        Assert.True(RaptorRules.IsAlone([10f], 10f));
    }

    [Theory]
    [InlineData(4f, 10f, true)]    // pack mate right next to the raptor
    [InlineData(9.99f, 10f, true)]
    [InlineData(10f, 10f, false)]  // boundary is exclusive
    [InlineData(39f, 40f, true)]   // flee-to-raptor search
    [InlineData(41f, 40f, false)]
    [InlineData(70f, 70f, false)]  // help call range
    public void IsWithinRadius_IsStrict(float distance, float radius, bool expected)
    {
        Assert.Equal(expected, RaptorRules.IsWithinRadius(distance, radius));
    }

    [Fact]
    public void StalkCooldown_NeverFled_IsNotOnCooldown()
    {
        Assert.False(RaptorRules.IsStalkOnCooldown(100f, float.NegativeInfinity, 5f));
    }

    [Theory]
    [InlineData(102f, true)]
    [InlineData(104.9f, true)]
    [InlineData(105f, false)]
    [InlineData(120f, false)]
    public void StalkCooldown_LastsFiveSecondsAfterFleeing(float now, bool expected)
    {
        Assert.Equal(expected, RaptorRules.IsStalkOnCooldown(now, 100f, 5f));
    }

    [Fact]
    public void Timer_GrowsWhileActive()
    {
        float t = 0f;
        for (int i = 0; i < 5; i++) t = RaptorRules.UpdateTimer(t, true, 1f);
        Assert.Equal(5f, t);
    }

    [Fact]
    public void Timer_DecaysAndNeverGoesNegative()
    {
        Assert.Equal(1f, RaptorRules.UpdateTimer(3f, false, 2f));
        Assert.Equal(0f, RaptorRules.UpdateTimer(1f, false, 2f));
    }

    [Fact]
    public void Timer_SeenGraceTime_FlickerDoesNotReachTwoSeconds()
    {
        // Seen 1 s, unseen 0.6 s, seen 1 s: 1.4 s accumulated, below the 2 s grace time
        float t = RaptorRules.UpdateTimer(0f, true, 1f);
        t = RaptorRules.UpdateTimer(t, false, 0.6f);
        t = RaptorRules.UpdateTimer(t, true, 1f);
        Assert.True(t < 2f);
    }

    [Theory]
    [InlineData(1f, 5f, false)]    // too early, even when hidden
    [InlineData(3f, 0.5f, false)]  // min time passed but not hidden long enough
    [InlineData(3f, 1f, true)]
    [InlineData(5f, 0f, false)]    // still in sight
    [InlineData(12f, 0f, true)]    // timeout
    public void IsHideComplete_NeedsMinTimeAndHiddenTimeOrTimeout(float sinceStart, float hidden, bool expected)
    {
        Assert.Equal(expected, RaptorRules.IsHideComplete(sinceStart, hidden, 3f, 1f, 12f));
    }
}