# Coding agents

`netwiv mcp` runs a local [Model Context Protocol](https://modelcontextprotocol.io/)
server on standard input and output. A coding agent that supports MCP, such as
Claude Code, Codex or Cursor, can use it to read the NetWiv guides for the CLI
version in use and the current project's configuration, capabilities and
commands. The server runs locally over stdio and does not host an HTTP service.
Tool results are delivered to the connected coding agent, which may send them to
its model provider according to that client's settings.

The tools only read. Building, running and editing stay with the agent's own
file and terminal tools and the `netwiv` CLI.

## New projects

### Bootstrap version choices

Before creating a project, ask the user which frontend and .NET versions to
use. Offer React 18 and .NET 10 as the defaults, and wait for the answer before
creating files. Reuse an explicit choice already given in the conversation;
do not ask again. Preserve the versions of an existing project unless the user
requests a migration. Ask about React only when the chosen frontend is React.

In a terminal, `netwiv new` asks for unspecified React and .NET versions.
Enter selects React 18.3.1 and `net10.0`. Explicit version options skip their
questions; `--yes` skips all questions. Redirected stdin or stdout also skips
questions and uses explicit options or defaults, so scripts never wait for input.
EOF or Ctrl+C during version selection stops creation before writing files.
The agent asks the user before invoking the CLI and passes the selected versions:
`--react-version 18|19` for React and
`--framework net8.0|net9.0|net10.0` to apply the selected .NET target across
the starter's projects; use an SDK that supports that target. Framework
packages retain their .NET 8 compatibility baseline. .NET 10 is the default
for new applications, including those opting into NativeAOT; this does not
guarantee AOT compatibility for every application dependency.

For example, after the user selects React 18 and .NET 10:

```sh
netwiv new MyApp --template react --react-version 18 --framework net10.0 --source /path/to/packages
```

For React 19, pass `--react-version 19`. The CLI selects matching runtime and
type packages. For npm, resolving the updated lockfile requires npm and access
to its registry or an existing cache; package lifecycle scripts are disabled:

```sh
netwiv new MyApp --template react --react-version 19 --framework net10.0 --source /path/to/packages
```

Keep `react` and `react-dom` on matching versions and update the lockfile using
the selected package manager. Run the generated frontend build after a version
change. Other React versions require their own dependency and application checks;
the default starter's validation does not establish their compatibility.

`netwiv new` writes `.mcp.json` at the project root:

```json
{
  "mcpServers": {
    "netwiv": {
      "command": "dotnet",
      "args": ["tool", "run", "netwiv", "--", "mcp"]
    }
  }
}
```

The command runs the project's local CLI from `.config/dotnet-tools.json`, so the
guides match the NetWiv version the project uses. Restore the local tool once
before an agent starts the server:

```sh
dotnet tool restore
```

Agents that read `.mcp.json`, such as Claude Code, pick it up when the project
folder is opened and may ask for approval the first time. Commit the file so the
registration is shared with the project.

## Other agents and existing projects

The server is an ordinary stdio command; register it with the agent's own MCP
configuration. Start it from the project folder so `dotnet tool run` finds the
local tool manifest:

| Agent | Registration |
| --- | --- |
| Claude Code | Project `.mcp.json` as above, or `claude mcp add netwiv -- dotnet tool run netwiv -- mcp` from the project folder |
| Codex | An `[mcp_servers.netwiv]` entry with `command = "dotnet"` and `args = ["tool", "run", "netwiv", "--", "mcp"]` in its configuration |
| Cursor | The same JSON as `.mcp.json` in `.cursor/mcp.json` |
| Others | Command `dotnet`, arguments `tool run netwiv -- mcp`, stdio transport |

Before a project exists, a globally installed CLI can serve the guides with
`netwiv mcp`. Use the project's local CLI afterwards so the guides follow the
project's version.

## Tools

| Tool | Returns |
| --- | --- |
| `netwiv_docs_search` | Guide sections that match the query words, with the guide name and heading |
| `netwiv_docs_read` | Available guide names when `name` is omitted; otherwise a whole guide or one section with its subsections |
| `netwiv_project_info` | CLI version, the configuration file and merged overlays, identifiers, build settings, the application project and its NetWiv package references |
| `netwiv_capabilities` | Inline capabilities, capability files and recursive application permission files |
| `netwiv_commands` | The commands of the last built application as generated TypeScript declarations and, where enabled, the typed C# client |

The project tools accept an optional `directory`, relative to the server's
working directory. `netwiv_project_info` reports when the project's NetWiv
package version differs from the CLI serving the guides.

Use this server only with projects and MCP clients you trust. The `directory`
argument and configuration references follow local CLI path rules; the working
directory is not a filesystem sandbox. Returned configuration and generated
declarations can contain project-sensitive values. No OS/device inventory is
collected, and tools do not launch builds, execute application assemblies or
run project hooks.

## Limits

- The guides are the `docs/` folder of the NetWiv revision the CLI was built
  from. Search matches words in headings and text; it is not semantic search.
- `netwiv_commands` reads metadata from the newest built application assembly
  under the application's `bin/` or the configured project root's `target/`,
  without loading or running it. All literal `AssemblyName` values in the project
  (including conditional declarations), the default project name and configured
  `mainBinaryName` are candidates for the newest output. MSBuild conditions and
  property expressions are not evaluated.
  Build the application after changing commands;
  an application without `[assembly: GenerateFrontendBindings]` has no command
  metadata to show.
- Package versions are read from project XML without executing MSBuild. Imported,
  conditional and centrally managed versions are not evaluated; unresolved values
  do not establish that the application's resolved version matches the CLI.
- `netwiv_capabilities` shows the configured files as written. It does not
  compute the effective permissions for a window; see
  [plugin configuration](plugin-configuration.md) for the rules. File and directory
  links are skipped during capability/permission enumeration.
- Standard output carries only protocol messages while the server runs.
  Diagnostics go to standard error.

## Maintainer checks

`dotnet run --project tests/NetWiv.Cli.Tests -c Release -- --filter mcp` exercises
guide discovery, project/permission/command reads and an actual stdio subprocess,
including initialization, tool calls, protocol-only stdout and clean exit on stdin
EOF. Set `NETWIV_MCP_TEST_EXECUTABLE` to a published CLI executable or an installed
tool shim and select `--filter "mcp stdio"` to run the same functional check on
that artifact. This also checks that its bundled guides are available.
