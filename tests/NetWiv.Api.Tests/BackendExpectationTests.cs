using Xunit;

namespace NetWiv.Api.Tests;

public sealed class BackendExpectationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("x11")]
    [InlineData("wayland")]
    [InlineData("windows")]
    [InlineData("macos")]
    public void AcceptsKnownBackendOrNoExpectation(string? backend) =>
        Assert.Equal(backend, BackendExpectation.Parse(backend));

    [Theory]
    [InlineData("")]
    [InlineData("Wayland")]
    [InlineData("wayland,x11")]
    [InlineData("unknown")]
    public void RejectsInvalidExpectation(string backend) =>
        Assert.Throws<ArgumentException>(() => BackendExpectation.Parse(backend));

    [Theory]
    [InlineData("wayland", "x11")]
    [InlineData("x11", "wayland")]
    [InlineData("wayland", null)]
    public void FailsOnDifferentOrMissingRuntimeBackend(string expected, string? actual) =>
        Assert.Throws<InvalidOperationException>(() => BackendExpectation.Verify(expected, actual));

    [Theory]
    [InlineData("wayland", "wayland")]
    [InlineData("x11", "x11")]
    [InlineData(null, "wayland")]
    [InlineData(null, null)]
    public void AllowsMatchingBackendOrUnconstrainedExecution(string? expected, string? actual) =>
        BackendExpectation.Verify(expected, actual);
}
