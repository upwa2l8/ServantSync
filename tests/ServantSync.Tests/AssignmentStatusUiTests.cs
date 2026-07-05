using ServantSync.Components.Shared;
using ServantSync.Models;
using Xunit;

namespace ServantSync.Tests;

public class AssignmentStatusUiTests
{
    [Theory]
    [InlineData(AssignmentStatus.Scheduled, "primary")]
    [InlineData(AssignmentStatus.Tentative, "warning")]
    [InlineData(AssignmentStatus.Completed, "success")]
    [InlineData(AssignmentStatus.Cancelled, "secondary")]
    [InlineData(AssignmentStatus.NoShow,    "danger")]
    public void ColorFor_ReturnsExpectedBootstrapClass(AssignmentStatus status, string expected)
    {
        Assert.Equal(expected, AssignmentStatusUi.ColorFor(status));
    }

    [Fact]
    public void ColorFor_AlwaysReturnsNonEmptyString_ForEveryDefinedStatus()
    {
        foreach (var s in Enum.GetValues<AssignmentStatus>())
        {
            var color = AssignmentStatusUi.ColorFor(s);
            Assert.False(string.IsNullOrWhiteSpace(color), $"Color for {s} was null/empty.");
        }
    }

    [Fact]
    public void ColorFor_ForUnknownEnumValue_FallsBackToSecondary()
    {
        var color = AssignmentStatusUi.ColorFor((AssignmentStatus)9999);
        Assert.Equal("secondary", color);
    }
}
