namespace NetWiv.Plugin.Sql;

/// <summary>An immutable forward SQLite schema migration registered by the application backend.</summary>
public sealed record SqlMigration(long Version, string Description, string Sql);
