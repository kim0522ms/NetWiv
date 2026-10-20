# NativeAOT renderer transport

The host derives from the official `Microsoft.AspNetCore.Components.RenderTree.Renderer`.
Microsoft's renderer continues to own component state, diffing, event handler
identity, dispatch and component lifecycle. Each native document creates a new
renderer, interop runtime and asynchronous service scope.

The following MIT-licensed ASP.NET Core sources are pinned to .NET 8.0.32,
commit `7413d24330774263a78a4f3837ca53493518455a`:

- `src/Components/Shared/src/RenderBatchWriter.cs`: copied binary serializer;
  the namespace and string storage use local types and `List<string>`.
- `src/Components/Web/src/WebEventData/*Reader.cs`: copied manual readers for
  built-in DOM event argument types, with namespace/import changes. These retain
  the string, boolean and string-array values required by `@bind`.
- `src/Components/Web/src/WebEventData/WebEventData.cs`,
  `WebEventDescriptorReader.cs` and `src/Components/Web/src/Web/EventHandlers.cs`:
  adapted event name dispatch and descriptor parsing. Custom event names fail
  explicitly instead of selecting a serializer through reflection.
- `src/Components/Web/src/WebRenderer.cs`,
  `src/Components/WebView/WebView/src/Services/WebViewRenderer.cs`,
  `WebViewNavigationManager.cs` and `PageContext.cs`: adapted transport/bootstrap,
  root attachment, acknowledgments, navigation and page scope ownership.

Per-file .NET Foundation attribution is retained. The full MIT license is in
`licenses/dotnet-runtime-MIT.txt` at the repository root.

Component construction uses explicit metadata-preserving registrations and
direct factories. There is no component assembly discovery. Routing applications
must use statically declared components; registering the framework Router is
rejected because its assembly discovery is unsupported. Navigation stays within the
bundled application base URI. At most 256 render batches can await browser
acknowledgment; exceeding this bound fails the active session.

Navigation locks are unsupported: .NET 8's `NavigationManager` exposes no hook
to cancel a location-changing handler when its document closes. The first
`RegisterLocationChangingHandler` registration throws, and the host never invokes
the base handler list, including after a caller catches that exception. Use
`NavigateTo` and `LocationChanged` with statically registered views instead.
