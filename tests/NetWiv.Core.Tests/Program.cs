using System.Collections.Concurrent;
using System.Text.Json;
using NetWiv;
using NetWiv.Runtime;
using NetWiv.Plugin.SingleInstance;

if (args is ["--shell-execution-probe", var shellRoot, var shellRole]) return await ShellLifetimeTests.Probe(shellRoot, shellRole);
if (args is ["--sql-migration-probe", var configRoot, var identifier, var role]) return await SqlMigrationProcessTests.Probe(configRoot, identifier, role);
if (args is ["--restart-probe", var restartRoot]) return RestartLifecycleTests.Probe(restartRoot);
if (args is ["--hosted-restart-probe", var hostedRoot]) return HostingTests.RestartProbe(hostedRoot);

if (args.Length >= 2 && args[0] == "--single-instance-child")
{
    using var child = new Builder().UseRuntime(new FakeRuntime())
        .Metadata(new AppMetadata("Child", "1.0.0", args[1]))
        .Plugin(new SingleInstancePlugin()).Build();
    child.Run();
    return 0;
}

GeneratedCommandRegistry.Register("generated_selected", static registry =>
    registry.Register("generated_selected", static (_, _, _) => ValueTask.FromResult(JsonValue.From("generated"))));

var tests = new List<(string Name, Func<Task> Run)>
{
    ("backend invocation selects immutable capabilities and preserves application denies", BackendAuthorizationTests.SelectedCapabilitiesAndApplicationDenies),
    ("backend invocation scoped context rejects guest channels and native forwarding", BackendAuthorizationTests.BackendContextAndNativeForwardingGuard),
    ("backend invocation merges permission and capability scopes", BackendAuthorizationTests.PermissionAndCapabilityScopesCombine),
    ("backend invocation retains unselected backend scope denies without adding allows", BackendAuthorizationTests.UnselectedBackendScopesContributeOnlyDenies),
    ("backend invocation scopes resources to tokens and cleans failed calls", BackendAuthorizationTests.ScopedResourcesRespectTokenAndFailureLifetime),
    ("backend invocation snapshots mutable ACL inputs and isolates handler scope changes", BackendAuthorizationTests.AuthorizationTokensSnapshotMutableAclInputs),
    ("backend invocation cleans failed resources and preserves binary response ownership", BackendAuthorizationTests.ScopedCleanupErrorsAndBinaryResponses),
    ("backend invocation scoped cancellation and shutdown close admission", BackendAuthorizationTests.ScopedInvocationCancellationAndShutdown),
    ("file path tickets require an explicit capability excluded from core defaults", FilePathPermissionTests.RequiresExplicitGrant),
    ("file path tickets enforce target origin and deny permission boundaries", FilePathPermissionTests.TargetOriginAndDenyBoundaries),
    ("macOS reopen delivers native-only notifications and observes application lifetime", ReopenTests.DeliveryAndLifetime),
    ("native opened delivers URL batches and observes resident application lifetime", OpenedTests.DeliveryAndLifetime),
    ("cooperative exit requests await handlers and preserve veto ordering", ExitRequestTests.ApprovalVetoAndOrdering),
    ("cooperative exit requests reject reentry and snapshot handler registrations", ExitRequestTests.ReentrancyAndHandlerSnapshot),
    ("cooperative exit requests cancel without stale exits and observe failures", ExitRequestTests.CancellationAndFailures),
    ("cooperative exit requests are invalidated by force exit and disposal", ExitRequestTests.ForceExitAndDisposalInvalidatePending),
    ("native opened observes sync and async failures and disposal", OpenedTests.FailuresAndDisposal),
    ("macOS reopen observes sync and async failures without blocking later handlers", ReopenTests.FailuresAndDisposal),
    ("legacy JSON metadata accepts nullable command members without relaxing value types", LegacyJsonTests.NullableMembers),
    ("runtime asset policy preserves configured CSP headers and isolation sources", AssetResponsePolicyTests.SharedPolicy),
    ("JSON text IPC preserves numeric precision through plain and isolation transport", JsonTextIpcTests.PrecisionAndIsolation),
    ("JSON text IPC enforces input authorization response and document boundaries", JsonTextIpcTests.Boundaries),
    ("website data-store error boundaries", DataStoreTests.ErrorBoundaries),
    ("polymorphic command input validates derived required nullable and numeric members", PolymorphicInputTests.DerivedMembers),
    ("polymorphic command input preserves discriminator rejection and explicit fallback", PolymorphicInputTests.DiscriminatorErrors),
    ("generated plugin commands dispatch through IPC with isolated names and explicit selection", GeneratedPluginCommandTests.DispatchAndIsolation),
    ("generated plugin commands require capability and respect deny precedence", GeneratedPluginCommandTests.Permissions),
    ("generated plugin commands reject invalid owners selections and registrations", GeneratedPluginCommandTests.InvalidSelection),
    ("runtime identity rejects a Tauri version through guest IPC", RuntimeIdentity),
    ("browser profile identity preserves app data roots and distinguishes reserved identifiers", BrowserProfileIdentityTests.RuntimeContext),
    ("Document-owned resource close rejects other windows and preserves backend sharing", ResourceOwnershipTests.CloseRequiresOwner),
    ("IPC boundary replacement rejects late resources and waits for async scope disposal", IpcBoundaryLifetimeTests.ReplacementWaitsForScopeAndRejectsLateResources),
    ("IPC boundary reparent preserves in-flight channels and rechecks destination ACL", IpcBoundaryLifetimeTests.ReparentPreservesInFlightDocumentAndRechecksAcl),
    ("SQLite integer parameters preserve signed 64-bit values and reject overflow", SqlPluginTests.IntegerBinding),
    ("SQLite migration registrations reject invalid history and late configuration", SqlMigrationTests.Registration),
    ("SQLite migrations upgrade once and survive close and application reopen", SqlMigrationTests.UpgradeAndReopen),
    ("SQLite migration failure rolls back the entire batch and permits retry", SqlMigrationTests.RollbackAndRetry),
    ("SQLite migrations reject changed missing and reordered applied history", SqlMigrationTests.ChangedHistory),
    ("SQLite migration scripts cannot escape the host transaction or edit history", SqlMigrationTests.TransactionBoundary),
    ("SQLite preload applies migrations without granting guest write permission", SqlMigrationTests.PreloadAndPermissions),
    ("SQLite concurrent connections serialize migration history checks", SqlMigrationTests.ConcurrentLoad),
    ("SQLite migration cancellation interrupts SQL and rolls back pending changes", SqlMigrationTests.Cancellation),
    ("SQLite failed preload rolls back and permits corrected startup", SqlMigrationTests.PreloadFailure),
    ("SQLite path aliases share migration history cache and close identity", SqlMigrationTests.DatabaseAliases),
    ("SQLite migration parser preserves triggers comments Unicode and 64-bit versions", SqlMigrationTests.ScriptParsing),
    ("Shell execution cancellation stops parent and descendant processes", ShellLifetimeTests.CancellationStopsProcessTree),
    ("HTTP redirects retain credentials only within the same origin", HttpRedirectTests.CredentialsStayOnOrigin),
    ("P2 guest command execution leaves the native UI callback responsive", CommandThreadTests.GuestDoesNotBlockUi),
    ("P2 default logging rotates a full file", LogPluginTests.Rotation),
    ("P2 concurrent oversized Unicode logs respect byte and retention limits", LogPluginTests.BoundedUnicodeRecords),
    ("P2 window state saves after native destruction and restores at startup", WindowStatePluginTests.AutomaticLifecycle),
    ("P2 automatic window state respects flags opt-out and corrupt files", WindowStatePluginTests.AutomaticPolicy),
    ("P1 store failed reload preserves values", PersistenceFailureTests.StoreReload),
    ("P1 store save atomically replaces the previous snapshot", PersistenceFailureTests.StoreReplace),
    ("P1 interrupted download preserves destination", PersistenceFailureTests.InterruptedDownload),
    ("P1 upload enforces file URL and redirect scopes through guest IPC", PluginSecurityTests.UploadScopes),
    ("P1 upload permits scoped transfers and deny overrides allow", PluginSecurityTests.UploadGrantsAndDenies),
    ("P1 SQL select permission cannot mutate or attach databases", PluginSecurityTests.SqlReadOnly),
    ("P1 SQL cancellation interrupts an executing query and preserves connection", PluginSecurityTests.SqlCancellation),
    ("P1 guest invocations cancel on document replacement and app disposal", InvocationLifetimeTests.CancelOnReloadAndDispose),
    ("async script evaluation captures document results and cancels with document lifetime", ScriptEvaluationTests.Run),
    ("P1 HTTP resources close on document replacement", InvocationLifetimeTests.HttpResourcesOnReload),
    ("P1 document close cancels streaming HTTP and releases both handles", InvocationLifetimeTests.HttpStreamingOnClose),
    ("P1 restart waits for cleanup and releases single-instance ownership", RestartLifecycleTests.RestartAfterCleanup),
    ("local app IPC succeeds through facade", LocalAppIpcSucceeds),
    ("IPC and command diagnostics use the configured logger factory", LoggingTests.CommandPipeline),
    ("log plugin forwards webview records to the configured logger factory", LoggingTests.WebviewRecordsUseLoggerFactory),
    ("instance commands plugin scope and permissions", NetWiv.Tests.InstanceCommandScenarios.PluginInstanceCommandsKeepTheirScopeAndPermissions),
    ("instance commands native callback context", NetWiv.Tests.InstanceCommandScenarios.NativeCallbackDoesNotCaptureCallerContext),
    ("instance commands Lifetimes", NetWiv.Tests.InstanceCommandScenarios.Lifetimes),
    ("trusted backend invocation bypasses guest ACL and exposes generated native context", BackendInvocationTests.TrustedContextAndGeneratedInjection),
    ("backend invocation generated channels callback against the native label", BackendInvocationTests.GeneratedChannelUsesNativeCallbackTarget),
    ("backend invocation links caller and shutdown cancellation without document ownership", BackendInvocationTests.CancellationAndDocumentLifetime),
    ("backend invocation resources belong to the application", BackendInvocationTests.BackendResourcesBelongToTheApplication),
    ("backend invocation preserves exception identity and structured payloads", BackendInvocationTests.ExceptionIdentityAndPayloadArePreserved),
    ("trusted backend invocation starts synchronous handlers on the caller thread", BackendInvocationTests.SynchronousHandlerStartsOnCallerThread),
    ("instance commands ConstructorAndParameterShareScope", NetWiv.Tests.InstanceCommandScenarios.ConstructorAndParameterShareScope),
    ("instance commands ConcurrentAwaitAndDisposal", NetWiv.Tests.InstanceCommandScenarios.ConcurrentAwaitAndDisposal),
    ("instance commands FailureAndCancellation", NetWiv.Tests.InstanceCommandScenarios.FailureAndCancellation),
    ("instance commands MissingRegistrationAndConstructorFailure", NetWiv.Tests.InstanceCommandScenarios.MissingRegistrationAndConstructorFailure),
    ("instance commands GuestIpcAndAuthorization", NetWiv.Tests.InstanceCommandScenarios.GuestIpcAndAuthorization),
    ("hosting wires services, options and AppHandle around the event loop lifecycle", HostingTests.ServicesConfigurationAndLifecycle),
    ("hosting runtime factories receive the configured root services", HostingTests.RuntimeFactoryUsesConfiguredServices),
    ("hosting StopApplication exits a running event loop and skips one not yet started", HostingTests.StopApplicationExitsEventLoop),
    ("hosting generated commands resolve scoped, keyed and AppHandle services", HostingTests.GeneratedCommandsResolveServices),
    ("hosting shutdown waits for commands in flight before stopping services", HostingTests.ShutdownWaitsForCommandsInFlight),
    ("hosting dispose waits for commands in flight before disposing services", HostingTests.DisposeWaitsForCommandsInFlight),
    ("hosting shutdown timeout bounds commands that ignore cancellation", HostingTests.ShutdownTimeoutBoundsHungCommands),
    ("hosting dispose atomically stops admitting commands before draining", HostingTests.DisposeClosesCommandAdmission),
    ("hosting hosted services can invoke commands while stopping", HostingTests.HostedServicesInvokeCommandsWhileStopping),
    ("hosting restart relaunches only after hosted services stop", HostingTests.RestartWaitsForHostedServices),
    ("upstream ipc::deserialize_invoke_response_body", InvokeResponseTests.DeserializeInvokeResponseBody),
    ("invoke response deserialization preserves binary and JSON boundaries", InvokeResponseTests.BinaryAndJsonBoundaries),
    ("command input ignores JSON-ignored and non-deserializable members", PropertyInputTests.IgnoredMembers),
    ("command input validates setter-only and populated members", PropertyInputTests.WritableAndPopulatedMembers),
    ("command input preserves directional JSON ignore conditions", PropertyInputTests.DirectionalIgnore),
    ("JSON context directional defaults preserve property overrides", SerializerPolicyTests.DirectionalDefaults),
    ("nullable enum converters preserve names, null and numeric values", SerializerPolicyTests.NullableEnumConverter),
    ("JSON included fields preserve names, constructors and required input", SerializerPolicyTests.IncludedFields),
    ("extension data flattens keys without bypassing known property validation", ExtensionDataTests.RoundTrip),
    ("extension data preserves directional ignores and field ownership", ExtensionDataTests.DirectionsAndFields),
    ("Rust debug strings match every Unicode scalar and command error escaping", RustDebugStringTests.UnicodeOracle),
    ("upstream event::test_illegal_event_name", RustDebugStringTests.IllegalEventName),
    ("DPI conversion matches pinned Rust rounding and saturation oracle", DpiOracleTests.Conversions),
    ("DPI scales match pinned Rust normal-number validation", DpiOracleTests.ScaleValidation),
    ("numeric command arguments match pinned serde JSON values", CommandNumberTests.PinnedArguments),
    ("numeric error formatting matches 100000 pinned float bit patterns", CommandNumberTests.FormatOracle),
    ("configured startup windows preserve defaults and defer create:false", ConfigurationWindowTests.DefaultsAndDeferred),
    ("embedded default icons reach runtime and explicit icons take precedence", ConfigurationWindowTests.Icons),
    ("empty configured windows suppress implicit UI and allow explicit later windows", ConfigurationWindowTests.EmptyAndProgrammatic),
    ("manager lookups return nullable snapshots across document and window lifecycles", ManagerLookupTests.Lifecycle),
    ("manager reparent updates retained handles and combined-window membership without guest events", ManagerLookupTests.ParentChanges),
    ("upstream format_callback::test_serialize_js", CallbackScriptTests.SerializeJs),
    ("upstream format_callback::test_serialize_js_raw", CallbackScriptTests.SerializeJsRaw),
    ("callback parsing threshold uses UTF-8 bytes and preserves primitives", CallbackScriptTests.ByteBoundaries),
    ("large events retain the upstream literal path separately from IPC callbacks", LargeEventCallbacks),
    ("upstream format_callback::qc_formatting", () => CallbackProperties(raw: false, result: false)),
    ("upstream format_callback::qc_format_res", () => CallbackProperties(raw: false, result: true)),
    ("upstream format_callback::qc_formatting_raw", () => CallbackProperties(raw: true, result: false)),
    ("upstream format_callback::qc_format_raw_res", () => CallbackProperties(raw: true, result: true)),
    ("generated commands require explicit builder selection", GeneratedCommandSelection),
    ("remote IPC is denied", RemoteIpcIsDenied),
    ("invalid invoke key is rejected before dispatch", InvalidInvokeKeyIsRejected),
    ("plugin command requires capability", PluginRequiresCapability),
    ("official fs plugin reads writes and rejects traversal", FsPluginTests.FileOperations),
    ("official fs binary guest write and read traverse IPC with scoped permission", FsPluginTests.BinaryGuestIpc),
    ("official fs scope permission joins command grants without broadening access", FsPluginTests.ScopePermissions),
    ("filesystem watch create modify rename remove and unwatch", FsWatchTests.Events),
    ("filesystem watch recursion file URLs and multiple files", FsWatchTests.RecursionAndMultipleFiles),
    ("filesystem watch debounce and pending disposal", FsWatchTests.Debounce),
    ("filesystem watch scope ownership reload and close", FsWatchTests.ScopeAndOwnership),
    ("filesystem watch validation cancellation and plugin disposal", FsWatchTests.ValidationAndDisposal),
    ("filesystem watch moved root and replacement preserve scope", FsWatchTests.MovedRootScope),
    ("filesystem watch directory movement cannot restore an invalidated registration", FsWatchRootTests.MovementCannotBecomeCurrentAgain),
    ("filesystem watch guards physical ancestors and releases directory handles", FsWatchRootTests.PhysicalAncestorsAndDisposal),
    ("filesystem watch recursive rearm and close across multiple roots", FsWatchTests.RearmLifetime),
    ("filesystem watch recursive registration resource limit", FsWatchTests.RecursiveRegistrationLimit),
    ("official dialog forwards native selection and grants selected file scope", DialogPluginTests.SelectedFileScope),
    ("official dialog legacy permission aliases authorize message only", DialogPluginTests.PermissionAliases),
    ("official OS guest receives synchronous platform values and guarded hostname", OsPluginTests.GuestContract),
    ("official process guest exit forwards status and restart stays permission guarded", ProcessPluginTests.ExitAndPermissions),
    ("official store guest shares handles and persists JSON on explicit save", StorePluginTests.Persistence),
    ("store autosave validates options and supports default and immediate saves", StoreAutosaveTests.Options),
    ("store autosave debounces every supported mutation", StoreAutosaveTests.DebounceAndMutations),
    ("store autosave polling retries missing files without hiding invalid data", StoreAutosaveTests.PollingReadRetriesMissingFile),
    ("store autosave explicit save cancels pending writes", StoreAutosaveTests.ExplicitSaveCancelsTimer),
    ("store autosave reload cancels pending writes and shutdown flush", StoreAutosaveTests.ReloadCancelsPendingWrites),
    ("store autosave failed reload preserves pending writes and shutdown flush", StoreAutosaveTests.FailedReloadKeepsPendingWrites),
    ("store autosave shares concurrent handles and flushes during app disposal", StoreAutosaveTests.SharedHandlesAndShutdown),
    ("store autosave failures remain retryable by save and shutdown", StoreAutosaveTests.FailedSaveRetries),
    ("store autosave disposal drains racing writes and reports flush failures", StoreAutosaveTests.DisposalRacesAndFailure),
    ("external opening reports launcher failures and respects deny permissions", ExternalOpenTests.LaunchFailures),
    ("external opening preserves opener URL/path arguments and rejects forbidden targets", ExternalOpenTests.OpenerTargets),
    ("external opening reveals files and directories with the platform launcher", ExternalOpenTests.RevealTargets),
    ("external opening preserves shell URL arguments and rejects forbidden targets", ExternalOpenTests.ShellTargets),
    ("official opener default scope rejects local URLs and custom executables", OpenerPluginTests.ScopeBoundaries),
    ("official log guest writes records and validates levels", LogPluginTests.Records),
    ("official HTTP guest enforces URL scope and streams a response", HttpPluginTests.Fetch),
    ("official clipboard guest requires explicit grants and forwards to native", ClipboardPluginTests.PermissionsAndForwarding),
    ("official shell guest executes only scoped aliases", ShellPluginTests.ScopedExecute),
    ("official shell sidecars enforce bundled paths argument scopes ownership and kill", ShellSidecarTests.ScopedSidecars),
    ("shell spawn rejects navigation during process startup", ShellLifetimeTests.NavigationDuringSpawn),
    ("SQLite competing processes serialize migration history and rollback failed migrations", SqlMigrationTests.ConcurrentProcessLoads),
    ("official window-state guest saves and restores selected window flags", WindowStatePluginTests.SaveAndRestore),
    ("official websocket guest connects and relays a local text frame", WebsocketPluginTests.LocalEcho),
    ("official upload guest streams files and reports HTTP failures", UploadPluginTests.Transfer),
    (OperatingSystem.IsLinux() ? "official autostart guest registers and removes a Linux desktop entry" :
        "official autostart guest reads an absent Windows registry entry", AutostartPluginTests.Registration),
    ("official positioner guest moves windows to screen and constrained tray anchors", PositionerPluginTests.AnchorsAndTray),
    ("official CLI guest resolves configured arguments and subcommands", CliPluginTests.MatchesAndErrors),
    ("persisted scope restores dialog selected asset access after reopening", PersistedScopePluginTests.RestoresDialogSelection),
    ("official SQL guest runs a SQLite query lifecycle with bound values", SqlPluginTests.SqliteLifecycle),
    ("single instance forwards a second launch's arguments to the first app", SingleInstancePluginTests.ForwardsSecondLaunch),
    ("single instance forwards arguments between separate desktop processes", SingleInstancePluginTests.ForwardsAcrossProcesses),
    ("deep link accepts configured launch URL and emits second launch event", DeepLinkPluginTests.CurrentAndEvent),
    ("deep link filters native opened URL batches and ignores application emitted events", DeepLinkPluginTests.NativeOpenedUrls),
    ("deep link registration uses per-user desktop protocol storage", DeepLinkPluginTests.Registration),
    ("deep link consumes the single-instance forwarded URL", DeepLinkPluginTests.SecondInstanceOpensUrl),
    ("localhost serves embedded assets and stops accepting requests on disposal", LocalhostPluginTests.ServesAssets),
    ("notification desktop guest forwards title and permission state", NotificationPluginTests.GuestContract),
    ("global shortcut guest registers native shortcuts and routes channel events", GlobalShortcutPluginTests.GuestCommandsAndChannel),
    ("stronghold guest forwards encrypted storage and procedure commands", StrongholdPluginTests.GuestContract),
    ("app manifest opts command into ACL", AppManifestRequiresCapability),
    ("app manifest gates every custom command", AppManifestGatesAllCommands),
    ("core default expands built-in permissions", CoreDefaultExpands),
    ("path commands match pinned lexical fixtures", PathCommandsMatchPinnedFixtures),
    ("upstream path::basename", UpstreamPathAndUrlTests.Basename),
    ("upstream path::join", UpstreamPathAndUrlTests.Join),
    ("upstream mime_type::should_parse_mimetype_from_uri", UpstreamPathAndUrlTests.MimeTypeFromUri),
    ("upstream acl::url_pattern_domain_wildcard", UpstreamPathAndUrlTests.UrlPatternDomainWildcard),
    ("upstream acl::url_pattern_path_wildcard", UpstreamPathAndUrlTests.UrlPatternPathWildcard),
    ("upstream acl::url_pattern_scheme_wildcard", UpstreamPathAndUrlTests.UrlPatternSchemeWildcard),
    ("unknown permission names never synthesize grants", UnknownPermissionDoesNotGrant),
    ("capabilities without targets grant nothing", EmptyCapabilityTargetDoesNotGrant),
    ("deny permissions precede allow permissions", DenyPrecedesAllow),
    ("upstream authority::window_glob_pattern_matches", UpstreamAuthorityTests.WindowGlobPatternMatches),
    ("upstream authority::webview_glob_pattern_matches", UpstreamAuthorityTests.WebviewGlobPatternMatches),
    ("upstream authority::remote_domain_matches", UpstreamAuthorityTests.RemoteDomainMatches),
    ("upstream authority::remote_domain_glob_pattern_matches", UpstreamAuthorityTests.RemoteDomainGlobPatternMatches),
    ("upstream authority::remote_context_denied", UpstreamAuthorityTests.RemoteContextDenied),
    ("upstream authority::denied_command_takes_precendence", UpstreamAuthorityTests.DeniedCommandTakesPrecedence),
    ("upstream authority::resolve_access_message", UpstreamAuthorityTests.ResolveAccessMessage),
    ("ACL diagnostics preserve associated permissions and deny provenance", UpstreamAuthorityTests.DiagnosticBoundaries),
    ("release IPC preserves the upstream ACL rejection string", UpstreamAuthorityTests.ReleaseDenialMessage),
    ("permission command names are exact ordinal keys", PermissionCommandsAreExact),
    ("initialization script uses pinned IPC serialization", InitializationScriptMatchesPinnedSerializer),
    ("authorized native forwarding preserves origin", ForwardingPreservesOrigin),
    ("remote capability admits an explicit app command", RemoteCapabilityAdmitsCommand),
    ("remote URL patterns match pinned parser and permission decisions", RemoteUrlPatternTests.Run),
    ("binary responses resolve as ArrayBuffer", BinaryResponseUsesArrayBuffer),
    ("structured errors reject with JSON", StructuredErrorUsesJson),
    ("image core stores RGBA resources", ImageCoreRoundTrips),
    ("resource disposal closes each resource", ResourceDisposalIsDeterministic),
    ("unknown resource close reaches native runtime", NativeResourceCloseIsForwarded),
    ("channel messages retain order and terminate", ChannelIsOrdered),
    ("event targets preserve window and webview kinds", EventTargetKindsAreDistinct),
    ("upstream manager::emit", ManagerEventTests.Emit),
    ("manager typed event payload uses source-generated JSON and preserves targets", ManagerEventTests.TypedPayload),
    ("upstream manager::emit_to", ManagerEventTests.EmitTo),
    ("manager once-any and unlisten preserve target and reentrant cleanup", ManagerEventTests.OnceAndUnlisten),
    ("native close events distinguish broadcast and preventing listeners", NativeCloseEventTargets),
    ("event names follow pinned Unicode validation and unlisten matches name", EventNameAndUnlisten),
    ("asset resolver is exposed to runtime", AssetResolverIsExposed),
    ("URI scheme requests preserve context binary data async completion and ownership", UriSchemeTests.RequestsAndOwnership),
    ("upstream webview::tauri_protocol_is_local", UriSchemeTests.TauriProtocolIsLocal),
    ("registered URI scheme origins enforce platform boundaries and app manifests", UriSchemeTests.OriginBoundaries),
    ("file asset protocol preserves scoped HTTP response semantics", FileAssetProtocolTests.Run),
    ("file scopes enforce glob and canonical filesystem boundaries", FileScopeTests.Run),
    ("file MIME signatures follow pinned infer matcher order", FileAssetMimeTypeTests.Run),
    ("embedded frontend assets resolve from the entry assembly", EmbeddedAssetsResolve),
    ("embedded asset routing matches pinned fallback and HTTP behavior", EmbeddedAssetTests.RoutingAndProtocol),
    ("embedded CSP follows the resolved asset rather than the requested MIME type", EmbeddedAssetTests.CspUsesResolvedAsset),
    ("embedded config applies windows ACL and security headers", EmbeddedConfigApplies),
    ("embedded config mode explicitly selects development settings", EmbeddedConfigDevelopmentMode),
    ("embedded config rejects unresolved capability files", EmbeddedConfigRejectsExternalCapabilities),
    ("embedded permission files gate commands and retain per-command scope", EmbeddedPermissionFiles),
    ("empty embedded ACL preserves local defaults and rejects cycles", EmbeddedPermissionBoundaries),
    ("plugin lifecycle receives events and disposal", PluginLifecycleRuns),
    ("plugin config preserves values defaults typed reads and document lifetime", PluginConfigurationTests.ValuesAndLifetime),
    ("invalid plugin config fails initialization and disposes owned plugins", PluginConfigurationTests.InvalidConfiguration),
    ("upstream resolved::resolves_permissions_from_other_plugins", PluginPermissionTests.OtherPlugins),
    ("plugin permission sets preserve nested precedence and reject cycles and missing references", PluginPermissionTests.PrecedenceAndFailures),
    ("cross-plugin permission sets authorize guest commands through the application host", CrossPluginPermissions),
    ("reload discards pending invocation replies", ReloadDiscardsPendingReplies),
    ("reload invalidates captured channels and listeners", ReloadInvalidatesCallbacks),
    ("window and webview labels use separate namespaces", SeparateLabelNamespaces),
    ("destroying a window preserves unrelated same-label webview", DestroyPreservesUnrelatedWebview),
    ("native menu and tray hooks reject guest event spoofing", NativeHooksAreTrusted),
    ("native tray events decode tagged rectangles and optional button fields", TaggedTrayEvents),
    ("capability labels implement upstream glob selectors", CapabilityLabelGlobs),
    ("upstream state::get_panics", StateTests.GetPanics),
    ("upstream state::simple_set_get", StateTests.SimpleSetGet),
    ("upstream state::simple_set_get_unmanage", StateTests.SimpleSetGetUnmanage),
    ("upstream state::dst_set_get", StateTests.DstSetGet),
    ("upstream state::set_get_remote", StateTests.SetGetRemote),
    ("upstream state::two_put_get", StateTests.TwoPutGet),
    ("upstream state::many_puts_only_one_succeeds", StateTests.ManyPutsOnlyOneSucceeds),
    ("upstream state::test_no_drop_on_set", StateTests.NoDropOnSet),
    ("upstream state::drop_inners_on_drop", StateTests.DropInnersOnDrop),
    ("state removal and aliased or failing disposal preserve ownership", StateTests.OwnershipAndDisposalErrors),
    ("failed application construction releases owned state and plugins", StateTests.FailedBuildReleasesState),
    ("upstream event::listeners_check_key (10000 cases)", UpstreamEventTests.ListenersCheckKey),
    ("upstream event::listeners_check_fn (10000 cases)", UpstreamEventTests.ListenersCheckFn),
    ("upstream event::check_on_event (10000 cases)", UpstreamEventTests.CheckOnEvent),
    ("upstream event::event_no_deadlocks", UpstreamEventTests.EventNoDeadlocks),
    ("upstream scope::path_is_escaped", FileScopeMutationTests.PathIsEscaped),
    ("upstream scope::push_pattern_generated_paths", FileScopeMutationTests.PushPatternGeneratedPaths),
    ("upstream scope::event_no_deadlocks", FileScopeMutationTests.EventNoDeadlocks),
    ("scope changes are concurrent and preserve deny precedence", FileScopeMutationTests.ConcurrentChangesAndDenyPrecedence),
    ("asset protocol reads the live application scope", FileScopeMutationTests.AssetProtocolUsesLiveScope),
    ("upstream html::csp", HtmlAssetTests.Csp),
    ("upstream html::normalize_script_for_csp_test", HtmlAssetTests.NormalizeScriptForCsp),
    ("upstream html::parse_and_serialize_roundtrips", HtmlAssetTests.ParseAndSerializeRoundtrips),
    ("upstream html::inject_nonce_to_scripts", HtmlAssetTests.InjectNonceToScripts),
    ("upstream html::inject_nonce_to_styles", HtmlAssetTests.InjectNonceToStyles),
    ("upstream html::inject_nonce_skips_existing", HtmlAssetTests.InjectNonceSkipsExisting),
    ("upstream html::inject_nonce_respects_disabled_modification", HtmlAssetTests.InjectNonceRespectsDisabledModification),
    ("upstream html::inline_isolation_replaces_src_with_content", HtmlAssetTests.InlineIsolationReplacesSrcWithContent),
    ("upstream html2::append_script_to_head_test", HtmlAssetTests.AppendScriptToHead),
    ("CSP parses HTML and authorizes independent nonce sources", HtmlAssetTests.CspUsesBrowserParsingAndNonceSources),
    ("upstream isolation::create_keys", IsolationTests.CreateKeys),
    ("upstream ipc::parse_invoke_request_isolation", IsolationTests.ParseInvokeRequestIsolation),
    ("upstream ipc::parse_invoke_request", ProtocolIpcTests.ParseInvokeRequest),
    ("upstream webview::remote_origin_blocked_for_custom_commands_without_app_manifest", ProtocolIpcTests.RemoteOriginWithoutAppManifest),
    ("IPC HTTP parsing authorization response and document boundaries", ProtocolIpcTests.Boundaries),
    ("upstream acl::identifier::format", PermissionInputTests.Format),
    ("upstream acl::identifier::base", PermissionInputTests.Base),
    ("upstream acl::identifier::prefix", PermissionInputTests.Prefix),
    ("upstream acl::capability::permission_entry_de", PermissionInputTests.PermissionEntryDe),
    ("capability parsing rejects malformed permissions and scopes", PermissionInputTests.InvalidInputs),
    ("isolation ciphertext authentication and secure asset boundaries", IsolationTests.CipherAndProtocolBoundaries),
    ("IPC cross-condition isolation rejects missing envelopes", IpcBoundaryMatrixTests.IsolationRequiresEnvelope),
    ("IPC cross-condition origin lifetime and resource boundaries", IpcBoundaryMatrixTests.OriginAndLifetime),
};
if (OperatingSystem.IsLinux())
{
    tests.Add(("official fs scope rejects a symlink escaping its allowed directory", FsPluginTests.SymlinkScope));
    tests.Add(("filesystem watch rejects symlink escapes and filters retargeted queued paths", FsWatchTests.SymlinkScope));
    tests.Add(("filesystem watch rearm preserves fresh debounce window", FsWatchTests.RearmDebounce));
}
if (OperatingSystem.IsWindows()) tests.Add(("upstream scope::windows_root_paths", FileScopeMutationTests.WindowsRootPaths));
if (OperatingSystem.IsWindows()) tests.Add(("upstream webview::windows_custom_protocol_rejects_spoofed_domain", UriSchemeTests.WindowsCustomProtocolRejectsSpoofedDomain));
else Console.WriteLine("SKIP upstream scope::windows_root_paths: Windows-specific scenarios");
if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("NETWIV_INFER_TESTDATA")))
    tests.Add(("pinned infer corpus validates 63 complete files and prefix reads", FileAssetMimeTypeTests.UpstreamCorpus));
else Console.WriteLine("SKIP pinned infer corpus: NETWIV_INFER_TESTDATA is not configured");

var failed = 0;
if (OperatingSystem.IsMacOS())
{
    tests.Add(("external opening rejects missing macOS reveal items before any launch", ExternalOpenTests.MacRevealValidation));
    tests.Add(("external opening canonicalizes macOS reveal links and rejects broken targets", ExternalOpenTests.MacRevealLinks));
}

var selected = args.Length == 0 ? tests : args.Length == 2 && args[0] == "--filter"
    ? tests.Where(test => test.Name.Contains(args[1], StringComparison.OrdinalIgnoreCase)).ToList()
    : throw new ArgumentException("Usage: NetWiv.Core.Tests [--filter text]");
if (selected.Count == 0) throw new ArgumentException("The test filter matched no cases.");
foreach (var test in selected)
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception exception) { failed++; Console.Error.WriteLine($"FAIL {test.Name}: {exception}"); }
}
return failed;

static async Task LocalAppIpcSucceeds()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime)
        .RegisterCommand("echo", (_, args, _) => ValueTask.FromResult(args.GetProperty("value").Clone()))
        .Build();
    app.Run();
    Send(runtime, "echo", "{\"value\":42}", new Uri("tauri://localhost"));
    var script = await runtime.NextScript();
    Assert.Contains("runCallback(1,42)", script);
}

static async Task GeneratedCommandSelection()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime).InvokeHandler("generated_selected").Build();
    app.Run();
    Send(runtime, "generated_selected", "{}", new Uri("tauri://localhost"));
    Assert.Contains("runCallback(1,\"generated\")", await runtime.NextScript());

    var missingRuntime = new FakeRuntime();
    using var missing = new Builder().UseRuntime(missingRuntime).Build();
    missing.Run();
    Send(missingRuntime, "generated_selected", "{}", new Uri("tauri://localhost"));
    Assert.Contains("runCallback(2,\"Command generated_selected not found\")", await missingRuntime.NextScript());
    Assert.Equal<string?>(null, missingRuntime.LastCommand);
    Send(missingRuntime, "plugin:not-a-command", "{}", new Uri("tauri://localhost"));
    Assert.Contains("runCallback(2,\"Command plugin:not-a-command not allowed by ACL\")", await missingRuntime.NextScript());
    Assert.Equal<string?>(null, missingRuntime.LastCommand);
}

static async Task RemoteIpcIsDenied()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime)
        .Window("main", "Remote", "https://untrusted.example")
        .RegisterCommand("echo", (_, args, _) => ValueTask.FromResult(args.Clone()))
        .Build();
    app.Run();
    Send(runtime, "echo", "{}", new Uri("https://untrusted.example/page"));
    var script = await runtime.NextScript();
    Assert.Contains("runCallback(2,", script);
    Assert.Contains("not allowed", script);
}

static async Task InvalidInvokeKeyIsRejected()
{
    var runtime = new FakeRuntime();
    var calls = 0;
    using var app = new Builder().UseRuntime(runtime)
        .RegisterCommand("echo", (_, args, _) => { calls++; return ValueTask.FromResult(args.Clone()); })
        .Build();
    app.Run();
    runtime.Context!.OnMessage(new("main", new Uri("tauri://localhost"),
        "{\"cmd\":\"echo\",\"callback\":1,\"error\":2,\"payload\":{},\"__NETWIV_INVOKE_KEY__\":\"wrong\"}"));
    Assert.Contains("runCallback(2,\"invalid invoke key\")", await runtime.NextScript());
    Assert.Equal(0, calls);
}

static async Task PluginRequiresCapability()
{
    var deniedRuntime = new FakeRuntime();
    using (var denied = new Builder().UseRuntime(deniedRuntime)
        .RegisterCommand("plugin:sample|ping", (_, _, _) => ValueTask.FromResult(JsonValue.From("pong")))
        .Build())
    {
        denied.Run(); Send(deniedRuntime, "plugin:sample|ping", "{}", new Uri("tauri://localhost"));
        Assert.Contains("runCallback(2,", await deniedRuntime.NextScript());
    }

    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime)
        .RegisterCommand("plugin:sample|ping", (_, _, _) => ValueTask.FromResult(JsonValue.From("pong")))
        .Permission(new("sample:allow-ping", "plugin:sample|ping"))
        .Capability(new("main", ["sample:allow-ping"], Windows: ["main"]))
        .Build();
    app.Run(); Send(runtime, "plugin:sample|ping", "{}", new Uri("tauri://localhost"));
    Assert.Contains("runCallback(1,\"pong\")", await runtime.NextScript());
}

static async Task AppManifestRequiresCapability()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime)
        .RegisterCommand("sensitive", (_, _, _) => ValueTask.FromResult(JsonValue.Null))
        .AppCommandManifest("sensitive")
        .Build();
    app.Run(); Send(runtime, "sensitive", "{}", new Uri("tauri://localhost"));
    Assert.Contains("runCallback(2,", await runtime.NextScript());

    var allowedRuntime = new FakeRuntime();
    using var allowed = new Builder().UseRuntime(allowedRuntime)
        .RegisterCommand("sensitive", (_, _, _) => ValueTask.FromResult(JsonValue.From(true)))
        .AppCommandManifest("sensitive")
        .Capability(new("allowed", ["allow-sensitive"], Windows: ["main"]))
        .Build();
    allowed.Run(); Send(allowedRuntime, "sensitive", "{}", new Uri("tauri://localhost"));
    Assert.Contains("runCallback(1,true)", await allowedRuntime.NextScript());
}

static async Task AppManifestGatesAllCommands()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime)
        .RegisterCommand("public", (_, _, _) => ValueTask.FromResult(JsonValue.From(true)))
        .RegisterCommand("sensitive", (_, _, _) => ValueTask.FromResult(JsonValue.From(true)))
        .AppCommandManifest("sensitive")
        .Capability(new("sensitive", ["allow-sensitive"], Windows: ["main"]))
        .Build();
    app.Run();
    Send(runtime, "public", "{}", new Uri("tauri://localhost"));
    Assert.Contains("runCallback(2,", await runtime.NextScript());
}

static Task UnknownPermissionDoesNotGrant()
{
    var runtime = new FakeRuntime();
    Assert.Throws<InvalidOperationException>(() => new Builder().UseRuntime(runtime)
        .RegisterCommand("plugin:ghost|pwn", (_, _, _) => ValueTask.FromResult(JsonValue.From(true)))
        .Capability(new("forged", ["core:ghost:allow-pwn", "plugin:ghost|pwn"], Windows: ["main"]))
        .Build());
    return Task.CompletedTask;
}

static async Task EmptyCapabilityTargetDoesNotGrant()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime)
        .Capability(new("untargeted", ["core:path:allow-is-absolute"]))
        .Build();
    app.Run(); Send(runtime, "plugin:path|is_absolute", "{\"path\":\"/tmp\"}", new Uri("tauri://localhost"));
    Assert.Contains("runCallback(2,", await runtime.NextScript());
}

static async Task DenyPrecedesAllow()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime)
        .Capability(new("allow", ["core:path:allow-is-absolute"], Windows: ["main"]))
        .Capability(new("deny", ["core:path:deny-is-absolute"], Windows: ["other"]))
        .Build();
    app.Run(); Send(runtime, "plugin:path|is_absolute", "{\"path\":\"/tmp\"}", new Uri("tauri://localhost"));
    Assert.Contains("runCallback(2,", await runtime.NextScript());
}

static async Task PermissionCommandsAreExact()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime)
        .AppCommandManifest("sensitive", "echo", "*", "literal_*")
        .Permission(new("exact-keys", ["*", "literal_*", "echo"], ["echo*", "SENSITIVE"]))
        .Capability(new("exact", ["exact-keys"], Windows: ["main"]))
        .RegisterCommand("sensitive", (_, _, _) => ValueTask.FromResult(JsonValue.From(true)))
        .RegisterCommand("echo", (_, _, _) => ValueTask.FromResult(JsonValue.From(true)))
        .RegisterCommand("*", (_, _, _) => ValueTask.FromResult(JsonValue.From(true)))
        .RegisterCommand("literal_*", (_, _, _) => ValueTask.FromResult(JsonValue.From(true)))
        .Build();
    app.Run();
    Send(runtime, "sensitive", "{}", new Uri("tauri://localhost"));
    Assert.Contains("runCallback(2,", await runtime.NextScript());
    foreach (var command in new[] { "echo", "*", "literal_*" })
    {
        Send(runtime, command, "{}", new Uri("tauri://localhost"));
        Assert.Contains("runCallback(1,true)", await runtime.NextScript());
    }
}

static Task InitializationScriptMatchesPinnedSerializer()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime).Build();
    app.Run();
    var script = runtime.Context!.InitializationScript;
    Assert.Contains("value instanceof Map", script);
    Assert.Contains("value instanceof Uint8Array", script);
    Assert.Contains("value instanceof ArrayBuffer", script);
    Assert.Contains("'__TAURI_TO_IPC_KEY__' in value", script);
    Assert.Contains("}, ipcReplacer)", script);
    Assert.Contains("if (window.top.location.origin !== window.location.origin) return;", script);
    Assert.Contains("} catch { return; }", script);
    Assert.DoesNotContain("Only Uint8Array is supported", script);
    Assert.DoesNotContain("isTauri", script);
    Assert.DoesNotContain("isNetWiv", script);
    return Task.CompletedTask;
}

static async Task RuntimeIdentity()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime)
        .Metadata(new("Example", "1.2.3", "dev.example"))
        .Capability(new("default", ["core:default"], Windows: ["main"]))
        .Build();
    app.Run();
    Send(runtime, "plugin:app|tauri_version", "{}", new Uri("tauri://localhost"));
    var response = await runtime.NextScript();
    Assert.Contains("runCallback(2,", response);
    Assert.Contains("NetWiv does not embed the TAURI framework", response);
    Assert.DoesNotContain("2.11.5", response);
    Send(runtime, "plugin:app|version", "{}", new Uri("tauri://localhost"));
    Assert.Contains("runCallback(1,\"1.2.3\")", await runtime.NextScript());
    Send(runtime, "plugin:app|name", "{}", new Uri("tauri://localhost"));
    Assert.Contains("runCallback(1,\"Example\")", await runtime.NextScript());
}

static async Task CoreDefaultExpands()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime)
        .Metadata(new("Example", "1.2.3", "dev.example"))
        .Capability(new("default", ["core:default"], Windows: ["main"]))
        .Build();
    app.Run(); Send(runtime, "plugin:app|name", "{}", new Uri("tauri://localhost"));
    Assert.Contains("runCallback(1,\"Example\")", await runtime.NextScript());
}

static async Task PathCommandsMatchPinnedFixtures()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime).Build();
    var separator = Path.DirectorySeparatorChar;
    async Task<string> InvokePath(string command, string args) =>
        (await app.Handle.InvokeAsync($"plugin:path|{command}", JsonValue.Parse(args))).GetString()!;

    Assert.Equal(".", await InvokePath("join", "{\"paths\":[\"\"]}"));
    Assert.Equal($"a{separator}b", await InvokePath("join", "{\"paths\":[\"a\",\"b\"]}"));
    Assert.Equal($"a{separator}b{separator}c", await InvokePath("join", "{\"paths\":[\"a\",\"/b\",\"c\"]}"));
    Assert.Equal($"b{separator}", await InvokePath("normalize", "{\"path\":\"a/../b/\"}"));
    Assert.Equal("..", await InvokePath("normalize", "{\"path\":\"..\"}"));
    Assert.Equal(".", await InvokePath("normalize", "{\"path\":\".\"}"));
    Assert.Equal("a", await InvokePath("basename", "{\"path\":\"a/./\"}"));
    Assert.Equal("", await InvokePath("extname", "{\"path\":\"file.\"}"));
    Assert.Equal("a//b", await InvokePath("dirname", "{\"path\":\"a//b/./c\"}"));
    Assert.Equal($"{separator}{separator}", await InvokePath("normalize", "{\"path\":\"/\"}"));
    var absolute = await app.Handle.InvokeAsync("plugin:path|is_absolute", JsonValue.Parse("{\"path\":\"/\"}"));
    Assert.Equal(!OperatingSystem.IsWindows(), absolute.GetBoolean());
    Assert.Equal(Environment.CurrentDirectory, await InvokePath("resolve", "{\"paths\":[]}"));
    Assert.Equal(Environment.CurrentDirectory, await InvokePath("resolve", "{\"paths\":[\".\"]}"));
    Assert.Equal(Path.Combine(Environment.CurrentDirectory, "b"), await InvokePath("resolve", "{\"paths\":[\"a\",\"..\",\"b\"]}"));
    if (OperatingSystem.IsWindows())
    {
        Assert.Equal(@"C:\b", await InvokePath("resolve", "{\"paths\":[\"C:/a\",\"/b\"]}"));
        Assert.Equal("C:b", await InvokePath("resolve", "{\"paths\":[\"C:/a\",\"C:b\"]}"));
        Assert.Equal("D:b", await InvokePath("resolve", "{\"paths\":[\"C:/a\",\"D:b\"]}"));
        Assert.Equal(@"C:\b", await InvokePath("resolve", "{\"paths\":[\"C:/a\",\"..\",\"..\",\"b\"]}"));
        // normalize and join use upstream's different string-based helper.
        Assert.Equal(@"C:\b", await InvokePath("normalize", "{\"path\":\"C:b\"}"));
    }
    else Assert.Equal("/b/c", await InvokePath("resolve", "{\"paths\":[\"/a\",\"/b\",\"c\"]}"));
    foreach (var command in new[] {"basename", "extname"})
    {
        try { await InvokePath(command, "{\"path\":\"..\"}"); throw new Exception("Expected path rejection."); }
        catch (InvokeException exception)
        {
            Assert.Contains(command == "basename" ? "path does not have a basename" : "path does not have an extension", exception.Message);
        }
    }
}

static async Task ForwardingPreservesOrigin()
{
    var runtime = new FakeRuntime { InvokeResult = JsonValue.From(true) };
    using var app = new Builder().UseRuntime(runtime)
        .Permission(new("sample:allow-native", "plugin:sample|native"))
        .Capability(new("child", ["sample:allow-native"], Webviews: ["child-* ".Trim()]))
        .Window(new("shell", "Shell", "index.html", 800, 600, default,
            [new("child-one", "child.html", 0, 0, 400, 300, default)]))
        .Build();
    app.Run();
    Send(runtime, "plugin:sample|native", "{\"spoofedLabel\":\"admin\",\"isBackend\":true}", new Uri("tauri://localhost"), "child-one", "shell");
    Assert.Contains("runCallback(1,true)", await runtime.NextScript());
    Assert.Equal("child-one", runtime.LastContext?.WebviewLabel);
    Assert.Equal(new Uri("tauri://localhost"), runtime.LastContext?.Source);
    Assert.Equal(true, runtime.LastContext?.IsAuthorized);
    Assert.Equal(false, runtime.LastContext?.IsBackend);
    await app.Handle.InvokeAsync("plugin:sample|native", JsonValue.EmptyObject);
    Assert.Equal(true, runtime.LastContext?.IsBackend);
    Assert.Equal("native", runtime.LastContext?.WebviewLabel);
}

static async Task RemoteCapabilityAdmitsCommand()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime)
        .Window("main", "Remote", "https://trusted.example/app")
        .RegisterCommand("remote-command", (_, _, _) => ValueTask.FromResult(JsonValue.From(true)))
        .Permission(new("allow-remote-command", "remote-command"))
        .Capability(new("remote", ["allow-remote-command"], Windows: ["main"], Local: false, RemoteUrls: ["https://trusted.example/*"]))
        .Build();
    app.Run(); Send(runtime, "remote-command", "{}", new Uri("https://trusted.example/app"));
    Assert.Contains("runCallback(1,true)", await runtime.NextScript());
}

static async Task BinaryResponseUsesArrayBuffer()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime)
        .InvokeHandler(registry => registry.RegisterResponse("echo_bytes", new ResponseCommandHandler(
            (_, _, _) => ValueTask.FromResult(RuntimeInvokeResponse.FromBinary(new byte[] { 1, 2, 3 })))))
        .Build();
    app.Run(); Send(runtime, "echo_bytes", "{}", new Uri("tauri://localhost"));
    Assert.Contains("Uint8Array.from([1,2,3]).buffer", await runtime.NextScript());
}

static async Task StructuredErrorUsesJson()
{
    using (var document = JsonDocument.Parse("\"window not found\""))
    {
        var error = new InvokeException(document.RootElement);
        document.Dispose();
        Assert.Equal("window not found", error.Message);
        Assert.Equal("window not found", error.ErrorPayload.GetString());
    }
    var structured = new InvokeException(JsonValue.Parse("{\"code\":7}"));
    Assert.Equal("The command returned a structured error.", structured.Message);
    Assert.Equal(7, structured.ErrorPayload.GetProperty("code").GetInt32());
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime)
        .RegisterCommand("fail_json", (_, _, _) => throw new InvokeException(JsonValue.Parse("{\"code\":7}")))
        .Build();
    app.Run(); Send(runtime, "fail_json", "{}", new Uri("tauri://localhost"));
    Assert.Contains("runCallback(2,{\"code\":7})", await runtime.NextScript());
}

static async Task ImageCoreRoundTrips()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime).Build();
    var rid = await app.Handle.InvokeAsync("plugin:image|new", JsonValue.Parse("{\"rgba\":[255,0,0,255],\"width\":1,\"height\":1}"));
    var rgba = await app.Handle.InvokeAsync("plugin:image|rgba", JsonValue.Parse($"{{\"rid\":{rid.GetUInt32()}}}"));
    var size = await app.Handle.InvokeAsync("plugin:image|size", JsonValue.Parse($"{{\"rid\":{rid.GetUInt32()}}}"));
    Assert.Equal("[255,0,0,255]", rgba.GetRawText());
    Assert.Equal("{\"width\":1,\"height\":1}", size.GetRawText());
    var metadata = await app.Handle.InvokeAsync("plugin:image|new", JsonValue.Parse("{\"rgba\":[1,2,3],\"width\":10,\"height\":20}"));
    var stored = app.Handle.Resources.Get<ImageData>(metadata.GetUInt32());
    Assert.Equal(10U, stored.Width);
    Assert.Equal(20U, stored.Height);
    Assert.Equal(3, stored.Rgba.Length);
}

static Task ResourceDisposalIsDeterministic()
{
    var table = new ResourceTable();
    var resources = Enumerable.Range(0, 128).Select(_ => new TrackedResource()).ToArray();
    Parallel.ForEach(resources, resource => table.Add(resource));
    table.Dispose();
    Assert.Equal(128, resources.Sum(static resource => resource.DisposeCount));
    Assert.Throws<ObjectDisposedException>(() => table.Add(new TrackedResource()));
    return Task.CompletedTask;
}

static async Task NativeResourceCloseIsForwarded()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime).Build();
    await app.Handle.InvokeAsync("plugin:resources|close", JsonValue.Parse("{\"rid\":9}"));
    Assert.Equal("plugin:resources|close", runtime.LastCommand);
}

static async Task ChannelIsOrdered()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime).Build();
    app.Run();
    using (var channel = new Channel(app.Handle, "main", 77))
    {
        channel.Send(JsonValue.From("a"));
        channel.Send(JsonValue.From("b"));
    }
    Assert.Contains("\"index\":0", await runtime.NextScript());
    Assert.Contains("\"index\":1", await runtime.NextScript());
    Assert.Contains("\"end\":true,\"index\":2", await runtime.NextScript());
}

static async Task EventTargetKindsAreDistinct()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime).Build();
    app.Run();
    await app.Handle.InvokeAsync("plugin:event|listen", JsonValue.Parse("{\"event\":\"typed\",\"target\":{\"kind\":\"Window\",\"label\":\"same\"},\"handler\":11}"));
    await app.Handle.InvokeAsync("plugin:event|listen", JsonValue.Parse("{\"event\":\"typed\",\"target\":{\"kind\":\"Webview\",\"label\":\"same\"},\"handler\":12}"));
    await runtime.NextScript();
    await runtime.NextScript();
    await app.Handle.InvokeAsync("plugin:event|emit_to", JsonValue.Parse("{\"event\":\"typed\",\"target\":{\"kind\":\"Window\",\"label\":\"same\"},\"payload\":1}"));
    Assert.Contains("runCallback(11,", await runtime.NextScript());
    Assert.Equal(0, runtime.PendingScripts);
}

static async Task NativeCloseEventTargets()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime).Build();
    app.Run();
    const string eventName = "tauri://close-requested";
    var received = new List<string>();
    using var any = app.Handle.Events.Listen(eventName, _ => received.Add("Any"));
    using var application = app.Handle.Events.Listen(eventName, RuntimeEventTarget.App, _ => received.Add("App"));
    using var label = app.Handle.Events.Listen(eventName, new("AnyLabel", "main"), _ => received.Add("AnyLabel"));
    using var webview = app.Handle.GetWebview("main")!.Listen(eventName, _ => received.Add("Webview"));
    Assert.Equal(false, runtime.Context!.HasEventListeners!(eventName, "main"));
    using (var window = app.Handle.GetWindow("main")!.Listen(eventName, _ => received.Add("Window")))
    using (var combined = app.Handle.GetWebviewWindow("main")!.Listen(eventName, _ => received.Add("WebviewWindow")))
    {
        Assert.Equal(true, runtime.Context.HasEventListeners(eventName, "main"));
        Assert.Equal(false, runtime.Context.HasEventListeners(eventName, "other"));
        runtime.Context.OnEvent(new(eventName, "main", JsonValue.Null, new("Window", "main")));
        Assert.Equal("Any,WebviewWindow,Window", string.Join(',', received.Order()));
    }
    Assert.Equal(false, runtime.Context.HasEventListeners(eventName, "main"));
    var js = await app.Handle.InvokeAsync("plugin:event|listen", JsonValue.Parse("""{"event":"tauri://close-requested","handler":71,"target":{"kind":"Any"}}"""));
    await runtime.NextScript();
    Assert.Equal(false, runtime.Context.HasEventListeners(eventName, "main"));
    var typed = await app.Handle.InvokeAsync("plugin:event|listen", JsonValue.Parse("""{"event":"tauri://close-requested","handler":72,"target":{"kind":"Window","label":"main"}}"""));
    await runtime.NextScript();
    Assert.Equal(true, runtime.Context.HasEventListeners(eventName, "main"));
    await app.Handle.InvokeAsync("plugin:event|unlisten", JsonValue.Parse($$"""{"event":"tauri://close-requested","eventId":{{typed}}}"""));
    Assert.Equal(false, runtime.Context.HasEventListeners(eventName, "main"));
}

static async Task EventNameAndUnlisten()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime).Build();
    app.Run();
    foreach (var name in new[] { "", "한글/事件:alpha_1-2", "Ⅷ²", "\u0345", "\u05B0", "\U00010400" })
        await app.Handle.InvokeAsync("plugin:event|emit", JsonValue.Parse($"{{\"event\":{JsonValue.From(name).GetRawText()}}}"));
    foreach (var name in new[] { "has space", "dot.name", "emoji😀", "\u0301", "\0", "line\n" })
    {
        try
        {
            await app.Handle.InvokeAsync("plugin:event|emit", JsonValue.Parse($"{{\"event\":{JsonValue.From(name).GetRawText()}}}"));
            throw new Exception("Invalid event was accepted.");
        }
        catch (InvokeException error) { Assert.Contains("invalid args `event` for command `emit`: Event name must include only", error.Message); }
    }
    using var empty = app.Handle.Events.Listen("", _ => { });
    var id = await app.Handle.InvokeAsync("plugin:event|listen", JsonValue.Parse("{\"event\":\"keep\",\"handler\":77,\"target\":{\"kind\":\"Any\"}}"));
    await runtime.NextScript();
    await app.Handle.InvokeAsync("plugin:event|unlisten", JsonValue.Parse($"{{\"event\":\"other\",\"eventId\":{id}}}"));
    app.Handle.Emit("keep", JsonValue.Null);
    Assert.Contains("runCallback(77,", await runtime.NextScript());
    await app.Handle.InvokeAsync("plugin:event|unlisten", JsonValue.Parse($"{{\"event\":\"keep\",\"eventId\":{id}}}"));
    app.Handle.Emit("keep", JsonValue.Null);
    Assert.Equal(0, runtime.PendingScripts);
}

static async Task LargeEventCallbacks()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime).Build();
    app.Run();
    app.Handle.AddJsEventListener("large", "main", 77, RuntimeEventTarget.Any);
    await runtime.NextScript();
    var payload = JsonValue.Parse("{\"__proto__\":{\"marker\":\"own data\"},\"text\":\"" + new string('x', 11000) + "\"}");
    app.Handle.Emit("large", payload);
    var script = await runtime.NextScript();
    Assert.Contains("runCallback(77,{", script);
    Assert.Equal(false, script.Contains("JSON.parse(", StringComparison.Ordinal));
    app.Handle.RunCallback("main", 78, payload);
    Assert.Contains("runCallback(78,JSON.parse('", await runtime.NextScript());
}

static Task AssetResolverIsExposed()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime)
        .ResolveAssets(request => request.Uri.AbsolutePath == "/index.html"
            ? new(200, "text/html", "ok"u8.ToArray()) : null)
        .Build();
    app.Run();
    var response = runtime.Context!.ResolveAsset(new(new Uri("tauri://localhost/index.html"), "GET", new Dictionary<string, string>()));
    Assert.Equal(200, response?.StatusCode);
    return Task.CompletedTask;
}

static Task EmbeddedAssetsResolve()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime).UseEmbeddedAssets().Build();
    app.Run();
    var response = runtime.Context!.ResolveAsset(new(new Uri("tauri://localhost/"), "GET", new Dictionary<string, string>()));
    Assert.Equal(200, response?.StatusCode);
    Assert.Equal("<!doctype html><title>embedded</title><script>window.embedded=true</script>\n", System.Text.Encoding.UTF8.GetString(response!.Body.Span).Replace("\r\n", "\n", StringComparison.Ordinal));
    var head = runtime.Context.ResolveAsset(new(new Uri("tauri://localhost/index.html"), "HEAD", new Dictionary<string, string>()));
    Assert.Equal(response.Body.Length, head?.Body.Length);
    var remote = runtime.Context.ResolveAsset(new(new Uri("https://untrusted.example/index.html"), "GET", new Dictionary<string, string>()));
    Assert.Equal<AssetResponse?>(null, remote);
    return Task.CompletedTask;
}

static async Task EmbeddedConfigApplies()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime).UseEmbeddedConfig().UseEmbeddedAssets()
        .RegisterCommand("scoped", (context, _, _) =>
        {
            Assert.Equal("$HOME/read", context.Scope.Allow[0].GetProperty("path").GetString());
            Assert.Equal("$HOME/private", context.Scope.Deny[0].GetProperty("path").GetString());
            return ValueTask.FromResult(JsonValue.From(true));
        })
        .AppCommandManifest("scoped")
        .Build();
    app.Run();
    Assert.Equal("Configured", app.Handle.Metadata.Name);
    Assert.Equal("configured", runtime.Context!.Windows.Single().Label);
    Assert.Contains("Object.freeze(Object.prototype)", runtime.Context.InitializationScript);
    Send(runtime, "plugin:app|name", "{}", new Uri("tauri://localhost"), "configured", "configured");
    Assert.Contains("runCallback(1,\"Configured\")", await runtime.NextScript());
    Send(runtime, "scoped", "{}", new Uri("tauri://localhost"), "configured", "configured");
    Assert.Contains("runCallback(1,true)", await runtime.NextScript());
    var response = runtime.Context.ResolveAsset(new(new Uri("tauri://localhost/index.html"), "GET", new Dictionary<string, string>()));
    Assert.Contains("default-src 'self'; img-src 'self' data:; script-src 'self' 'sha256-", response!.Headers!["Content-Security-Policy"]);
    Assert.Equal("same-origin", response.Headers["Cross-Origin-Opener-Policy"]);
}

static async Task EmbeddedPermissionFiles()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime).UseEmbeddedConfig(resourceName: "acl-files.json")
        .RegisterCommand("read", (context, _, _) =>
        {
            Assert.Equal(2, context.Scope.Deny.Count);
            Assert.Equal("private", context.Scope.Deny[0].GetString());
            return ValueTask.FromResult(JsonValue.From(string.Join(',', context.Scope.Allow.Select(value => value.GetString()))));
        })
        .RegisterCommand("write", (context, _, _) =>
        {
            Assert.Equal(0, context.Scope.Deny.Count);
            return ValueTask.FromResult(JsonValue.From(string.Join(',', context.Scope.Allow.Select(value => value.GetString()))));
        })
        .RegisterCommand("public_info", (_, _, _) => ValueTask.FromResult(JsonValue.From(true)))
        .RegisterCommand("windows_command", (_, _, _) => ValueTask.FromResult(JsonValue.From(true)))
        .RegisterCommand("linux_command", (_, _, _) => ValueTask.FromResult(JsonValue.From(true)))
        .RegisterCommand("macOS_command", (_, _, _) => ValueTask.FromResult(JsonValue.From(true)))
        .Build();
    app.Run();
    foreach (var command in new[] { "read", "write" })
    {
        Send(runtime, command, "{}", new Uri("tauri://localhost"));
        Assert.Contains($"runCallback(1,\"extension,{command},second,{command}\")", await runtime.NextScript());
    }
    Send(runtime, "public_info", "{}", new Uri("tauri://localhost"));
    Assert.Contains("runCallback(2,", await runtime.NextScript());
    Send(runtime, "read", "{}", new Uri("https://remote.example"));
    Assert.Contains("runCallback(2,", await runtime.NextScript());
    Send(runtime, "read", "{}", new Uri("tauri://localhost"), "other", "other");
    Assert.Contains("runCallback(2,", await runtime.NextScript());
    foreach (var platform in new[] { "windows", "linux", "macOS" })
    {
        Send(runtime, platform + "_command", "{}", new Uri("tauri://localhost"));
        var current = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macOS" : "linux";
        Assert.Contains(platform == current ? "runCallback(1,true)" : "runCallback(2,", await runtime.NextScript());
    }
    Send(runtime, "plugin:app|name", "{}", new Uri("tauri://localhost"));
    Assert.Contains("runCallback(1,", await runtime.NextScript());
}

static async Task EmbeddedPermissionBoundaries()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime).UseEmbeddedConfig(resourceName: "acl-empty.json")
        .RegisterCommand("local", (_, _, _) => ValueTask.FromResult(JsonValue.From(true))).Build();
    app.Run();
    Send(runtime, "local", "{}", new Uri("tauri://localhost"));
    Assert.Contains("runCallback(1,true)", await runtime.NextScript());
    Send(runtime, "local", "{}", new Uri("https://remote.example"));
    Assert.Contains("runCallback(2,", await runtime.NextScript());
    Assert.Throws<InvalidOperationException>(() => new Builder().UseRuntime(new FakeRuntime())
        .UseEmbeddedConfig(resourceName: "acl-cycle.json").Build());

    var shadowRuntime = new FakeRuntime();
    var shadowBuilder = new Builder().UseRuntime(shadowRuntime).UseEmbeddedConfig(resourceName: "acl-shadow.json");
    foreach (var command in new[] { "old", "new", "top", "nested" })
        shadowBuilder.RegisterCommand(command, (_, _, _) => ValueTask.FromResult(JsonValue.From(true)));
    using var shadow = shadowBuilder.Build();
    shadow.Run();
    foreach (var command in new[] { "old", "new", "top", "nested" })
    {
        Send(shadowRuntime, command, "{}", new Uri("tauri://localhost"));
        Assert.Contains(command == "old" ? "runCallback(2," : "runCallback(1,true)", await shadowRuntime.NextScript());
    }
}

static Task EmbeddedConfigDevelopmentMode()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime)
        .UseEmbeddedConfig(EmbeddedConfigMode.Development)
        .UseEmbeddedAssets()
        .AppCommandManifest("scoped")
        .Build();
    app.Run();
    Assert.Equal("http://localhost:1420/index.html", runtime.Context!.Windows.Single().Url);
    var response = runtime.Context.ResolveAsset(new(new Uri("tauri://localhost/index.html"), "GET", new Dictionary<string, string>()));
    Assert.Contains("default-src 'none'; script-src 'self' 'sha256-", response!.Headers!["Content-Security-Policy"]);
    return Task.CompletedTask;
}

static Task EmbeddedConfigRejectsExternalCapabilities()
{
    Assert.Throws<InvalidOperationException>(() => new Builder().UseEmbeddedConfig(resourceName: "netwiv.invalid.config.json"));
    return Task.CompletedTask;
}

static async Task PluginLifecycleRuns()
{
    var runtime = new FakeRuntime();
    var plugin = new TrackedPlugin();
    var app = new Builder().UseRuntime(runtime)
        .Plugin(plugin)
        .Permission(new("tracked:allow-ping", "plugin:tracked|ping"))
        .Capability(new("tracked", ["tracked:allow-ping"], Windows: ["main"]))
        .Build();
    Assert.Equal(0, plugin.SetupCount);
    app.Run();
    Assert.Equal(1, plugin.SetupCount);
    Send(runtime, "plugin:tracked|ping", "{}", new Uri("tauri://localhost"));
    Assert.Contains("runCallback(1,true)", await runtime.NextScript());
    runtime.Context!.OnEvent(new("tick", "main", JsonValue.From(1)));
    Assert.Equal(1, plugin.EventCount);
    app.Dispose();
    Assert.Equal(1, plugin.DisposeCount);
}

static Task SeparateLabelNamespaces()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime)
        .Window(new WindowOptions("main", "Main", "index.html", 800, 600, default,
            [new("main", "index.html", 0, 0, 400, 600, default), new("child", "index.html", 400, 0, 400, 600, default)]))
        .Window(new WindowOptions("empty", "Empty", "index.html", 400, 300, default, []))
        .Build();
    Assert.Equal(2, app.Handle.Webviews.Count);
    Assert.Throws<InvalidOperationException>(() => new Builder().UseRuntime(runtime)
        .Window("main", "Main", "index.html")
        .Window(new WindowOptions("second", "Second", "index.html", 400, 300, default,
            [new("main", "index.html", 0, 0, 400, 300, default)])).Build());
    return Task.CompletedTask;
}

static Task DestroyPreservesUnrelatedWebview()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime)
        .Window(new WindowOptions("owner", "Owner", "index.html", 400, 300, default,
            [new("other", "index.html", 0, 0, 400, 300, default)]))
        .Window(new WindowOptions("other", "Bare", "index.html", 400, 300, default, []))
        .Build();
    app.Run();
    runtime.Context!.OnEvent(new("tauri://destroyed", "other", JsonValue.Null, new("Window", "other")));
    Assert.Equal("other", app.Handle.GetWebview("other")!.Label);
    Assert.Equal(1, app.Handle.Windows.Count);
    Assert.Equal(0, app.Handle.WebviewWindows.Count);
    return Task.CompletedTask;
}

static Task TaggedTrayEvents()
{
    var runtime = new FakeRuntime();
    TrayIconEvent? received = null;
    using var app = new Builder().UseRuntime(runtime).OnTrayIconEvent((_, value) => received = value).Build();
    app.Run();
    foreach (var type in new[] { "Click", "DoubleClick", "Enter", "Move", "Leave" })
    {
        var button = type is "Click" or "DoubleClick" ? ",\"button\":\"Right\"" : "";
        var state = type is "Click" ? ",\"buttonState\":\"Down\"" : "";
        runtime.Context!.OnEvent(new("tray-action", null, JsonValue.Parse($$$$"""
            {"type":"{{{{type}}}}","id":"tray","position":{"x":12.5,"y":-34.25},
             "rect":{"position":{"Physical":{"x":11,"y":-31}},"size":{"Physical":{"width":16,"height":24}}}{{{{button}}}}{{{{state}}}}}
            """)));
        Assert.Equal(type, received!.Type.ToString());
        Assert.Equal(new PhysicalPosition<double>(12.5, -34.25), received.Position);
        Assert.Equal(new PhysicalPosition<double>(11, -31), received.Rect.Position);
        Assert.Equal(new PhysicalSize(16, 24), received.Rect.Size);
        Assert.Equal<MouseButton?>(type is "Click" or "DoubleClick" ? MouseButton.Right : null, received.Button);
        Assert.Equal<MouseButtonState?>(type is "Click" ? MouseButtonState.Down : null, received.ButtonState);
    }
    return Task.CompletedTask;
}

static Task NativeHooksAreTrusted()
{
    var runtime = new FakeRuntime();
    var menuIds = new List<string>();
    TrayIconEvent? tray = null;
    using var app = new Builder().UseRuntime(runtime)
        .OnMenuEvent((_, value) => menuIds.Add(value.Id))
        .OnTrayIconEvent((_, value) => tray = value).Build();
    app.OnMenuEvent((_, value) => menuIds.Add("app:" + value.Id));
    app.Handle.OnMenuEvent((_, value) => menuIds.Add("handle:" + value.Id));
    app.Run();
    app.Handle.Emit("menu-action", JsonValue.Parse("{\"id\":\"forged\"}"));
    Assert.Equal(0, menuIds.Count);
    runtime.Context!.OnEvent(new("menu-action", null, JsonValue.Parse("{\"id\":\"open\"}")));
    Assert.Equal("open,app:open,handle:open", string.Join(',', menuIds));
    runtime.Context.OnEvent(new("tray-action", null, JsonValue.Parse("""
        {"type":"Click","id":"tray","position":{"x":12.5,"y":34.25},"rect":{"position":{"x":10,"y":30},"size":{"width":16,"height":16}},"button":"Left","buttonState":"Up"}
        """)));
    Assert.Equal("tray", tray!.Id);
    Assert.Equal(new PhysicalPosition<double>(12.5, 34.25), tray.Position);
    Assert.Equal<MouseButtonState?>(MouseButtonState.Up, tray.ButtonState);
    app.Dispose();
    runtime.Context.OnEvent(new("menu-action", null, JsonValue.Parse("{\"id\":\"late\"}")));
    Assert.Equal(3, menuIds.Count);
    return Task.CompletedTask;
}

static async Task CapabilityLabelGlobs()
{
    (string Pattern, string Label, bool Allowed)[] cases =
    [
        ("tool-?", "tool-7", true), ("tool-?", "tool-77", false),
        ("tool-[0-9]", "tool-7", true), ("tool-[0-9]", "tool-x", false),
        ("tool-[!a-z]", "tool-7", true), ("tool-[!a-z]", "tool-q", false),
        ("tool-[]]", "tool-]", true), ("tool-[[]", "tool-[", true),
        ("a/**/b", "a/b", true), ("a/**/b", "a/nested/deep/b", true),
        ("**/b", "ab", false), ("**/b", "a/b", true),
        ("tool-?", "tool-🐂", true), ("tool-*", "TOOL-7", false)
    ];
    foreach (var (pattern, label, allowed) in cases)
    {
        var runtime = new FakeRuntime();
        using var app = new Builder().UseRuntime(runtime)
            .Capability(new("glob", ["core:app:allow-name"], Windows: [pattern])).Build();
        app.Run();
        Send(runtime, "plugin:app|name", "{}", new Uri("tauri://localhost"), window: label);
        Assert.Contains(allowed ? "runCallback(1," : "runCallback(2,", await runtime.NextScript());
    }
    foreach (var invalid in new[] {"[", "a**b", "***", "a/**b"})
        Assert.Throws<ArgumentException>(() => new Builder().UseRuntime(new FakeRuntime())
            .Capability(new("invalid", ["core:app:allow-name"], Windows: [invalid])).Build());
}

static async Task ReloadDiscardsPendingReplies()
{
    var runtime = new FakeRuntime();
    var pending = new TaskCompletionSource<JsonElement>();
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var app = new Builder().UseRuntime(runtime)
        .RegisterCommand("pending", (_, _, _) => { entered.TrySetResult(); return new ValueTask<JsonElement>(pending.Task); }).Build();
    app.Run();
    runtime.Context!.OnDocumentChanged!(new("main", 1, false));
    Send(runtime, "pending", "{}", new Uri("tauri://localhost"), documentId: 1);
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
    runtime.Context.OnDocumentChanged(new("main", 2, false));
    pending.SetResult(JsonValue.From("old document"));
    Assert.Equal(0, runtime.PendingScripts);
    Send(runtime, "pending", "{}", new Uri("tauri://localhost"), documentId: 2);
    Assert.Contains("old document", await runtime.NextScript());
    Assert.Equal(2UL, runtime.LastScriptDocument);
}

static async Task ReloadInvalidatesCallbacks()
{
    var runtime = new FakeRuntime();
    Channel? captured = null;
    using var app = new Builder().UseRuntime(runtime)
        .Capability(new("events", ["core:event:default"], Windows: ["main"]))
        .RegisterCommand("channel", (context, args, _) =>
        {
            captured = Channel.FromJson(context, args.GetProperty("channel"));
            return ValueTask.FromResult(JsonValue.Null);
        }).Build();
    app.Run();
    runtime.Context!.OnDocumentChanged!(new("main", 1, false));
    Send(runtime, "channel", "{\"channel\":\"__CHANNEL__:42\"}", new Uri("tauri://localhost"), documentId: 1);
    await runtime.NextScript();
    Send(runtime, "plugin:event|listen", "{\"event\":\"tick\",\"target\":{\"kind\":\"Any\"},\"handler\":43}", new Uri("tauri://localhost"), documentId: 1);
    await runtime.NextScript(); await runtime.NextScript();
    runtime.Context.OnDocumentChanged(new("main", 2, false));
    captured!.Send(JsonValue.From(1)); captured.Dispose();
    app.Handle.Emit("tick", JsonValue.From(1));
    Assert.Equal(0, runtime.PendingScripts);
    runtime.Context.OnDocumentChanged(new("main", 2, true));
    Send(runtime, "channel", "{\"channel\":\"__CHANNEL__:42\"}", new Uri("tauri://localhost"), documentId: 2);
    Assert.Equal(0, runtime.PendingScripts);
}

static async Task CallbackProperties(bool raw, bool result)
{
    var random = new Random(17321);
    var tokens = new[] { "a", "Z", "0", "'", "\\", "\"", "\n", "\r", "\t", "\0", "한", "🚀", "\u2028", "\u2029" };
    string Text() => string.Concat(Enumerable.Range(0, random.Next(97)).Select(_ => tokens[random.Next(tokens.Length)]));
    var payload = JsonValue.Null;
    var failed = false;
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime).RegisterCommand("property_result", (_, _, _) =>
        failed ? ValueTask.FromException<JsonElement>(new InvokeException(payload)) : ValueTask.FromResult(payload)).Build();
    app.Run();
    for (var index = 0; index < 10000; index++)
    {
        var text = JsonValue.From(Text()).GetRawText();
        payload = raw && random.Next(2) == 0 ? JsonValue.Parse($"{{ {JsonValue.From(Text()).GetRawText()}: {text} }}") : JsonValue.Parse(text);
        var success = (uint)random.NextInt64(0, 1L << 32);
        var error = (uint)random.NextInt64(0, 1L << 32);
        failed = random.Next(2) != 0;
        if (result) Send(runtime, "property_result", "{}", new Uri("tauri://localhost"), callback: success, error: error);
        else app.Handle.RunCallback("main", success, payload);
        var selected = result && failed ? error : success;
        Assert.Contains($"runCallback({selected},{payload.GetRawText()})", await runtime.NextScript());
    }
}

static async Task CrossPluginPermissions()
{
    var runtime = new FakeRuntime();
    using var app = new Builder().UseRuntime(runtime)
        .Plugin(new PermissionFixturePlugin("dialog")).Plugin(new PermissionFixturePlugin("http")).Plugin(new PermissionFixturePlugin("fs"))
        .Capability(new("main", ["dialog:extra"], Windows: ["main"]))
        .Build();
    app.Run();
    foreach (var command in new[] { "plugin:dialog|save", "plugin:fs|read", "plugin:fs|exist", "plugin:fs|write", "plugin:http|fetch", "plugin:http|fetch-cancel" })
    {
        Send(runtime, command, "{}", new Uri("tauri://localhost"));
        Assert.Contains("runCallback(1,", await runtime.NextScript());
    }
    foreach (var command in new[] { "plugin:dialog|open", "plugin:fs|rm" })
    {
        Send(runtime, command, "{}", new Uri("tauri://localhost"));
        Assert.Contains("runCallback(2,", await runtime.NextScript());
    }
}

static void Send(FakeRuntime runtime, string command, string payload, Uri source, string webview = "main", string? window = null, ulong documentId = 0, uint callback = 1, uint error = 2)
{
    const string marker = "__NETWIV_INVOKE_KEY__: '";
    var keyStart = runtime.Context!.InitializationScript.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
    var key = keyStart >= marker.Length ? runtime.Context.InitializationScript.Substring(keyStart, 64) : string.Empty;
    Assert.True(key.Length == 64, "Initialization script did not contain a 256-bit invocation key.");
    runtime.Context.OnMessage(new(webview, source,
        $"{{\"cmd\":\"{command}\",\"callback\":{callback},\"error\":{error},\"payload\":{payload},\"__NETWIV_INVOKE_KEY__\":\"{key}\"}}", window, documentId));
}

sealed class FakeRuntime : IDesktopRuntime
{
    private readonly ConcurrentQueue<string> _scripts = new();
    private readonly SemaphoreSlim _scriptReady = new(0);
    public RuntimeContext? Context { get; private set; }
    public RuntimeCommandContext? LastContext { get; private set; }
    public string? LastCommand { get; private set; }
    public JsonElement InvokeResult { get; init; } = JsonValue.Null;
    public Func<string, JsonElement, JsonElement>? InvokeHandler { get; init; }
    public int? ExitCode { get; private set; }
    public int PendingScripts => _scripts.Count;
    public ulong LastScriptDocument { get; private set; }
    public void Run(RuntimeContext context) { Context = context; context.OnReady?.Invoke(); }
    public void Dispatch(Action action) => action();
    public Task<JsonElement> InvokeAsync(string command, JsonElement args, CancellationToken cancellationToken = default) => Task.FromResult(InvokeHandler?.Invoke(command, args) ?? InvokeResult);
    public Task<JsonElement> InvokeAsync(string command, JsonElement args, RuntimeCommandContext context, CancellationToken cancellationToken = default)
    { LastCommand = command; LastContext = context; return Task.FromResult(InvokeHandler?.Invoke(command, args) ?? InvokeResult); }
    public void EvaluateScript(string webviewLabel, string script) { _scripts.Enqueue(script); _scriptReady.Release(); }
    public void EvaluateScript(string webviewLabel, string script, ulong documentId) { LastScriptDocument = documentId; EvaluateScript(webviewLabel, script); }
    public void Exit(int exitCode = 0) { ExitCode = exitCode; }
    public async Task<string> NextScript(TimeSpan? timeout = null)
    {
        if (!await _scriptReady.WaitAsync(timeout ?? TimeSpan.FromSeconds(2))) throw new TimeoutException("Core did not evaluate a callback.");
        return _scripts.TryDequeue(out var script) ? script : throw new InvalidOperationException();
    }
}

sealed class TrackedResource : IDisposable
{
    public int DisposeCount;
    public void Dispose() => Interlocked.Increment(ref DisposeCount);
}

sealed class TrackedPlugin : IPlugin
{
    public string Name => "tracked";
    public int EventCount;
    public int DisposeCount;
    public int SetupCount;
    public void Initialize(PluginContext context) => context.RegisterCommand("ping", static (_, _, _) => ValueTask.FromResult(JsonValue.From(true)));
    public void Setup(AppHandle app) => SetupCount++;
    public void OnEvent(AppHandle app, RuntimeEvent runtimeEvent) => EventCount++;
    public void Dispose() => DisposeCount++;
}

static class Assert
{
    public static void True(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    public static void Contains(string expected, string actual) { if (!actual.Contains(expected, StringComparison.Ordinal)) throw new InvalidOperationException($"Expected '{expected}' in '{actual}'."); }
    public static void DoesNotContain(string expected, string actual) { if (actual.Contains(expected, StringComparison.Ordinal)) throw new InvalidOperationException($"Did not expect '{expected}' in '{actual}'."); }
    public static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"Expected '{expected}', got '{actual}'."); }
    public static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new InvalidOperationException($"Expected {typeof(T).Name}."); }
}
