using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace NetWiv.BlazorHybrid.Aot;

internal static class AotFrameworkVersion
{
    // WebView's exact package version alone does not pin the implementation
    // assemblies when a consumer references a newer ASP.NET Core shared framework.
    internal static void EnsureSupported()
    {
        Check(typeof(ComponentBase).Assembly);
        Check(typeof(MouseEventArgs).Assembly);
        Check(typeof(IJSRuntime).Assembly);
    }

    private static void Check(Assembly assembly)
    {
        BlazorFrameworkVersion.Check(assembly);
    }
}
