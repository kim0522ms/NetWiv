using NetWiv.BlazorHybrid;
using NetWiv.BlazorHybrid.Aot;

internal static class FrameworkTests
{
    internal static Task MatchingTarget()
    {
#if NET10_0_OR_GREATER
        const string expected = "10.0.13";
#elif NET9_0_OR_GREATER
        const string expected = "9.0.21";
#else
        const string expected = "8.0.32";
#endif
        Check.Equal(expected, BlazorFrameworkVersion.Expected);
        BlazorWebViewManager.EnsureSupportedFrameworkVersion();
        AotFrameworkVersion.EnsureSupported();
        Check.Throws<NotSupportedException>(() => BlazorFrameworkVersion.Check(typeof(FrameworkTests).Assembly));
        return Task.CompletedTask;
    }
}
