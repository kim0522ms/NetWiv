using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.FileProviders;

namespace NetWiv.Blazor;

/// <summary>Configures a managed Blazor renderer for one NetWiv webview.</summary>
public sealed class BlazorWebViewOptions
{
    /// <summary>The host HTML file. Relative paths are resolved from the application directory.</summary>
    public string HostPage { get; set; } = "wwwroot/index.html";

    /// <summary>The existing webview label that will host Razor components.</summary>
    public string WebviewLabel { get; set; } = "main";

    /// <summary>The browser-facing Windows origin of the private NetWiv Blazor protocol.</summary>
    public Uri AppBaseUri { get; } = new("http://netwiv-blazor.localhost/");

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
        var copy = new BlazorWebViewOptions { HostPage = HostPage, WebviewLabel = WebviewLabel, FileProvider = FileProvider };
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

    public void Add<TComponent>(string selector, IReadOnlyDictionary<string, object?>? parameters = null)
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

public sealed record BlazorRootComponent(Type ComponentType, string Selector, IReadOnlyDictionary<string, object?> Parameters);
