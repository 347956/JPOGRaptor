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
    public void ChasesAfterFlee_LoudFleeChases_SilentFleeSearches()
    {
        Assert.True(RaptorRules.ChasesAfterFlee(false));
        Assert.False(RaptorRules.ChasesAfterFlee(true));
    }

    [Fact]
    public void PursuedTime_GrowsWhilePlayerIsClose()
    {
        float t = 0f;
        for (int i = 0; i < 5; i++) t = RaptorRules.UpdatePursuedTime(t, true, 1f);
        Assert.Equal(5f, t);
    }

    [Fact]
    public void PursuedTime_DecaysAndNeverGoesNegative()
    {
        Assert.Equal(1f, RaptorRules.UpdatePursuedTime(3f, false, 2f));
        Assert.Equal(0f, RaptorRules.UpdatePursuedTime(1f, false, 2f));
    }
}
