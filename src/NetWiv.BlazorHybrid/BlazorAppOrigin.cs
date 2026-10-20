namespace NetWiv.BlazorHybrid;

internal static class BlazorAppOrigin
{
    internal static readonly Uri Windows = new("http://netwiv-blazor.localhost/");
    internal static readonly Uri Native = new("netwiv-blazor://localhost/");

    internal static bool Matches(Uri uri, Uri origin) => uri.IsAbsoluteUri &&
        uri.Scheme == origin.Scheme && uri.IdnHost == origin.IdnHost &&
        uri.Port == origin.Port && string.IsNullOrEmpty(uri.UserInfo);
}
