using System.Reflection;

namespace NetWiv.BlazorHybrid;

internal static class BlazorFrameworkVersion
{
    internal static string Expected { get; } = typeof(BlazorFrameworkVersion).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "NetWivBlazorFrameworkVersion").Value!;

    internal static void Check(Assembly assembly)
    {
        var actual = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0];
        if (actual != Expected)
            throw new NotSupportedException(
                $"This NetWiv.BlazorHybrid build requires {assembly.GetName().Name} {Expected}, but loaded {actual ?? "an unknown version"}. " +
                "Use the matching Blazor package graph for the application's target framework.");
    }
}
