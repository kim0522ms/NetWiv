using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.FileProviders;

namespace NetWiv.BlazorHybrid;

/// <summary>Configures a Blazor renderer for one NetWiv webview.</summary>
public sealed class BlazorWebViewOptions
{
    public BlazorWebViewOptions() : this(OperatingSystem.IsWindows() ? BlazorAppOrigin.Windows : BlazorAppOrigin.Native) { }

    internal BlazorWebViewOptions(Uri appBaseUri) => AppBaseUri = appBaseUri;

    /// <summary>The host HTML file. Relative paths are resolved from the application directory.</summary>
    public string HostPage { get; set; } = "wwwroot/index.html";

    /// <summary>The existing webview label that will host Razor components.</summary>
    public string WebviewLabel { get; set; } = "main";

    /// <summary>The browser-facing origin of the private NetWiv Blazor protocol on the current platform.</summary>
    public Uri AppBaseUri { get; }

    /// <summary>
    /// An optional static file provider. When supplied, HostPage is relative to its root.
    /// The caller retains ownership of this provider.
    /// </summary>
    public IFileProvider? FileProvider { get; set; }

    public BlazorRootComponentCollection RootComponents { get; } = new();

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(HostPage);
        ArgumentException.ThrowIfNullOrWhiteSpace(WebviewLabel);
        if (FileProvider is not null && (Path.IsPathRooted(HostPage) ||
            HostPage.Replace('\\', '/').Split('/').Any(segment => segment is ".." or ".")))
            throw new ArgumentException("HostPage must be a relative path within the supplied file provider.", nameof(HostPage));
        if (RootComponents.Count == 0)
            throw new InvalidOperationException("Register at least one Blazor root component.");
    }

    internal BlazorWebViewOptions Snapshot()
    {
        Validate();
        var copy = new BlazorWebViewOptions(AppBaseUri) { HostPage = HostPage, WebviewLabel = WebviewLabel, FileProvider = FileProvider };
        foreach (var component in RootComponents) copy.RootComponents.Add(component);
        return copy;
    }
}

/// <summary>The Razor components attached to host-page selectors.</summary>
public sealed class BlazorRootComponentCollection : IReadOnlyList<BlazorRootComponent>
{
    private readonly List<BlazorRootComponent> _components = [];
    public int Count => _components.Count;
    public BlazorRootComponent this[int index] => _components[index];

    public void Add<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(
        string selector, IReadOnlyDictionary<string, object?>? parameters = null)
        where TComponent : IComponent
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        if (_components.Any(component => component.Selector == selector))
            throw new ArgumentException($"A root component is already registered for selector '{selector}'.", nameof(selector));
        _components.Add(new(typeof(TComponent), selector,
            parameters is null ? new Dictionary<string, object?>() : new Dictionary<string, object?>(parameters)));
    }

    public IEnumerator<BlazorRootComponent> GetEnumerator() => _components.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    internal void Add(BlazorRootComponent component) =>
        _components.Add(component with { Parameters = new Dictionary<string, object?>(component.Parameters) });
}

public sealed record BlazorRootComponent(
    [property: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    [param: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type ComponentType,
    string Selector, IReadOnlyDictionary<string, object?> Parameters);
