using NetWiv.Build.Configuration;
using NetWiv.Build.Execution;
using NetWiv.Build.Packaging;
using NetWiv.Cli;

if (args is ["--bundle-sign-probe", var signedPath]) return BundleSigningTests.Sign(signedPath);
if (Environment.GetEnvironmentVariable("NETWIV_BUNDLE_PROBE") == "1") return BundleSigningTests.Pack(args);

if (args is ["--owned-process-probe", var phase, var report])
    return await ProcessOwnershipTests.Probe(phase, report);

if (args is ["--hook-probe", var hookReport, var hookCode, var prerequisite])
    return await HookTests.Probe(hookReport, hookCode, prerequisite);

if (args is ["--frontend-consumer", var consumer])
{
    await FrontendDevTests.Run(consumer);
    return 0;
}

if (Environment.GetEnvironmentVariable("NETWIV_RUNNER_PROBE_OUTPUT") is { } runnerProbe)
{
    var record = new System.Text.Json.Nodes.JsonObject { ["cwd"] = Environment.CurrentDirectory,
        ["pid"] = Environment.ProcessId, ["patch"] = Environment.GetEnvironmentVariable("TAURI_CONFIG"),
        ["environment"] = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"),
        ["args"] = new System.Text.Json.Nodes.JsonArray(args.Select(arg => (System.Text.Json.Nodes.JsonNode?)System.Text.Json.Nodes.JsonValue.Create(arg)).ToArray()) };
    ConcurrentFile.Publish(runnerProbe, record.ToJsonString());
    if (Environment.GetEnvironmentVariable("NETWIV_RUNNER_PROBE_WAIT") is { } runnerWait)
    {
        if (runnerWait == "1") await Task.Delay(Timeout.Infinite);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (!File.Exists(runnerWait)) await Task.Delay(20, timeout.Token);
    }
    return 17;
}

var tests = new (string Name, Func<Task> Run)[]
{
    ("Windows installer options validate before hooks and tools", WindowsInstallerTests.Validation),
    ("Windows installer options render scope lifecycle and literal arguments", WindowsInstallerTests.Rendering),
    ("macOS targets configuration and metadata", MacOsTests.Configuration),
    ("macOS application and dmg packaging with real signing tools", MacOsTests.Packaging),
    ("bundle target arrays accept supported formats and reject malformed entries", BundleTargetTests.ValidateTargets),
    ("consumer publish managed self-contained and bundled execution", () => FirstBuildTests.MissingDist("managed")),
    ("consumer publish framework-dependent and bundled execution", () => FirstBuildTests.MissingDist("framework")),
    ("consumer publish NativeAOT embeds absent frontend output", () => FirstBuildTests.MissingDist("aot")),
    ("consumer publish rejects conflicting deployment modes", FirstBuildTests.InvalidModes),
    ("P2 Windows installers enforce prerequisites and integrate install uninstall", async () =>
    {
        if (OperatingSystem.IsWindows()) await BundleSigningTests.Installation();
        else Console.WriteLine("SKIP Windows packaging tools: Windows only");
    }),
    ("P1 Windows bundle signs final payload and honors no-sign", async () =>
    {
        if (OperatingSystem.IsWindows()) await BundleSigningTests.FinalPayload();
        else Console.WriteLine("SKIP Windows packaging tools: Windows only");
    }),
    ("new project creates a personalized package-only React application", NewProjectTests.CreateReact),
    ("new project defaults keep matching SDK and CLI versions", NewProjectTests.Defaults),
    ("new project covers the upstream UI template matrix and manager choices", NewProjectTests.TemplateMatrix),
    ("new project rejects invalid arguments before writing files", NewProjectTests.InvalidInput),
    ("new project preserves existing files and honors cancellation", NewProjectTests.PreserveAndCancel),
    ("new project help list and shell completions are discoverable", NewProjectTests.Discovery),
    ("owned process preserves successful and failing exit codes", ProcessOwnershipTests.ExitCodes),
    ("upstream winres::version_uses_numeric_build_metadata", WindowsFileVersionTests.NumericMetadata),
    ("upstream winres::version_ignores_non_numeric_composite_build_metadata", WindowsFileVersionTests.CompositeMetadata),
    ("upstream winres::version_ignores_non_numeric_build_metadata", WindowsFileVersionTests.TextMetadata),
    ("upstream winres::version_ignores_build_metadata_that_does_not_fit_in_u16", WindowsFileVersionTests.LargeMetadata),
    ("application package versions resolve validate and join watched configuration inputs", WindowsFileVersionTests.ConfigResolution),
    ("plugin configuration merges without losing scalar values and rejects non-object roots", PluginConfiguration),
    ("cancelled runner does not start a process", ProcessOwnershipTests.CancelledBeforeStart),
    ("development watcher discards transient build files but preserves source changes", DevReloadTests.TransientFiles),
    ("development reload retries configuration files that are still being saved", DevReloadTests.SavingRetry),
    ("direct-root config keeps project root", DirectRoot),
    ("nested new config is discovered", NestedConfig),
    ("new and legacy bases are ambiguous", AmbiguousConfig),
    ("platform and CLI patches use RFC7396", MergePatch),
    ("CLI patches compose before applying to platform configuration", ConfigOverlayTests.CompositionBoundaries),
    ("upstream config::merge_patches", ConfigOverlayTests.UpstreamMergePatches),
    ("build bundle and waiting development hooks await completion and propagate failures", HookTests.Completion),
    ("build hooks and runners receive composed configuration environment", HookTests.BuildEnvironment),
    ("failing development hook terminates managed and AOT runners", HookTests.DevFailure),
    ("hook configuration rejects invalid fields before process launch", HookTests.Configuration),
    ("plain JSON is strict", StrictJson),
    ("JSON5 syntax is parsed", Json5Config),
    ("TOML tables and array tables are parsed", TomlConfig),
    ("TOML multiline strings dates and quoted keys are parsed", FullTomlConfig),
    ("JSON5 escapes and comments are parsed", FullJson5Config),
    ("parsed syntax does not bypass config validation", ParsedSyntaxStillValidated),
    ("x64 target mapping is explicit", TargetMapping),
    ("payload staging preserves paths and excludes bundle", PayloadStaging),
    ("payload staging never traverses excluded bundle symlinks", PayloadStagingExcludedLinks),
    ("upstream resources::resource_relative_paths", ResourcePathsTests.RelativePaths),
    ("upstream resources::resource_paths_iter_slice_allow_walk", () => ResourcePathsTests.Run("resource_paths_iter_slice_allow_walk")),
    ("upstream resources::resource_paths_iter_slice_no_walk", () => ResourcePathsTests.Run("resource_paths_iter_slice_no_walk")),
    ("upstream resources::resource_paths_iter_map_allow_walk", () => ResourcePathsTests.Run("resource_paths_iter_map_allow_walk")),
    ("upstream resources::resource_paths_iter_map_no_walk", () => ResourcePathsTests.Run("resource_paths_iter_map_no_walk")),
    ("upstream resources::resource_paths_errors", () => ResourcePathsTests.Run("resource_paths_errors")),
    ("resource mapping preserves literal destinations dotfiles and rejects invalid globs", ResourcePathsTests.Boundaries),
    ("resource staging copies changes removes stale owned files and confines destinations", ResourcePathsTests.Staging),
    ("resource watchers detect external glob additions and deletions in managed and AOT development", ResourcePathsTests.Watching),
    ("standalone bundle refreshes resource inputs and reports missing sources", ResourcePathsTests.BundleRefresh),
    ("bundle marker patch is exact and unambiguous", BundleMarkerPatch),
    ("upstream category::category_from_string_ok", AppCategoryTests.Valid),
    ("upstream category::category_from_string_did_you_mean", AppCategoryTests.Suggestions),
    ("upstream category::category_from_string_totally_wrong", AppCategoryTests.Wrong),
    ("upstream category::ls_application_category_type_round_trip", AppCategoryTests.RoundTrip),
    ("bundle category validation aliases and Linux desktop metadata", AppCategoryTests.DesktopMetadata),
    ("upstream msi::validates_wix_version", MsiVersionTests.ValidatesWixVersion),
    ("upstream msi::converts_version_to_wix", MsiVersionTests.ConvertsVersionToWix),
    ("MSI version precedence overrides and numeric boundaries", MsiVersionTests.Boundaries),
    ("upstream config::test_runner_config_string_format", RunnerConfigTests.StringFormat),
    ("upstream config::test_runner_config_object_format_full", RunnerConfigTests.ObjectFull),
    ("upstream config::test_runner_config_object_format_minimal", RunnerConfigTests.ObjectMinimal),
    ("upstream config::test_runner_config_default", RunnerConfigTests.Default),
    ("upstream config::test_runner_config_from_str", RunnerConfigTests.FromStr),
    ("upstream config::test_runner_config_from_string", RunnerConfigTests.FromString),
    ("upstream config::test_runner_config_from_str_parse", RunnerConfigTests.FromStrParse),
    ("upstream config::test_runner_config_in_build_config", RunnerConfigTests.InBuildConfig),
    ("upstream config::test_runner_config_in_build_config_object", RunnerConfigTests.InBuildConfigObject),
    ("upstream config::test_runner_config_in_full_config", RunnerConfigTests.InFullConfig),
    ("upstream config::test_runner_config_equality", RunnerConfigTests.Equality),
    ("upstream config::test_runner_config_untagged_serialization", RunnerConfigTests.UntaggedSerialization),
    ("runner parsing and CLI override boundaries", RunnerConfigTests.Boundaries),
    ("build and managed/AOT dev honor runner cwd args and child exit code", RunnerConfigTests.ChildProcess),
    ("cancelled owned process terminates promptly", ProcessCancellation),
    ("init creates new layout with local references", InitLayout),
    ("init defaults to matching NuGet Wry and generator packages", InitPackages),
    ("plugin scaffold emits AOT registration contract", PluginScaffold),
    ("plugin scaffold defaults to matching NuGet versions and preserves existing files", PluginNuGetScaffold),
    ("effective config expands capabilities", EffectiveConfig),
    ("upstream acl::capability::capability_file_de", CapabilityFileDe),
    ("effective capabilities reject malformed inactive entries before platform filtering", CapabilityInputBoundaries),
    ("effective ACL discovers recursive TOML and JSON permissions", EffectivePermissionDiscovery),
    ("empty permission inputs do not create an app manifest", EmptyEffectivePermissions),
    ("effective ACL preserves default permissions and sets", EffectivePermissionSets),
    ("effective ACL rejects unresolved active app permissions", UnknownEffectivePermission),
    ("effective ACL rejects permission set cycles", EffectivePermissionCycle),
    ("effective ACL follows pinned duplicate identifier resolution", DuplicateEffectivePermission),
    ("effective ACL rejects malformed permission structures", MalformedEffectivePermission),
    ("unsupported accepted flags fail explicitly", UnsupportedFlags),
    ("additional watch folders resolve with CLI precedence", AdditionalWatchFolders),
    ("isolation assets resolve from configuration and remain watched with CLI overrides", IsolationWatchFolders),
    ("built-in development server serves assets and reloads", BuiltInDevelopmentServer),
    ("development server port follows CLI and Tauri environment precedence", DevelopmentServerPort),
    ("TOML capabilities support creation and permission edits", TomlCapabilityEditing),
    ("signer artifacts interoperate with Tauri and minisign", SignerCompatibility),
    ("CLI reports unknown commands", UnknownCommand),
    ("asset protocol configuration retains strict scope types", AssetProtocolConfiguration),
    ("managed dev without watching overrides features and rejects invalid names", DevReloadTests.NoWatchFeatures),
    ("managed dev reloads permissions and overlays and cleans up children", () => DevReloadTests.Run(false)),
    ("AOT dev reloads permissions and overlays and cleans up children", () => DevReloadTests.Run(true))
};

var failures = 0;
var selected = args.Length == 0 ? tests : args.Length == 2 && args[0] is "--filter" or "--exclude"
    ? tests.Where(test => test.Name.Contains(args[1], StringComparison.OrdinalIgnoreCase) == (args[0] == "--filter")).ToArray()
    : throw new ArgumentException("Usage: NetWiv.Cli.Tests [--filter text | --exclude text]");
if (selected.Length == 0) throw new ArgumentException("The test filter matched no cases.");
foreach (var test in selected)
{
    Console.WriteLine($"RUN {test.Name}");
    var timer = System.Diagnostics.Stopwatch.StartNew();
    try { await test.Run(); Console.WriteLine($"PASS {test.Name} ({timer.Elapsed.TotalSeconds:F2}s)"); }
    catch (Exception exception) { failures++; Console.Error.WriteLine($"FAIL {test.Name} ({timer.Elapsed.TotalSeconds:F2}s): {exception}"); }
}
return failures == 0 ? 0 : 1;

static Task DirectRoot() => Temp(root =>
{
    File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), "{\"identifier\":\"com.test.app\",\"build\":{}}");
    var config = ConfigLoader.Load(root, "windows"); Equal(root, config.ProjectDirectory); return Task.CompletedTask;
});

static Task AssetProtocolConfiguration() => Temp(root =>
{
    var file = Path.Combine(root, "netwiv.conf.json");
    foreach (var value in new[] { "{}", "{\"enable\":true,\"scope\":[\"$TEMP/**\"]}", "{\"scope\":{\"allow\":[],\"deny\":[],\"requireLiteralLeadingDot\":null}}" })
    {
        File.WriteAllText(file, "{\"identifier\":\"com.test.app\",\"app\":{\"security\":{\"assetProtocol\":" + value + "}}}");
        ConfigLoader.Load(root, "windows");
    }
    foreach (var value in new[] { "null", "[]", "{\"enable\":null}", "{\"enable\":\"yes\"}", "{\"scope\":null}", "{\"scope\":[1]}", "{\"scope\":{\"allow\":null}}", "{\"scope\":{\"deny\":[false]}}", "{\"unknown\":true}", "{\"scope\":{\"requireLiteralLeadingDot\":true,\"require-literal-leading-dot\":false}}" })
    {
        File.WriteAllText(file, "{\"identifier\":\"com.test.app\",\"app\":{\"security\":{\"assetProtocol\":" + value + "}}}");
        Throws<NetWivConfigException>(() => ConfigLoader.Load(root, "windows"));
    }
    return Task.CompletedTask;
});

static Task NestedConfig() => Temp(root =>
{
    var configDir = Path.Combine(root, "src-netwiv"); Directory.CreateDirectory(configDir);
    File.WriteAllText(Path.Combine(configDir, "netwiv.conf.json"), "{\"identifier\":\"com.test.app\"}");
    var nested = Path.Combine(root, "frontend", "src"); Directory.CreateDirectory(nested);
    var config = ConfigLoader.Load(nested, "windows"); Equal(root, config.ProjectDirectory); return Task.CompletedTask;
});

static Task AmbiguousConfig() => Temp(root =>
{
    Directory.CreateDirectory(Path.Combine(root, "src-netwiv")); Directory.CreateDirectory(Path.Combine(root, "src-tauri"));
    File.WriteAllText(Path.Combine(root, "src-netwiv", "netwiv.conf.json"), "{}"); File.WriteAllText(Path.Combine(root, "src-tauri", "tauri.conf.json"), "{}");
    Throws<NetWivConfigException>(() => ConfigLoader.Discover(root)); return Task.CompletedTask;
});

static Task MergePatch() => Temp(root =>
{
    var dir = Path.Combine(root, "src-netwiv"); Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "netwiv.conf.json"), "{\"identifier\":\"com.test.app\",\"build\":{\"features\":[\"a\"],\"devUrl\":\"old\"},\"remove\":1}");
    File.WriteAllText(Path.Combine(dir, "netwiv.windows.conf.json"), "{\"build\":{\"features\":[\"b\"]},\"remove\":null}");
    var config = ConfigLoader.Load(root, "windows", ["{\"build\":{\"devUrl\":\"new\"}}"]);
    Equal("b", config.Strings("build", "features").Single()); Equal("new", config.String("build", "devUrl")); True(config.Node("remove") is null); return Task.CompletedTask;
});

static Task StrictJson() => Temp(root =>
{
    File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), "{/* comment */ \"identifier\":\"x\",}");
    Throws<NetWivConfigException>(() => ConfigLoader.Load(root, "windows")); return Task.CompletedTask;
});

static Task Json5Config() => Temp(root =>
{
    File.WriteAllText(Path.Combine(root, "netwiv.conf.json5"), "{ // comment\n identifier: 'com.test.app', build: { features: ['a',], }, }");
    var config = ConfigLoader.Load(root, "windows"); Equal("com.test.app", config.String("identifier")); Equal("a", config.Strings("build", "features").Single()); return Task.CompletedTask;
});

static Task TomlConfig() => Temp(root =>
{
    File.WriteAllText(Path.Combine(root, "NetWiv.toml"), "identifier = \"com.test.app\"\n[build]\nfeatures = [\"a\", \"b\"]\n[[app.windows]]\nlabel = \"main\"\n");
    var config = ConfigLoader.Load(root, "linux"); Equal(2, config.Strings("build", "features").Count); Equal("main", config.Node("app", "windows")![0]!["label"]!.GetValue<string>()); return Task.CompletedTask;
});

static Task FullTomlConfig() => Temp(root =>
{
    var path = Path.Combine(root, "fixture.toml");
    File.WriteAllText(path, "identifier = \"com.test.app\"\n\"quoted.key\" = \"ok\"\nmessage = \"\"\"first\nsecond\"\"\"\nwhen = 1979-05-27T07:32:00Z\n");
    var parsed = ConfigLoader.ParseFile(path);
    Equal("ok", parsed["quoted.key"]!.GetValue<string>()); Equal("first\nsecond", parsed["message"]!.GetValue<string>()); Equal("1979-05-27T07:32:00Z", parsed["when"]!.GetValue<string>()); return Task.CompletedTask;
});

static Task FullJson5Config() => Temp(root =>
{
    var path = Path.Combine(root, "fixture.json5");
    File.WriteAllText(path, "{ identifier: 'com.test.\\u0061pp', value: '\\x41\\\nB', /* between */ number: .5 }");
    var parsed = ConfigLoader.ParseFile(path); Equal("com.test.app", parsed["identifier"]!.GetValue<string>()); Equal("AB", parsed["value"]!.GetValue<string>()); Equal(0.5, parsed["number"]!.GetValue<double>()); return Task.CompletedTask;
});

static Task ParsedSyntaxStillValidated() => Temp(root =>
{
    File.WriteAllText(Path.Combine(root, "netwiv.conf.json5"), "{ identifier: 'com.test.app', parserFixtureOnly: true }");
    Throws<NetWivConfigException>(() => ConfigLoader.Load(root, "windows")); return Task.CompletedTask;
});

static Task TargetMapping()
{
    Equal("win-x64", BuildTarget.Resolve("x86_64-pc-windows-msvc").RuntimeIdentifier);
    Equal("linux-x64", BuildTarget.Resolve("linux-x64").RuntimeIdentifier);
    Throws<PlatformNotSupportedException>(() => BuildTarget.Resolve("win-arm64")); return Task.CompletedTask;
}

static Task PayloadStaging() => Temp(root =>
{
    Directory.CreateDirectory(Path.Combine(root, "a")); Directory.CreateDirectory(Path.Combine(root, "b")); Directory.CreateDirectory(Path.Combine(root, "bundle", "old"));
    File.WriteAllText(Path.Combine(root, "a", "same.txt"), "a"); File.WriteAllText(Path.Combine(root, "b", "same.txt"), "b"); File.WriteAllText(Path.Combine(root, "bundle", "old", "bad.txt"), "bad");
    var destination = Path.Combine(root, "bundle", "test", "payload"); var copied = PayloadStager.Stage(root, destination);
    Equal(2, copied.Count); Equal("a", File.ReadAllText(Path.Combine(destination, "a", "same.txt"))); Equal("b", File.ReadAllText(Path.Combine(destination, "b", "same.txt"))); True(!File.Exists(Path.Combine(destination, "bundle", "old", "bad.txt")));
    if (OperatingSystem.IsWindows())
    {
        var mixedCase = Path.Combine(root, "Bundle", "test", "payload");
        copied = PayloadStager.Stage(root, mixedCase);
        Equal(2, copied.Count); Equal("a", File.ReadAllText(Path.Combine(mixedCase, "a", "same.txt")));
    }
    var external = root + "-external";
    try { copied = PayloadStager.Stage(root, external); Equal(2, copied.Count); Equal("a", File.ReadAllText(Path.Combine(external, "a", "same.txt"))); }
    finally { if (Directory.Exists(external)) Directory.Delete(external, recursive: true); }
    Throws<ArgumentException>(() => PayloadStager.Stage(root, Path.Combine(root, "staging")));
    Throws<ArgumentException>(() => PayloadStager.Stage(root, root));
    Throws<ArgumentException>(() => PayloadStager.Stage(root, Path.GetDirectoryName(root)!));
    return Task.CompletedTask;
});

static Task PayloadStagingExcludedLinks() => Temp(root =>
{
    if (OperatingSystem.IsWindows())
    {
        Console.WriteLine("SKIP directory symlink fixture: exercised on Unix without elevated privileges");
        return Task.CompletedTask;
    }
    var outside = Directory.CreateTempSubdirectory("netwiv-staging-links-").FullName;
    var destination = Directory.CreateTempSubdirectory("netwiv-staging-output-").FullName;
    var blocked = Directory.CreateDirectory(Path.Combine(outside, "unreadable")).FullName;
    var bundle = Path.Combine(root, "bundle");
    try
    {
        File.WriteAllText(Path.Combine(root, "app.bin"), "application");
        File.WriteAllText(Path.Combine(outside, "outside.txt"), "must not be visited");
        Directory.CreateSymbolicLink(Path.Combine(outside, "cycle"), outside);
        // A cycle alone can be silently truncated by the OS. An unreadable
        // directory makes accidental traversal fail deterministically.
        File.SetUnixFileMode(blocked, UnixFileMode.None);
        Throws<UnauthorizedAccessException>(() => Directory.GetFileSystemEntries(blocked));
        Directory.CreateDirectory(Path.Combine(bundle, "dmg", "payload"));
        // The DMG Applications shortcut can lead to cyclic links in installed apps.
        Directory.CreateSymbolicLink(Path.Combine(bundle, "dmg", "payload", "Applications"), outside);
        var copied = PayloadStager.Stage(root, destination);
        True(copied.SequenceEqual(new[] { "app.bin" }));
        Equal("application", File.ReadAllText(Path.Combine(destination, "app.bin")));
        True(!File.Exists(Path.Combine(destination, "outside.txt")));

        Directory.Delete(bundle, true);
        Directory.CreateSymbolicLink(bundle, outside);
        copied = PayloadStager.Stage(root, destination);
        True(copied.SequenceEqual(new[] { "app.bin" }));
        Equal("must not be visited", File.ReadAllText(Path.Combine(outside, "outside.txt")));
    }
    finally
    {
        File.SetUnixFileMode(blocked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        if (Directory.Exists(bundle)) Directory.Delete(bundle, true);
        Directory.Delete(outside, true);
        Directory.Delete(destination, true);
    }
    return Task.CompletedTask;
});

static Task BundleMarkerPatch() => Temp(root =>
{
    const string marker = "__NETWIV_BUNDLE_TYPE_VAR_UNK";
    var binary = Path.Combine(root, "app.bin");
    File.WriteAllBytes(binary, System.Text.Encoding.ASCII.GetBytes("prefix" + marker + "suffix"));
    var length = new FileInfo(binary).Length;
    NativeBundler.PatchBundleType(binary, "MSI");
    Equal(length, new FileInfo(binary).Length);
    Equal("prefix__NETWIV_BUNDLE_TYPE_VAR_MSIsuffix", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(binary)));
    Throws<InvalidOperationException>(() => NativeBundler.PatchBundleType(binary, "DEB"));
    File.WriteAllText(binary, marker + marker);
    Throws<InvalidOperationException>(() => NativeBundler.PatchBundleType(binary, "RPM"));
    Throws<ArgumentException>(() => NativeBundler.PatchBundleType(binary, "bad"));
    var runtimeConfig = Path.Combine(root, (OperatingSystem.IsWindows() ? "app" : "app.bin") + ".runtimeconfig.json");
    File.WriteAllText(runtimeConfig, "{}");
    Throws<FileNotFoundException>(() => NativeBundler.PatchBundleType(binary, "MAC"));
    var core = Path.Combine(root, "NetWiv.Core.dll");
    File.WriteAllText(core, "no marker");
    Throws<InvalidOperationException>(() => NativeBundler.PatchBundleType(binary, "MAC"));
    File.WriteAllText(core, marker + marker);
    Throws<InvalidOperationException>(() => NativeBundler.PatchBundleType(binary, "MAC"));
    File.WriteAllText(core, marker);
    NativeBundler.PatchBundleType(binary, "MAC");
    Equal("__NETWIV_BUNDLE_TYPE_VAR_MAC", File.ReadAllText(core));
    Equal(marker + marker, File.ReadAllText(binary));
    Equal(@"C:\price${U+24}${U+24}\payload", NativeBundler.EscapeNsis(@"C:\price$\payload"));
    return Task.CompletedTask;
});

static Task PluginConfiguration() => Temp(root =>
{
    var path = Path.Combine(root, "netwiv.conf.json");
    File.WriteAllText(path, """{"plugins":{"sample":{"count":2,"message":"base"},"scalar":[1,true]}}""");
    var config = ConfigLoader.Load(root, "windows", ["""{"plugins":{"sample":{"count":42}}}"""]);
    Equal(42, config.Root["plugins"]!["sample"]!["count"]!.GetValue<int>());
    Equal("base", config.Root["plugins"]!["sample"]!["message"]!.GetValue<string>());
    Equal(2, ((System.Text.Json.Nodes.JsonArray)config.Root["plugins"]!["scalar"]!).Count);
    foreach (var invalid in new[] { "null", "[]", "\"invalid\"", "42" })
    {
        File.WriteAllText(path, "{\"plugins\":" + invalid + "}");
        Throws<NetWivConfigException>(() => ConfigLoader.Load(root, "windows"));
    }
    return Task.CompletedTask;
});

static async Task ProcessCancellation()
{
    using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
    var spec = OperatingSystem.IsWindows() ? new ProcessSpec("powershell.exe", ["-NoProfile", "-Command", "Start-Sleep -Seconds 30"], Directory.GetCurrentDirectory()) : new ProcessSpec("/bin/sh", ["-c", "sleep 30"], Directory.GetCurrentDirectory());
    var started = DateTime.UtcNow; await ThrowsAsync<OperationCanceledException>(() => ProcessRunner.RunAsync(spec, cancellation.Token)); True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
}

static Task InitLayout() => Temp(async root =>
{
    var repo = Directory.GetCurrentDirectory(); var stdout = new StringWriter(); var stderr = new StringWriter();
    var exit = await CliApp.RunAsync(["init", "--ci", "--directory", root, "--app-name", "Sample", "--netwiv-source", repo], stdout, stderr, repo);
    Equal(0, exit, stderr.ToString()); var dir = Path.Combine(root, "src-netwiv"); True(File.Exists(Path.Combine(dir, "netwiv.conf.json"))); var project = File.ReadAllText(Path.Combine(dir, "NetWiv.App.csproj")); True(!project.Contains("<PublishAot>", StringComparison.Ordinal)); True(project.Contains("NetWiv.Core.csproj", StringComparison.Ordinal)); True(project.Contains("NetWiv.Platform.Desktop.csproj", StringComparison.Ordinal)); True(project.Contains("NetWiv.Generators.csproj", StringComparison.Ordinal)); True(File.ReadAllText(Path.Combine(dir, "Program.cs")).Contains("UseEmbeddedAssets", StringComparison.Ordinal));
});

static Task InitPackages() => Temp(async root =>
{
    var output = new StringWriter(); var error = new StringWriter();
    const string title = "first\nsecond \"quoted\"";
    Equal(0, await CliApp.RunAsync(["init", "--ci", "--directory", root, "--window-title", title], output, error, Directory.GetCurrentDirectory()), error.ToString());
    var dir = Path.Combine(root, "src-netwiv");
    var project = File.ReadAllText(Path.Combine(dir, "NetWiv.App.csproj"));
    True(project.Contains($"<NetWivVersion>{CliApp.Version}</NetWivVersion>", StringComparison.Ordinal));
    True(project.Contains("NetWiv.Platform.Desktop", StringComparison.Ordinal) && project.Contains("NetWiv.Generators", StringComparison.Ordinal));
    True(!project.Contains("ProjectReference", StringComparison.Ordinal) && !project.Contains("__", StringComparison.Ordinal));
    var config = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "netwiv.conf.json")))!;
    Equal(title, config["app"]!["windows"]![0]!["title"]!.GetValue<string>());
    var source = File.ReadAllText(Path.Combine(dir, "Program.cs"));
    True(source.Contains("new DesktopRuntime()", StringComparison.Ordinal) && source.Contains("GenerateFrontendBindings", StringComparison.Ordinal));
    True(File.ReadAllText(Path.Combine(dir, "README.md")).Contains("not affiliated with, endorsed by, or officially approved by", StringComparison.Ordinal));
    True(source.Contains("#if NETWIV_DEV", StringComparison.Ordinal));
});

static Task PluginScaffold() => Temp(async root =>
{
    var repo = Directory.GetCurrentDirectory(); var output = new StringWriter(); var error = new StringWriter();
    var exit = await CliApp.RunAsync(["plugin", "new", "sample-tools", "--directory", root, "--netwiv-source", repo], output, error, repo);
    Equal(0, exit, error.ToString());
    var source = File.ReadAllText(Path.Combine(root, "Plugin.cs"));
    True(source.Contains("[Plugin]", StringComparison.Ordinal));
    True(source.Contains("RegisterGeneratedCommands<SampleToolsPlugin>(\"ping\")", StringComparison.Ordinal));
    True(source.Contains("[Command]", StringComparison.Ordinal));
    True(source.Contains("new PermissionDefinition(\"default\", \"ping\")", StringComparison.Ordinal));
    True(source.Contains("JsonValue.From(\"pong\")", StringComparison.Ordinal));
    var project = File.ReadAllText(Path.Combine(root, "NetWiv.Plugin.csproj"));
    True(project.Contains("NetWiv.Generators.csproj", StringComparison.Ordinal));
    True(project.Contains("OutputItemType=\"Analyzer\"", StringComparison.Ordinal));
    True(!project.Contains("PackageReference", StringComparison.Ordinal));
    True(!project.Contains("__", StringComparison.Ordinal));
    True(File.ReadAllText(Path.Combine(root, "README.md")).Contains("TAURI®", StringComparison.Ordinal));
});

static Task PluginNuGetScaffold() => Temp(async root =>
{
    var output = new StringWriter(); var error = new StringWriter();
    Equal(0, await CliApp.RunAsync(["plugin", "new", "7-tools", "--directory", root, "--author", "A&B <team>"], output, error, Directory.GetCurrentDirectory()), error.ToString());
    var project = System.Xml.Linq.XDocument.Load(Path.Combine(root, "NetWiv.Plugin.csproj"));
    var packages = project.Descendants("PackageReference").ToArray();
    Equal(2, packages.Length);
    foreach (var package in packages) Equal(CliApp.Version, package.Attribute("Version")!.Value);
    Equal("A&B <team>", project.Descendants("Authors").Single().Value);
    Equal("NetWiv.Plugin.7Tools", project.Descendants("PackageId").Single().Value);
    True(!project.Descendants("ProjectReference").Any());
    True(!project.ToString().Contains("__", StringComparison.Ordinal));
    True(File.ReadAllText(Path.Combine(root, "Plugin.cs")).Contains("class _7ToolsPlugin", StringComparison.Ordinal));
    var conflict = Path.Combine(root, "conflict"); Directory.CreateDirectory(conflict);
    File.WriteAllText(Path.Combine(conflict, "Plugin.cs"), "existing source");
    Equal(1, await CliApp.RunAsync(["plugin", "init", "sample", "--directory", conflict], output, error, root));
    Equal("existing source", File.ReadAllText(Path.Combine(conflict, "Plugin.cs")));
    True(!File.Exists(Path.Combine(conflict, "NetWiv.Plugin.csproj")));
    var invalid = Path.Combine(root, "invalid");
    Equal(1, await CliApp.RunAsync(["plugin", "new", "sample", "--directory", invalid, "--netwiv-source", "not-a-source-tree"], output, error, root));
    True(!Directory.EnumerateFiles(invalid, "*", SearchOption.AllDirectories).Any());
});

static Task EffectiveConfig() => Temp(root =>
{
    var dir = Path.Combine(root, "src-netwiv"); Directory.CreateDirectory(Path.Combine(dir, "capabilities"));
    File.WriteAllText(Path.Combine(dir, "netwiv.conf.json"), "{\"identifier\":\"com.test.app\",\"app\":{\"security\":{\"capabilities\":[\"main\"]}}}");
    File.WriteAllText(Path.Combine(dir, "capabilities", "main.json"), "{\"identifier\":\"main\",\"windows\":[\"main\"],\"permissions\":[\"core:default\"]}");
    var config = ConfigLoader.Load(root, "windows"); var target = BuildTarget.Resolve("win-x64"); var path = CommandConfig.WriteEffectiveConfig(config, target, false, "http://127.0.0.1:1430"); var effective = ConfigLoader.ParseFile(path);
    Equal("main", effective["app"]!["security"]!["capabilities"]![0]!["identifier"]!.GetValue<string>());
    Equal("http://127.0.0.1:1430", effective["build"]!["devUrl"]!.GetValue<string>()); return Task.CompletedTask;
});

static Task CapabilityFileDe() => Temp(root =>
{
    var dir = Path.Combine(root, "src-netwiv"); Directory.CreateDirectory(Path.Combine(dir, "capabilities"));
    File.WriteAllText(Path.Combine(dir, "netwiv.conf.json"), """{"identifier":"com.test.app","app":{"security":{"capabilities":["test"]}}}""");
    const string capability = """{"identifier":"test","description":"","local":true,"windows":[],"webviews":[],"permissions":[],"remote":null,"platforms":null}""";
    foreach (var input in new[] { capability, "[" + capability + "]", "{\"capabilities\":[" + capability + "]}" })
    {
        File.WriteAllText(Path.Combine(dir, "capabilities", "test.json"), input);
        var config = ConfigLoader.Load(root, "windows");
        var path = CommandConfig.WriteEffectiveConfig(config, BuildTarget.Resolve("win-x64"), false);
        var expanded = ConfigLoader.ParseFile(path)["app"]!["security"]!["capabilities"]!.AsArray();
        Equal(1, expanded.Count);
        True(System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(capability), expanded[0]));
    }
    return Task.CompletedTask;
});

static Task CapabilityInputBoundaries() => Temp(root =>
{
    var dir = Path.Combine(root, "src-netwiv"); Directory.CreateDirectory(Path.Combine(dir, "capabilities"));
    File.WriteAllText(Path.Combine(dir, "netwiv.conf.json"), """{"identifier":"com.test.app","app":{"security":{"capabilities":["test"]}}}""");
    foreach (var input in new[] {
        """{"identifier":"test","platforms":["linux"],"permissions":["plugin:bad_name"]}""",
        """{"identifier":"test","permissions":[{"identifier":"core:default","allow":{}}]}""",
        """{"identifier":"test","permissions":[],"local":null}""",
        """{"identifier":"test","permissions":[],"windows":[1]}""",
        """{"identifier":"test","permissions":[],"remote":{}}""",
        """{"identifier":"test"}""", """{"capabilities":null}""", "[null]" })
    {
        File.WriteAllText(Path.Combine(dir, "capabilities", "test.json"), input);
        var config = ConfigLoader.Load(root, "windows");
        Throws<NetWivConfigException>(() => CommandConfig.WriteEffectiveConfig(config, BuildTarget.Resolve("win-x64"), false));
    }
    return Task.CompletedTask;
});

static Task EffectivePermissionDiscovery() => Temp(root =>
{
    var dir = Path.Combine(root, "src-netwiv");
    Directory.CreateDirectory(Path.Combine(dir, "capabilities"));
    Directory.CreateDirectory(Path.Combine(dir, "permissions", "nested"));
    Directory.CreateDirectory(Path.Combine(dir, "permissions", "schemas"));
    File.WriteAllText(Path.Combine(dir, "netwiv.conf.json"), "{\"identifier\":\"com.test.app\",\"app\":{\"security\":{\"capabilities\":[\"main\"]}}}");
    File.WriteAllText(Path.Combine(dir, "capabilities", "main.json"), "{\"identifier\":\"main\",\"windows\":[\"main\"],\"permissions\":[\"read-set\"]}");
    File.WriteAllText(Path.Combine(dir, "permissions", "allow-read.json"), "{\"permission\":[{\"identifier\":\"allow-read\",\"description\":\"read\",\"commands\":{\"allow\":[\"read_file\"]},\"scope\":{\"allow\":[{\"path\":\"$HOME/docs/**\"}]},\"platforms\":[\"windows\"]}]}");
    File.WriteAllText(Path.Combine(dir, "permissions", "nested", "set.toml"), "[[set]]\nidentifier = \"read-set\"\ndescription = \"read files\"\npermissions = [\"allow-read\"]\n");
    File.WriteAllText(Path.Combine(dir, "permissions", "schemas", "schema.json"), "not json");
    File.WriteAllText(Path.Combine(dir, "permissions", "ignored.json5"), "{ permission: [{ identifier: 'ignored' }] }");

    var config = ConfigLoader.Load(root, "windows");
    var path = CommandConfig.WriteEffectiveConfig(config, BuildTarget.Resolve("win-x64"), false);
    var acl = ConfigLoader.ParseFile(path)["$netwivAcl"]!;
    Equal(1, acl["version"]!.GetValue<int>());
    Equal(true, acl["hasAppManifest"]!.GetValue<bool>());
    Equal(2, acl["files"]!.AsArray().Count);
    Equal("allow-read", acl["files"]![0]!["permission"]![0]!["identifier"]!.GetValue<string>());
    Equal("$HOME/docs/**", acl["files"]![0]!["permission"]![0]!["scope"]!["allow"]![0]!["path"]!.GetValue<string>());
    Equal("windows", acl["files"]![0]!["permission"]![0]!["platforms"]![0]!.GetValue<string>());
    Equal("read-set", acl["files"]![1]!["set"]![0]!["identifier"]!.GetValue<string>());
    return Task.CompletedTask;
});

static Task EmptyEffectivePermissions() => Temp(root =>
{
    var dir = Path.Combine(root, "src-netwiv");
    Directory.CreateDirectory(Path.Combine(dir, "permissions", "schemas"));
    File.WriteAllText(Path.Combine(dir, "netwiv.conf.json"), "{\"identifier\":\"com.test.app\"}");
    File.WriteAllText(Path.Combine(dir, "permissions", "schemas", "schema.json"), "not json");
    File.WriteAllText(Path.Combine(dir, "permissions", "ignored.json5"), "{ permission: [{ identifier: 'ignored' }] }");
    var config = ConfigLoader.Load(root, "windows");
    var effective = ConfigLoader.ParseFile(CommandConfig.WriteEffectiveConfig(config, BuildTarget.Resolve("win-x64"), false));
    Equal(false, effective["$netwivAcl"]!["hasAppManifest"]!.GetValue<bool>());
    Equal(0, effective["$netwivAcl"]!["files"]!.AsArray().Count);
    return Task.CompletedTask;
});

static Task EffectivePermissionSets() => Temp(root =>
{
    var dir = Path.Combine(root, "src-netwiv");
    Directory.CreateDirectory(Path.Combine(dir, "permissions"));
    File.WriteAllText(Path.Combine(dir, "netwiv.conf.json"), "{\"identifier\":\"com.test.app\",\"app\":{\"security\":{\"capabilities\":[{\"identifier\":\"main\",\"windows\":[\"main\"],\"permissions\":[\"default\",{\"identifier\":\"read-set\",\"allow\":[{\"path\":\"one\"}]}]}]}}}");
    File.WriteAllText(Path.Combine(dir, "permissions", "default.toml"), "[default]\ndescription = \"default app permissions\"\npermissions = [\"read-set\"]\n\n[[set]]\nidentifier = \"read-set\"\ndescription = \"read\"\npermissions = [\"allow-read\", \"sample:default\"]\n\n[[permission]]\nidentifier = \"allow-read\"\ndescription = \"read\"\nplatforms = [\"windows\"]\n[permission.commands]\nallow = [\"read_file\"]\n[permission.scope]\nallow = [{ path = \"$HOME/**\" }]\n");
    var config = ConfigLoader.Load(root, "windows");
    var acl = ConfigLoader.ParseFile(CommandConfig.WriteEffectiveConfig(config, BuildTarget.Resolve("win-x64"), false))["$netwivAcl"]!;
    Equal(true, acl["hasAppManifest"]!.GetValue<bool>());
    Equal("read-set", acl["files"]![0]!["default"]!["permissions"]![0]!.GetValue<string>());
    Equal("sample:default", acl["files"]![0]!["set"]![0]!["permissions"]![1]!.GetValue<string>());
    Equal("$HOME/**", acl["files"]![0]!["permission"]![0]!["scope"]!["allow"]![0]!["path"]!.GetValue<string>());
    return Task.CompletedTask;
});

static Task UnknownEffectivePermission() => Temp(root =>
{
    var dir = Path.Combine(root, "src-netwiv");
    Directory.CreateDirectory(Path.Combine(dir, "capabilities"));
    Directory.CreateDirectory(Path.Combine(dir, "permissions"));
    File.WriteAllText(Path.Combine(dir, "netwiv.conf.json"), "{\"identifier\":\"com.test.app\",\"app\":{\"security\":{\"capabilities\":[\"active\",\"inactive\"]}}}");
    File.WriteAllText(Path.Combine(dir, "permissions", "known.json"), "{\"permission\":[{\"identifier\":\"known\"}]}");
    File.WriteAllText(Path.Combine(dir, "capabilities", "active.json"), "{\"identifier\":\"active\",\"windows\":[\"main\"],\"permissions\":[\"missing\",\"core:default\"]}");
    File.WriteAllText(Path.Combine(dir, "capabilities", "inactive.json"), "{\"identifier\":\"inactive\",\"platforms\":[\"linux\"],\"windows\":[\"main\"],\"permissions\":[\"also-missing\"]}");
    var config = ConfigLoader.Load(root, "windows");
    var message = ThrowsMessage<NetWivConfigException>(() => CommandConfig.WriteEffectiveConfig(config, BuildTarget.Resolve("win-x64"), false));
    True(message.Contains("active.json", StringComparison.Ordinal));
    True(message.Contains("missing", StringComparison.Ordinal));
    True(!message.Contains("also-missing", StringComparison.Ordinal));
    return Task.CompletedTask;
});

static Task EffectivePermissionCycle() => Temp(root =>
{
    var dir = Path.Combine(root, "src-netwiv"); Directory.CreateDirectory(Path.Combine(dir, "permissions"));
    File.WriteAllText(Path.Combine(dir, "netwiv.conf.json"), "{\"identifier\":\"com.test.app\",\"app\":{\"security\":{\"capabilities\":[{\"identifier\":\"main\",\"windows\":[\"main\"],\"permissions\":[\"a\"]}]}}}");
    File.WriteAllText(Path.Combine(dir, "permissions", "cycle.json"), "{\"set\":[{\"identifier\":\"a\",\"description\":\"a\",\"permissions\":[\"b\"]},{\"identifier\":\"b\",\"description\":\"b\",\"permissions\":[\"a\"]}]}");
    var config = ConfigLoader.Load(root, "windows");
    var message = ThrowsMessage<NetWivConfigException>(() => CommandConfig.WriteEffectiveConfig(config, BuildTarget.Resolve("win-x64"), false));
    True(message.Contains("a -> b -> a", StringComparison.Ordinal));
    True(message.Contains("cycle.json", StringComparison.Ordinal));
    return Task.CompletedTask;
});

static Task DuplicateEffectivePermission() => Temp(root =>
{
    var dir = Path.Combine(root, "src-netwiv"); Directory.CreateDirectory(Path.Combine(dir, "permissions"));
    File.WriteAllText(Path.Combine(dir, "netwiv.conf.json"), "{\"identifier\":\"com.test.app\",\"app\":{\"security\":{\"capabilities\":[{\"identifier\":\"main\",\"windows\":[\"main\"],\"permissions\":[\"same\"]}]}}}");
    File.WriteAllText(Path.Combine(dir, "permissions", "first.json"), "{\"set\":[{\"identifier\":\"same\",\"description\":\"replaced\",\"permissions\":[\"missing\"]}],\"permission\":[{\"identifier\":\"same\"},{\"identifier\":\"leaf\"}]}");
    File.WriteAllText(Path.Combine(dir, "permissions", "second.toml"), "[[set]]\nidentifier = \"same\"\ndescription = \"last set wins\"\npermissions = [\"leaf\"]\n\n[[permission]]\nidentifier = \"same\"\n");
    var config = ConfigLoader.Load(root, "windows");
    var acl = ConfigLoader.ParseFile(CommandConfig.WriteEffectiveConfig(config, BuildTarget.Resolve("win-x64"), false))["$netwivAcl"]!;
    Equal(2, acl["files"]!.AsArray().Count);
    Equal("missing", acl["files"]![0]!["set"]![0]!["permissions"]![0]!.GetValue<string>());
    Equal("leaf", acl["files"]![1]!["set"]![0]!["permissions"]![0]!.GetValue<string>());
    return Task.CompletedTask;
});

static Task MalformedEffectivePermission() => Temp(root =>
{
    var dir = Path.Combine(root, "src-netwiv"); Directory.CreateDirectory(Path.Combine(dir, "permissions"));
    File.WriteAllText(Path.Combine(dir, "netwiv.conf.json"), "{\"identifier\":\"com.test.app\"}");
    File.WriteAllText(Path.Combine(dir, "permissions", "bad.json"), "{\"permission\":[{\"identifier\":\"bad\",\"commands\":{\"allow\":\"read_file\"}}]}");
    var config = ConfigLoader.Load(root, "windows");
    var message = ThrowsMessage<NetWivConfigException>(() => CommandConfig.WriteEffectiveConfig(config, BuildTarget.Resolve("win-x64"), false));
    True(message.Contains("bad.json", StringComparison.Ordinal));
    True(message.Contains("permission[0].commands.allow", StringComparison.Ordinal));
    return Task.CompletedTask;
});

static Task UnsupportedFlags() => Temp(async root =>
{
    File.WriteAllText(Path.Combine(root, "netwiv.conf.json"), "{\"identifier\":\"com.test.app\"}");
    var error = new StringWriter(); var exit = await CliApp.RunAsync(["dev", "--exit-on-panic"], TextWriter.Null, error, root); Equal(1, exit); True(error.ToString().Contains("no .NET equivalent", StringComparison.Ordinal));
    error = new StringWriter(); exit = await CliApp.RunAsync(["build", "--ignore-version-mismatches"], TextWriter.Null, error, root); Equal(1, exit); True(error.ToString().Contains("cannot be honored", StringComparison.Ordinal));
});

static Task IsolationWatchFolders() => Temp(root =>
{
    var configuration = Path.Combine(root, "src-netwiv");
    var isolation = Path.Combine(root, "secure");
    var other = Path.Combine(root, "other");
    Directory.CreateDirectory(configuration); Directory.CreateDirectory(isolation); Directory.CreateDirectory(other);
    File.WriteAllText(Path.Combine(configuration, "netwiv.conf.json"), """{"identifier":"com.test.isolation","app":{"security":{"pattern":{"use":"isolation","options":{"dir":"../secure"}}}}}""");
    var config = ConfigLoader.Load(root, "windows");
    Equal(isolation, CommandConfig.IsolationDirectory(config));
    Equal(isolation, DevCommand.ResolveWatchFolders(config, []).Single());
    var watched = DevCommand.ResolveWatchFolders(config, ["../other", "../secure"]);
    Equal(2, watched.Count); Equal(isolation, watched[0]); Equal(other, watched[1]);
    Directory.Delete(isolation);
    Throws<DirectoryNotFoundException>(() => DevCommand.ResolveWatchFolders(config, ["../other"]));
    return Task.CompletedTask;
});

static Task AdditionalWatchFolders() => Temp(root =>
{
    var dir = Path.Combine(root, "src-netwiv"); var configured = Path.Combine(root, "configured"); var selected = Path.Combine(root, "selected");
    Directory.CreateDirectory(dir); Directory.CreateDirectory(configured); Directory.CreateDirectory(selected);
    File.WriteAllText(Path.Combine(dir, "netwiv.conf.json"), "{\"identifier\":\"com.test.app\",\"build\":{\"additionalWatchFolders\":[\"../configured\"]}}");
    var config = ConfigLoader.Load(root, "windows");
    Equal(configured, DevCommand.ResolveWatchFolders(config, []).Single());
    Equal(selected, DevCommand.ResolveWatchFolders(config, ["../selected"]).Single());
    Throws<DirectoryNotFoundException>(() => DevCommand.ResolveWatchFolders(config, ["../missing"]));
    File.WriteAllText(Path.Combine(dir, "netwiv.conf.json"), "{\"identifier\":\"com.test.app\",\"build\":{\"additionalWatchFolders\":[1]}}");
    Throws<NetWivConfigException>(() => ConfigLoader.Load(root, "windows"));
    return Task.CompletedTask;
});

static Task BuiltInDevelopmentServer() => Temp(async root =>
{
    File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><body>hello</body>");
    File.WriteAllText(Path.Combine(root, "about.html"), "<!doctype html><body>about</body>");
    File.WriteAllText(Path.Combine(root, "app.js"), "console.log('ready')");
    var port = FreePort();
    await using var server = BuiltInDevServer.Start(root, port);
    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    var html = await client.GetStringAsync(server.Url + "/");
    True(html.Contains("hello", StringComparison.Ordinal));
    True(html.Contains("/.netwiv/reload.js", StringComparison.Ordinal));
    using var script = await client.GetAsync(server.Url + "/app.js");
    Equal("text/javascript", script.Content.Headers.ContentType!.MediaType);
    True((await client.GetStringAsync(server.Url + "/about")).Contains("about", StringComparison.Ordinal));
    True((await client.GetStringAsync(server.Url + "/client/route")).Contains("hello", StringComparison.Ordinal));

    using var reload = await client.GetAsync(server.Url + "/.netwiv/reload", HttpCompletionOption.ResponseHeadersRead);
    using var reader = new StreamReader(await reload.Content.ReadAsStreamAsync());
    Equal("retry: 250", await reader.ReadLineAsync());
    await reader.ReadLineAsync();
    File.WriteAllText(Path.Combine(root, "app.js"), "console.log('changed')");
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    Equal("event: reload", await reader.ReadLineAsync(timeout.Token));

    var occupiedPort = FreePort();
    var occupied = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, occupiedPort); occupied.Start();
    try
    {
        await using var fallback = BuiltInDevServer.Start(root, occupiedPort, autoPort: true);
        True(!fallback.Url.EndsWith(":" + occupiedPort, StringComparison.Ordinal));
    }
    finally { occupied.Stop(); }
});

static Task DevelopmentServerPort()
{
    Equal(1430, DevCommand.ResolveServerPort(null, null));
    Equal(4321, DevCommand.ResolveServerPort(null, "4321"));
    Equal(5678, DevCommand.ResolveServerPort("5678", "4321"));
    Throws<ArgumentException>(() => DevCommand.ResolveServerPort("0", null));
    Throws<ArgumentException>(() => DevCommand.ResolveServerPort("invalid", null));
    True(!DevCommand.ResolveNoDevServerWait(false, null));
    True(DevCommand.ResolveNoDevServerWait(false, "true"));
    True(DevCommand.ResolveNoDevServerWait(false, "1"));
    True(!DevCommand.ResolveNoDevServerWait(false, "0"));
    Throws<ArgumentException>(() => DevCommand.ResolveNoDevServerWait(false, "invalid"));
    return Task.CompletedTask;
}

static Task TomlCapabilityEditing() => Temp(async root =>
{
    var configDirectory = Path.Combine(root, "src-netwiv"); Directory.CreateDirectory(configDirectory);
    File.WriteAllText(Path.Combine(configDirectory, "netwiv.conf.json"), "{\"identifier\":\"com.test.app\"}");
    var output = new StringWriter(); var error = new StringWriter();
    var exit = await CliApp.RunAsync(["capability", "new", "default", "--format", "toml", "--description", "Desktop access", "--windows", "main,settings"], output, error, root);
    Equal(0, exit, error.ToString());
    var path = Path.Combine(configDirectory, "capabilities", "default.toml");
    True(ConfigLoader.ParseFile(path)["permissions"]!.AsArray().Any(node => node!.GetValue<string>() == "core:default"));
    File.WriteAllText(path, File.ReadAllText(path).Replace("permissions = [\"core:default\"]", "permissions = [{ identifier = \"scoped\", allow = [{ path = \"$APP/**\" }] }]", StringComparison.Ordinal));
    File.AppendAllText(path, "\n[remote]\nurls = [\"https://example.com\"]\n");
    exit = await CliApp.RunAsync(["permission", "add", "sample:default", "default"], output, error, root);
    Equal(0, exit, error.ToString());
    var capability = ConfigLoader.ParseFile(path);
    Equal("Desktop access", capability["description"]!.GetValue<string>());
    Equal("settings", capability["windows"]![1]!.GetValue<string>());
    True(capability["permissions"]!.AsArray().Any(node => node is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var item) && item == "sample:default"));
    Equal("scoped", capability["permissions"]![0]!["identifier"]!.GetValue<string>());
    Equal("https://example.com", capability["remote"]!["urls"]![0]!.GetValue<string>());
    exit = await CliApp.RunAsync(["permission", "rm", "sample:default"], output, error, root);
    Equal(0, exit, error.ToString());
    capability = ConfigLoader.ParseFile(path);
    True(!capability["permissions"]!.AsArray().Any(node => node is System.Text.Json.Nodes.JsonValue value && value.TryGetValue<string>(out var item) && item == "sample:default"));
    Equal("https://example.com", capability["remote"]!["urls"]![0]!.GetValue<string>());
});

// Empty-password vectors: tauri-cli/src/helpers/updater_signature.rs.
// Copyright 2019-2024 Tauri Programme within The Commons Conservancy.
// SPDX-License-Identifier: Apache-2.0 OR MIT
static Task SignerCompatibility() => Temp(async root =>
{
    async Task<int> RunSigner(string[] arguments, TextWriter output, TextWriter error)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        if (Environment.GetEnvironmentVariable("NETWIV_CLI_UNDER_TEST") is not { } cli)
            return await CliApp.RunAsync(arguments, output, error, root, timeout.Token);
        var result = await ProcessRunner.RunAsync(new ProcessSpec(cli, arguments, root, CaptureOutput: true), timeout.Token);
        await output.WriteAsync(result.StandardOutput); await error.WriteAsync(result.StandardError);
        return result.ExitCode;
    }
    var minisign = FindMinisign();
    var previous = Environment.GetEnvironmentVariable("NETWIV_MINISIGN_PATH");
    Environment.SetEnvironmentVariable("NETWIV_MINISIGN_PATH", minisign);
    try
    {
        var output = new StringWriter(); var error = new StringWriter();
        var exit = await RunSigner(["signer", "generate", "--password=", "--write-keys", "test.key"], output, error);
        Equal(0, exit, error.ToString());
        var privatePath = Path.Combine(root, "test.key"); var publicPath = privatePath + ".pub";
        True(File.Exists(privatePath)); True(File.Exists(publicPath));
        var rawPrivate = DecodeTauriBox(File.ReadAllText(privatePath)); var rawPublic = DecodeTauriBox(File.ReadAllText(publicPath));
        True(rawPrivate.StartsWith("untrusted comment: minisign encrypted secret key", StringComparison.Ordinal));
        True(rawPublic.StartsWith("untrusted comment: minisign public key", StringComparison.Ordinal));

        var payload = Path.Combine(root, "payload.bin"); File.WriteAllText(payload, "TAURI");
        output = new StringWriter(); error = new StringWriter();
        exit = await RunSigner(["signer", "sign", "--private-key-path", privatePath, "--password=", payload], output, error);
        Equal(0, exit, error.ToString());
        var tauriSignaturePath = payload + ".sig"; True(File.Exists(tauriSignaturePath));
        var rawPublicPath = Path.Combine(root, "raw.pub"); var rawSignaturePath = Path.Combine(root, "raw.minisig");
        var signature = DecodeTauriBox(File.ReadAllText(tauriSignaturePath));
        True(signature.StartsWith("untrusted comment: signature from NetWiv secret key", StringComparison.Ordinal));
        True(!signature.Contains("signature from tauri", StringComparison.OrdinalIgnoreCase));
        File.WriteAllText(rawPublicPath, rawPublic); File.WriteAllText(rawSignaturePath, signature);
        Equal(0, await RunProcess(minisign, ["-V", "-p", rawPublicPath, "-m", payload, "-x", rawSignaturePath]));

        // From tauri-cli 2.11.4 updater_signature.rs at the commit pinned in upstream-lock.json.
        const string upstreamPrivateKey = "dW50cnVzdGVkIGNvbW1lbnQ6IHJzaWduIGVuY3J5cHRlZCBzZWNyZXQga2V5ClJXUlRZMEl5dkpDN09RZm5GeVAzc2RuYlNzWVVJelJRQnNIV2JUcGVXZUplWXZXYXpqUUFBQkFBQUFBQUFBQUFBQUlBQUFBQTZrN2RnWGh5dURxSzZiL1ZQSDdNcktiaHRxczQwMXdQelRHbjRNcGVlY1BLMTBxR2dpa3I3dDE1UTVDRDE4MXR4WlQwa1BQaXdxKy9UU2J2QmVSNXhOQWFDeG1GSVllbUNpTGJQRkhhTnROR3I5RmdUZi90OGtvaGhJS1ZTcjdZU0NyYzhQWlQ5cGM9Cg==";
        var upstreamPrivatePath = Path.Combine(root, "upstream.key"); var upstreamPublicPath = Path.Combine(root, "upstream.pub");
        File.WriteAllText(upstreamPrivatePath, DecodeTauriBox(upstreamPrivateKey));
        Equal(0, await RunProcess(minisign, ["-R", "-s", upstreamPrivatePath, "-p", upstreamPublicPath], "\n"));
        output = new StringWriter(); error = new StringWriter();
        exit = await RunSigner(["signer", "sign", "--private-key", upstreamPrivateKey, "--password=", payload], output, error);
        Equal(0, exit, error.ToString());
        File.WriteAllText(rawSignaturePath, DecodeTauriBox(File.ReadAllText(tauriSignaturePath)));
        Equal(0, await RunProcess(minisign, ["-V", "-p", upstreamPublicPath, "-m", payload, "-x", rawSignaturePath]));
        File.WriteAllText(payload, "TAURI-modified");
        var tampered = await ProcessRunner.RunAsync(new ProcessSpec(minisign,
            ["-V", "-p", upstreamPublicPath, "-m", payload, "-x", rawSignaturePath], root, CaptureOutput: true));
        True(tampered.ExitCode != 0);
    }
    finally { Environment.SetEnvironmentVariable("NETWIV_MINISIGN_PATH", previous); }
});

static async Task UnknownCommand()
{
    var error = new StringWriter(); var exit = await CliApp.RunAsync(["wat"], TextWriter.Null, error, Directory.GetCurrentDirectory()); Equal(1, exit); True(error.ToString().Contains("Unknown command", StringComparison.Ordinal));
}

static async Task Temp(Func<string, Task> action)
{
    var path = Path.Combine(Path.GetTempPath(), "netwiv-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path);
    try { await action(path); } finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
}

static string FindMinisign()
{
    if (Environment.GetEnvironmentVariable("NETWIV_MINISIGN_PATH") is { Length: > 0 } configured && File.Exists(configured)) return Path.GetFullPath(configured);
    var architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture == System.Runtime.InteropServices.Architecture.Arm64 ? "aarch64" : "x86_64";
    var platform = OperatingSystem.IsWindows() ? "minisign-win64" : "minisign-linux";
    var executable = OperatingSystem.IsWindows() ? "minisign.exe" : "minisign";
    var path = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "tools", "minisign-0.12", platform, architecture, executable));
    if (!File.Exists(path)) throw new Exception($"Pinned minisign test tool was not found: {path}");
    return path;
}

static string DecodeTauriBox(string value) => System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value.Trim()));

static int FreePort()
{
    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

static async Task<int> RunProcess(string fileName, IReadOnlyList<string> arguments, string? input = null)
{
    using var process = new System.Diagnostics.Process { StartInfo = new System.Diagnostics.ProcessStartInfo(fileName) { UseShellExecute = false, RedirectStandardInput = input is not null, RedirectStandardOutput = true, RedirectStandardError = true } };
    foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
    process.Start();
    if (input is not null) { await process.StandardInput.WriteAsync(input); process.StandardInput.Close(); }
    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    if (process.ExitCode != 0) throw new Exception($"{fileName} failed ({process.ExitCode}): {await stdout}{await stderr}");
    return process.ExitCode;
}

static void Equal<T>(T expected, T actual, string? detail = null) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}. {detail}"); }
static void True(bool value) { if (!value) throw new Exception("Assertion failed."); }
static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
static string ThrowsMessage<T>(Action action) where T : Exception { try { action(); } catch (T exception) { return exception.Message; } throw new Exception($"Expected {typeof(T).Name}."); }
static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}."); }
