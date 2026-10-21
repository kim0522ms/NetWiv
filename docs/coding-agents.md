# Coding agents

`netwiv mcp` runs a local [Model Context Protocol](https://modelcontextprotocol.io/)
server on standard input and output. A coding agent that supports MCP, such as
Claude Code, Codex or Cursor, can use it to read the NetWiv guides for the CLI
version in use and the current project's configuration, capabilities and
commands. The server runs on the developer's machine; nothing is hosted or
sent elsewhere.

The tools only read. Building, running and editing stay with the agent's own
file and terminal tools and the `netwiv` CLI.

## New projects

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
| `netwiv_docs_read` | A whole guide, or one section with its subsections |
| `netwiv_project_info` | CLI and .NET SDK versions, the configuration file and merged overlays, identifiers, build settings, the application project and its NetWiv package versions |
| `netwiv_capabilities` | Inline capabilities, capability files and application permission files |
| `netwiv_commands` | The commands of the last built application as generated TypeScript declarations and, where enabled, the typed C# client |

The project tools accept an optional `directory`, relative to the server's
working directory. `netwiv_project_info` reports when the project's NetWiv
package version differs from the CLI serving the guides.

## Limits

- The guides are the `docs/` folder of the NetWiv revision the CLI was built
  from. Search matches words in headings and text; it is not semantic search.
- `netwiv_commands` reads metadata from the newest built application assembly
  without loading or running it. Build the application after changing commands;
  an application without `[assembly: GenerateFrontendBindings]` has no command
  metadata to show.
- `netwiv_capabilities` shows the configured files as written. It does not
  compute the effective permissions for a window; see
  [plugin configuration](plugin-configuration.md) for the rules.
- Standard output carries only protocol messages while the server runs.
  Diagnostics go to standard error.
