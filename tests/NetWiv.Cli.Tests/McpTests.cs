using System.IO.Pipelines;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NetWiv.Cli;

[assembly: System.Reflection.AssemblyMetadata("NetWiv.Frontend.JavaScript.v1", "// synthetic command metadata")]
[assembly: System.Reflection.AssemblyMetadata("NetWiv.Frontend.TypeScript.v1", "export declare function mcpProbe(): Promise<string>;")]

internal static class McpTests
{
    private static readonly Dictionary<string, string> Guides = new()
    {
        ["plugin-configuration"] = """
            # Plugin configuration
            Intro text.

            ## Capabilities
            A capability grants permissions to windows.

            ### Remote URLs
            Remote pages need an explicit capability.

            ## Packaging
            Unrelated packaging notes mention capability once.
            """,
        ["deployment"] = """
            # Deployment
            ```sh
            # not a heading inside a fence
            netwiv build --aot
            ```
            NativeAOT publishing with netwiv build.
            """,
    };

    // Use the same functional check for build output, a published AOT executable,
    // or an installed tool shim. No transcripts or environment inventories persist.
    internal static async Task Stdio()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-mcp-stdio-").FullName;
        using var process = new Process();
        var started = false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), """{"identifier":"dev.netwiv.mcp","mainBinaryName":"Renamed","app":{"security":{"capabilities":[{"identifier":"inline","permissions":[]}]}}}""");
            File.WriteAllText(Path.Combine(root, "Probe.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            Directory.CreateDirectory(Path.Combine(root, "bin"));
            File.Copy(typeof(McpTests).Assembly.Location, Path.Combine(root, "bin", "Probe.dll"));
            File.SetLastWriteTimeUtc(Path.Combine(root, "bin", "Probe.dll"), DateTime.UtcNow.AddDays(-1));
            Directory.CreateDirectory(Path.Combine(root, "target", "release"));
            File.Copy(typeof(McpTests).Assembly.Location, Path.Combine(root, "target", "release", "Renamed.dll"));
            var executable = Environment.GetEnvironmentVariable("NETWIV_MCP_TEST_EXECUTABLE");
            process.StartInfo = new ProcessStartInfo(executable ?? "dotnet")
            {
                WorkingDirectory = root, UseShellExecute = false,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true,
            };
            if (executable is null)
            {
                process.StartInfo.ArgumentList.Add("exec");
                process.StartInfo.ArgumentList.Add("--runtimeconfig");
                process.StartInfo.ArgumentList.Add(Path.ChangeExtension(typeof(McpTests).Assembly.Location, ".runtimeconfig.json"));
                process.StartInfo.ArgumentList.Add("--depsfile");
                process.StartInfo.ArgumentList.Add(Path.ChangeExtension(typeof(McpTests).Assembly.Location, ".deps.json"));
                process.StartInfo.ArgumentList.Add(typeof(CliApp).Assembly.Location);
            }
            process.StartInfo.ArgumentList.Add("mcp");
            started = process.Start();
            Check(started, "stdio process starts");
            var errors = process.StandardError.ReadToEndAsync(timeout.Token);
            var id = 0;
            async Task<JsonElement> Request(string method, object arguments)
            {
                var requestId = ++id;
                await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = requestId, method, @params = arguments }));
                await process.StandardInput.FlushAsync(timeout.Token);
                while (true)
                {
                    var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
                    Check(line is not null, "stdio stays open until the client closes stdin");
                    using var json = JsonDocument.Parse(line!); // Any stdout diagnostics fail this check.
                    if (!json.RootElement.TryGetProperty("id", out var responseId)) continue;
                    Check(responseId.GetInt32() == requestId && !json.RootElement.TryGetProperty("error", out _), "JSON-RPC response succeeds");
                    return json.RootElement.GetProperty("result").Clone();
                }
            }
            var initialized = await Request("initialize", new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "netwiv-test", version = "1" } });
            Check(initialized.GetProperty("serverInfo").GetProperty("name").GetString() == "netwiv", "stdio handshake identifies the server");
            await process.StandardInput.WriteLineAsync("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}");
            var tools = await Request("tools/list", new { });
            Check(tools.GetProperty("tools").GetArrayLength() == 5, "stdio lists all tools");
            foreach (var (name, arguments) in new (string, object)[]
            {
                ("netwiv_docs_search", new { query = "capability" }),
                ("netwiv_docs_read", new { name = "coding-agents" }),
                ("netwiv_project_info", new { }),
                ("netwiv_capabilities", new { }),
                ("netwiv_commands", new { }),
            })
            {
                var result = await Request("tools/call", new { name, arguments });
                Check(!result.TryGetProperty("isError", out var error) || !error.GetBoolean(), "stdio tool succeeds: " + name);
                Check(result.GetProperty("content").GetArrayLength() > 0, "stdio returns tool content");
                if (name == "netwiv_commands") Check(result.ToString().Contains("mcpProbe", StringComparison.Ordinal), "stdio reads actual generated command metadata");
                if (name == "netwiv_commands") Check(result.ToString().Contains("Renamed.dll", StringComparison.Ordinal), "stdio selects the newer mainBinaryName output instead of stale bin metadata");
            }
            File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), """{"identifier":"dev.netwiv.mcp"}""");
            File.WriteAllText(Path.Combine(root, "Probe.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup Condition="'$(Configuration)' == 'Debug'"><AssemblyName>DebugProbe</AssemblyName></PropertyGroup>
                  <PropertyGroup Condition="'$(Configuration)' == 'Release'"><AssemblyName>Renamed</AssemblyName></PropertyGroup>
                </Project>
                """);
            var conditional = await Request("tools/call", new { name = "netwiv_commands", arguments = new { } });
            Check((!conditional.TryGetProperty("isError", out var conditionalError) || !conditionalError.GetBoolean()) &&
                conditional.ToString().Contains("Renamed.dll", StringComparison.Ordinal), "stdio discovers a later conditional assembly name");
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            Check(process.ExitCode == 0 && string.IsNullOrWhiteSpace(await errors), "stdin EOF shuts down cleanly without diagnostics");
            Check(Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length == 4, "MCP tools do not write into the project");
        }
        finally
        {
            if (started && !process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            Directory.Delete(root, true);
        }
    }

    internal static Task Docs()
    {
        var docs = McpDocs.FromText(Guides);
        var hits = docs.Search("capability remote", 5);
        Check(hits.Count > 0 && hits[0].Section.Heading == "Remote URLs", "a section matching every word ranks first");
        Check(docs.Search("zzz-nothing", 5).Count == 0, "no match returns nothing");
        Check(docs.Search("a", 5).Count == 0, "one-letter queries are ignored");

        var section = docs.Read("plugin-configuration", "Capabilities", out var error)!;
        Check(error is null && section.Contains("### Remote URLs", StringComparison.Ordinal) && !section.Contains("Packaging", StringComparison.Ordinal),
            "a section includes its subsections and stops at the next sibling");
        Check(docs.Read("deployment.md", null, out _)!.Contains("not a heading inside a fence", StringComparison.Ordinal), "whole guides read by file name");
        Check(docs.Search("fence heading", 5).All(static hit => hit.Section.Heading != "not a heading inside a fence"), "headings inside code fences are text");
        Check(docs.Read("missing", null, out error) is null && error!.Contains("plugin-configuration", StringComparison.Ordinal), "unknown guides list the available names");
        Check(docs.Read("plugin-configuration", "Nope", out error) is null && error!.Contains("Capabilities", StringComparison.Ordinal), "unknown sections list the headings");
        Check(Directory.EnumerateFiles(McpCommand.DocsDirectory, "*.md").Any(), "the CLI output carries the guides");
        return Task.CompletedTask;
    }

    // The real server and the SDK client over in-memory streams: protocol framing, tool list,
    // read-only results and error reporting.
    internal static async Task Protocol()
    {
        var root = Directory.CreateTempSubdirectory("netwiv-mcp-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), """{"identifier":"dev.netwiv.mcp","productName":"Mcp Probe","version":"1.2.3"}""");
            Directory.CreateDirectory(Path.Combine(root, "capabilities"));
            File.WriteAllText(Path.Combine(root, "capabilities", "default.json"), """{"identifier":"default","windows":["main"],"permissions":["core:default"]}""");
            Directory.CreateDirectory(Path.Combine(root, "permissions", "nested"));
            File.WriteAllText(Path.Combine(root, "permissions", "nested", "probe.toml"), "[[permission]]\nidentifier = 'allow-probe'\ncommands.allow = ['mcp_probe']\n");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var toServer = new Pipe();
            var toClient = new Pipe();
            await using var server = McpServer.Create(
                new StreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream()),
                McpCommand.CreateOptions(root, McpDocs.FromText(Guides)));
            var running = server.RunAsync(timeout.Token);
            await using (var client = await McpClient.CreateAsync(
                new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()), cancellationToken: timeout.Token))
            {
                Check(client.ServerInfo?.Name == "netwiv" && client.ServerInstructions?.Contains("netwiv_docs_search", StringComparison.Ordinal) == true, "server identity and instructions");
                var tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
                Check(tools.Select(static tool => tool.Name).Order(StringComparer.Ordinal).SequenceEqual(
                    ["netwiv_capabilities", "netwiv_commands", "netwiv_docs_read", "netwiv_docs_search", "netwiv_project_info"]), "five tools are listed");
                Check(tools.All(static tool => tool.ProtocolTool.Annotations?.ReadOnlyHint == true), "every tool is marked read-only");

                var search = await Call(client, "netwiv_docs_search", new() { ["query"] = "nativeaot" }, timeout.Token);
                Check(search.IsError != true && Text(search).Contains("deployment", StringComparison.Ordinal), "docs search finds the guide");
                var list = await Call(client, "netwiv_docs_read", [], timeout.Token);
                Check(list.IsError != true && Text(list).Contains("plugin-configuration", StringComparison.Ordinal), "docs can be discovered without knowing a guide name");
                var info = await Call(client, "netwiv_project_info", [], timeout.Token);
                Check(info.IsError != true && Text(info).Contains("identifier: dev.netwiv.mcp", StringComparison.Ordinal) && Text(info).Contains("productName: Mcp Probe", StringComparison.Ordinal),
                    "project info reads the configuration");
                var capabilities = await Call(client, "netwiv_capabilities", [], timeout.Token);
                Check(Text(capabilities).Contains("default.json", StringComparison.Ordinal) && Text(capabilities).Contains("core:default", StringComparison.Ordinal), "capability files are shown");
                Check(Text(capabilities).Contains("allow-probe", StringComparison.Ordinal), "nested application permissions are included");
                var commands = await Call(client, "netwiv_commands", [], timeout.Token);
                Check(commands.IsError == true, "commands without an application project report an error result");
                File.WriteAllText(Path.Combine(root, "Probe.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
                var build = Path.Combine(root, "target", "probe", "release");
                Directory.CreateDirectory(build);
                File.Copy(typeof(McpTests).Assembly.Location, Path.Combine(build, "Probe.dll"));
                commands = await Call(client, "netwiv_commands", [], timeout.Token);
                Check(commands.IsError != true && Text(commands).Contains("mcpProbe", StringComparison.Ordinal), "root-level projects find their own CLI target output and read PE metadata");
                File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), """{"identifier":"dev.netwiv.mcp","mainBinaryName":"Renamed"}""");
                File.Move(Path.Combine(build, "Probe.dll"), Path.Combine(build, "Renamed.dll"));
                foreach (var assemblyName in new[] { "", "<AssemblyName>Declared</AssemblyName>" })
                {
                    File.WriteAllText(Path.Combine(root, "Probe.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework>" + assemblyName + "</PropertyGroup></Project>");
                    commands = await Call(client, "netwiv_commands", [], timeout.Token);
                    Check(commands.IsError != true && Text(commands).Contains("Renamed.dll", StringComparison.Ordinal) && Text(commands).Contains("mcpProbe", StringComparison.Ordinal),
                        "mainBinaryName output is found with implicit or explicit project assembly names");
                }
                File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), """{"identifier":"dev.netwiv.mcp"}""");
                foreach (var assemblyProperties in new[]
                {
                    "<PropertyGroup Condition=\"'$(Configuration)' == 'Debug'\"><AssemblyName>DebugProbe</AssemblyName></PropertyGroup><PropertyGroup Condition=\"'$(Configuration)' == 'Release'\"><AssemblyName>Renamed</AssemblyName></PropertyGroup>",
                    "<PropertyGroup><AssemblyName>$(CustomName)</AssemblyName><AssemblyName> </AssemblyName><AssemblyName>Renamed</AssemblyName><AssemblyName>Renamed</AssemblyName></PropertyGroup>",
                })
                {
                    File.WriteAllText(Path.Combine(root, "Probe.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\">" + assemblyProperties + "</Project>");
                    commands = await Call(client, "netwiv_commands", [], timeout.Token);
                    Check(commands.IsError != true && Text(commands).Contains("Renamed.dll", StringComparison.Ordinal), "all literal assembly names are considered without evaluating MSBuild");
                }
                File.WriteAllText(Path.Combine(root, "Probe.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup Condition=\"'$(Configuration)' == 'Debug'\"><AssemblyName>DebugProbe</AssemblyName></PropertyGroup></Project>");
                File.Move(Path.Combine(build, "Renamed.dll"), Path.Combine(build, "Probe.dll"));
                commands = await Call(client, "netwiv_commands", [], timeout.Token);
                Check(commands.IsError != true && Text(commands).Contains("Probe.dll", StringComparison.Ordinal), "the default project name remains a candidate when conditional overrides do not apply");
                var missing = await Call(client, "netwiv_docs_read", new() { ["name"] = "missing" }, timeout.Token);
                Check(missing.IsError == true && Text(missing).Contains("Available", StringComparison.Ordinal), "tool errors are results, not protocol failures");
                var outside = await Call(client, "netwiv_project_info", new() { ["directory"] = "no-such-dir" }, timeout.Token);
                Check(outside.IsError == true, "a missing directory is rejected");
                var invalid = await Call(client, "netwiv_project_info", new() { ["directory"] = 42 }, timeout.Token);
                Check(invalid.IsError == true, "invalid directory arguments do not fall back to reading the current project");
                File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), "{ invalid JSON");
                var broken = await Call(client, "netwiv_project_info", [], timeout.Token);
                Check(broken.IsError == true, "malformed projects are tool errors, not a successful no-project result");
            }
            toServer.Writer.Complete();
            await running.WaitAsync(timeout.Token);
        }
        finally { Directory.Delete(root, true); }
    }

    private static Task<CallToolResult> Call(McpClient client, string name, Dictionary<string, object?> args, CancellationToken token)
        => client.CallToolAsync(name, args, cancellationToken: token).AsTask();

    private static string Text(CallToolResult result) => string.Concat(result.Content.OfType<TextContentBlock>().Select(static block => block.Text));

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("MCP: " + message);
    }
}
