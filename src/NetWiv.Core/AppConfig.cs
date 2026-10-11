namespace NetWiv;

public sealed record AppMetadata(
    string Name,
    string Version,
    string Identifier)
{
    public static AppMetadata Default { get; } = new("NetWiv", "1.0.0", "com.netwiv.app");
}
