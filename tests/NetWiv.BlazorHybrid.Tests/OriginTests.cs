using NetWiv.BlazorHybrid;

internal static class OriginTests
{
    internal static Task AuthorityBoundaries()
    {
        var cases = new (Uri Origin, string Source, bool Allowed)[]
        {
            (BlazorAppOrigin.Windows, "HTTP://NETWIV-BLAZOR.LOCALHOST/route?x=y#fragment", true),
            (BlazorAppOrigin.Windows, "http://netwiv-blazor.localhost:80/", true),
            (BlazorAppOrigin.Windows, "http://netwiv-blazor.localhost:8080/", false),
            (BlazorAppOrigin.Windows, "https://netwiv-blazor.localhost/", false),
            (BlazorAppOrigin.Windows, "http://netwiv-blazor.localhost./", false),
            (BlazorAppOrigin.Windows, "http://netwiv-blazor.localhost.evil.invalid/", false),
            (BlazorAppOrigin.Windows, "http://user:password@netwiv-blazor.localhost/", false),
            (BlazorAppOrigin.Windows, "netwiv-blazor://localhost/", false),
            (BlazorAppOrigin.Native, "NETWIV-BLAZOR://LOCALHOST/route?x=y#fragment", true),
            (BlazorAppOrigin.Native, "netwiv-blazor://localhost//foreign.invalid/index.html", true),
            (BlazorAppOrigin.Native, "netwiv-blazor://localhost:80/", false),
            (BlazorAppOrigin.Native, "netwiv-blazor://localhost./", false),
            (BlazorAppOrigin.Native, "netwiv-blazor://localhost.evil.invalid/", false),
            (BlazorAppOrigin.Native, "netwiv-blazor://user:password@localhost/", false),
            (BlazorAppOrigin.Native, "netwiv-blazor://[::1]/", false),
            (BlazorAppOrigin.Native, "netwiv-blazor:localhost/index.html", false),
            (BlazorAppOrigin.Native, "other-scheme://localhost/", false),
            (BlazorAppOrigin.Native, "http://netwiv-blazor.localhost/", false),
            (BlazorAppOrigin.Native, "file:///index.html", false),
        };
        foreach (var (origin, source, allowed) in cases)
            Check.Equal(allowed, BlazorAppOrigin.Matches(new Uri(source), origin));
        Check.True(!BlazorAppOrigin.Matches(new Uri("index.html", UriKind.Relative), BlazorAppOrigin.Native),
            "A relative URI was accepted as an authenticated application source.");
        return Task.CompletedTask;
    }
}
