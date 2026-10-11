namespace NetWiv;

/// <summary>Matches Tauri-style absolute URL scope patterns.</summary>
public sealed class UrlScopePattern
{
    private readonly RemoteUrlPattern _pattern;

    public UrlScopePattern(string pattern) => _pattern = new RemoteUrlPattern(pattern);

    public bool Matches(Uri url) => _pattern.Matches(url);
}
