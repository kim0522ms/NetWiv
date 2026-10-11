using System.Text.Json;
using NetWiv;

internal static class RemoteUrlPatternTests
{
    internal static Task Run()
    {
        var acl = new AclAuthorizer([new("allow-read", "read")],
            [new("remote", ["allow-read"], Windows: ["main"], Local: false, RemoteUrls: ["https://*/*"])],
            new HashSet<string>(), hasAppCommandManifest: true);
        if (acl.Authorize(new("read", "main", "main", new("https://example.com:8443/a"))).Allowed)
            throw new InvalidOperationException("A hostname wildcard must not admit a non-default port.");
        var scoped = new AclAuthorizer([new("allow-read", "read")],
            [new("remote", ["allow-read"], Windows: ["main"], Local: false,
                RemoteUrls: ["*://example.com/:id\\?role=admin#approved"])], new HashSet<string>(), hasAppCommandManifest: true);
        foreach (var (url, allowed) in new[]
        {
            ("https://example.com/a?role=admin#approved", true),
            ("http://example.com/a?role=admin#approved", true),
            ("https://example.com/a?role=user#approved", false),
            ("https://example.com/a?role=admin#other", false),
            ("https://example.com/a/b?role=admin#approved", false),
            ("https://example.com.evil/a?role=admin#approved", false),
        })
            if (scoped.Authorize(new("read", "main", "main", new(url))).Allowed != allowed)
                throw new InvalidOperationException($"Remote permission boundary mismatch for {url}.");
        try
        {
            _ = new AclAuthorizer([], [new("invalid", [], Windows: ["main"], RemoteUrls: ["/relative"])], new HashSet<string>(), false);
            throw new InvalidOperationException("An invalid remote pattern must fail application configuration.");
        }
        catch (FormatException) { }
        using var stream = typeof(RemoteUrlPatternTests).Assembly.GetManifestResourceStream("netwiv.tests.remote-url-pattern-oracle.json")
            ?? throw new InvalidOperationException("Remote URL oracle fixture is missing.");
        using var data = JsonDocument.Parse(stream);
        var failures = new List<string>();
        foreach (var row in data.RootElement.GetProperty("cases").EnumerateArray())
        {
            var pattern = row.GetProperty("pattern").GetString()!;
            RemoteUrlPattern? parsed = null;
            try { parsed = new(pattern); }
            catch (Exception error) when (error is FormatException or ArgumentException) { }
            if ((parsed is not null) != row.GetProperty("valid").GetBoolean())
            {
                failures.Add($"parse {pattern}: expected {row.GetProperty("valid")}, actual {parsed is not null}");
                continue;
            }
            if (parsed is null) continue;
            var urls = row.GetProperty("urls");
            var expected = row.GetProperty("matches");
            for (var i = 0; i < urls.GetArrayLength(); i++)
            {
                var url = urls[i].GetString()!;
                var canonical = row.GetProperty("canonicalUrls")[i].GetString();
                var actual = canonical is not null && parsed.Matches(canonical);
                if (actual != expected[i].GetBoolean()) failures.Add($"match {pattern} => {url}: expected {expected[i]}, actual {actual}");
                if (Uri.TryCreate(canonical, UriKind.Absolute, out var uri) && parsed.Matches(uri) != actual)
                    failures.Add($"System.Uri changed the original source URL decision for {pattern} => {canonical}.");
            }
        }
        if (failures.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, failures));
        return Task.CompletedTask;
    }
}
