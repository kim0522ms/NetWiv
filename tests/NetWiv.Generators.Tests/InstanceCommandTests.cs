using Xunit;

namespace NetWiv.Generators.Tests;

public sealed class InstanceCommandTests
{
    [Fact]
    public Task NativeCallbackDoesNotCaptureCallerContext() => NetWiv.Tests.InstanceCommandScenarios.NativeCallbackDoesNotCaptureCallerContext();

    [Fact]
    public Task PluginInstanceCommandsKeepTheirScopeAndPermissions() => NetWiv.Tests.InstanceCommandScenarios.PluginInstanceCommandsKeepTheirScopeAndPermissions();

    [Fact]
    public Task Lifetimes() => NetWiv.Tests.InstanceCommandScenarios.Lifetimes();

    [Fact]
    public Task ConstructorAndParameterShareScope() => NetWiv.Tests.InstanceCommandScenarios.ConstructorAndParameterShareScope();

    [Fact]
    public Task ConcurrentAwaitAndDisposal() => NetWiv.Tests.InstanceCommandScenarios.ConcurrentAwaitAndDisposal();

    [Fact]
    public Task FailureAndCancellation() => NetWiv.Tests.InstanceCommandScenarios.FailureAndCancellation();

    [Fact]
    public Task MissingRegistrationAndConstructorFailure() => NetWiv.Tests.InstanceCommandScenarios.MissingRegistrationAndConstructorFailure();

    [Fact]
    public Task GuestIpcAndAuthorization() => NetWiv.Tests.InstanceCommandScenarios.GuestIpcAndAuthorization();

}
