internal static class BackendExpectation
{
    internal static string? Parse(string? value) => value switch
    {
        null => null,
        "x11" or "wayland" or "windows" or "macos" => value,
        _ => throw new ArgumentException("NETWIV_EXPECT_BACKEND must be x11, wayland, windows or macos.")
    };

    internal static void Verify(string? expected, string? actual)
    {
        if (expected is not null && !string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected native backend {expected}, observed {actual ?? "(missing)"}.");
    }
}
