namespace NetWiv;

/// <summary>The application and WebView which originated a custom protocol request.</summary>
public sealed record UriSchemeContext(AppHandle AppHandle, string WebviewLabel);
