using System.IO.Pipelines;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using NetWiv.Cli;

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
                var info = await Call(client, "netwiv_project_info", [], timeout.Token);
                Check(info.IsError != true && Text(info).Contains("identifier: dev.netwiv.mcp", StringComparison.Ordinal) && Text(info).Contains("productName: Mcp Probe", StringComparison.Ordinal),
                    "project info reads the configuration");
                var capabilities = await Call(client, "netwiv_capabilities", [], timeout.Token);
                Check(Text(capabilities).Contains("default.json", StringComparison.Ordinal) && Text(capabilities).Contains("core:default", StringComparison.Ordinal), "capability files are shown");
                var commands = await Call(client, "netwiv_commands", [], timeout.Token);
                Check(commands.IsError == true, "commands without an application project report an error result");
                var missing = await Call(client, "netwiv_docs_read", new() { ["name"] = "missing" }, timeout.Token);
                Check(missing.IsError == true && Text(missing).Contains("Available", StringComparison.Ordinal), "tool errors are results, not protocol failures");
                var outside = await Call(client, "netwiv_project_info", new() { ["directory"] = "no-such-dir" }, timeout.Token);
                Check(outside.IsError == true, "a missing directory is rejected");
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
