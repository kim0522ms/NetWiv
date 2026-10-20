# SQLite databases and migrations

Register `NetWiv.Plugin.Sql` with the application builder. Frontend applications
use `Database` from `@netwiv/api/plugins/sql` for `load`, `execute`, `select` and
`close`. A connection such as `sqlite:app.db` opens below the application config
directory; absolute paths and paths escaping that directory are rejected.
SQLite is the only supported database engine in this plugin.

## Forward migrations

Register migrations in the application backend before `Builder.Build()`:

```csharp
using NetWiv;
using NetWiv.Plugin.Sql;

var sql = new SqlPlugin().AddMigrations("sqlite:app.db",
[
    new SqlMigration(1, "create notes", """
        CREATE TABLE notes (id INTEGER PRIMARY KEY, title TEXT NOT NULL);
        INSERT INTO notes (title) VALUES ('Welcome');
        """),
    new SqlMigration(2, "add completed flag", """
        ALTER TABLE notes ADD COLUMN completed INTEGER NOT NULL DEFAULT 0;
        """)
]);

// Add this plugin to the application's configured Builder:
builder.Plugin(sql);
```

`AddMigrations` takes a snapshot of the supplied collection. Register one complete
history for each database, with positive, strictly increasing
64-bit versions. Gaps are allowed. Duplicate versions, descending versions,
duplicate database registrations, empty descriptions/SQL and NUL characters
are rejected. Registration after plugin initialization is rejected.
Relative path aliases such as `sqlite:./app.db` resolve to the same migration and
connection identity as `sqlite:app.db`, including casing and separators on
Windows. Registrations that resolve to the same path are rejected during setup.
This normalization is lexical; filesystem link aliases are not resolved.

The first `Database.load("sqlite:app.db")` applies pending migrations before the
connection becomes available. Configuration preloads take the same path during
application setup:

```json
{
  "plugins": {
    "sql": { "preload": ["sqlite:app.db"] }
  }
}
```

The plugin stores applied versions, descriptions and SHA-256 SQL checksums in
`__netwiv_migrations`. Keep all applied definitions unchanged, including SQL
whitespace. Append a new version for schema changes. Missing or changed applied
versions, descriptions or SQL cause load to fail; they do not silently reset the
database. Do not modify this reserved table through application SQL or other
database tools. It is migration bookkeeping, not a security boundary against a
caller already granted arbitrary database writes.

History validation and all pending scripts run in one `BEGIN IMMEDIATE`
transaction. Connections in other plugin instances or processes contend on the
SQLite write lock, waiting up to five seconds during migration. A competing
opener then checks the committed history before applying anything. A lock timeout
fails load and can be retried. A failed script rolls back the pending batch and
its history records. A failed load closes its connection and retains the
registered definitions, so a later load can retry after the underlying cause is
fixed. Previously committed migrations stay applied. Failed preloads fail
application setup; restart with the cause corrected. Close/reopen and a later app
start validate the history again and skip already applied SQL.

Scripts may contain multiple statements, SQL comments and trigger bodies.
SQLite's parser determines statement boundaries. The host owns the transaction:
scripts cannot issue transaction/savepoint commands, PRAGMA, ATTACH or DETACH, or
access the migration history table. Use ordinary transactional schema/data SQL;
perform any necessary connection administration through a separately validated
backend path. Migrations do not provide a frontend command to run arbitrary
scripts or change schema versions. Cancellation of a load interrupts running
migration SQL and rolls back pending changes; waiting for a busy SQLite lock may
last until its five-second timeout.

## Permissions and values

Migrations are trusted backend configuration. They run when a database is loaded
or preloaded, even when the guest has only `sql:default`. Only register scripts
owned by the application. Guest input never supplies migration definitions.
`sql:default` permits load, select and close; writes through `execute` require an
explicit `sql:allow-execute` grant. The SQLite authorizer continues to reject
writes, PRAGMA, ATTACH and transaction controls through `select`, including after
migration or reopen.

Bound JSON null, signed 64-bit integers, fractional numbers and strings map to
SQLite null, integer, real and text. Out-of-range integral JSON values are
rejected instead of being rounded to floating point. Other JSON values use their
JSON text. Results map SQLite storage classes to JSON numbers, strings, null and
byte arrays for BLOBs. Declared-type conversion (for example BOOLEAN/date/JSON
columns) remains unsupported; validate and convert these in application code.

## Deliberate differences and limits

The pinned reference is the SQL plugin 2.4.1 at
[`6aa2854`](https://github.com/tauri-apps/plugins-workspace/blob/6aa2854f314481a459be1189b02c65a2450789ab/plugins/sql/src/lib.rs).
Its Rust builder registers migrations for SQLx. NetWiv provides an immutable
`SqlMigration` and fluent C# registration to work with NativeAOT and its existing
native SQLite backend, without a runtime serializer or a second database stack.
This is an independently implemented .NET API, not Rust API parity.

NetWiv applies forward migrations only, validates an ordered complete history,
and rolls back all pending migrations together. It uses its own metadata table;
it does not import SQLx `_sqlx_migrations` histories or offer automatic down
migrations. An existing database migrating from SQLx needs an application-owned
schema/data transition plan; do not simply register old creation scripts over an
existing schema. These deliberate differences apply to all NetWiv desktop
platforms. No existing NetWiv migration API is replaced by this addition.

The plugin uses system SQLite (`winsqlite3` on Windows and the `sqlite3` native
import on non-Windows hosts), so engine features depend on the installed version.
MySQL, PostgreSQL, SQLx history import and declared-type result conversion are
unsupported. Managed and NativeAOT functional tests cover native SQLite migrations
and IPC permission boundaries. Both execution modes passed on physical Windows
x64 and macOS arm64 hosts and Linux x64 under WSL, through headless test processes.
These tests do not establish a WebView interaction or clean-machine distribution
check on every desktop platform. See
[plugin status](official-plugins.md) for platform validation boundaries.
