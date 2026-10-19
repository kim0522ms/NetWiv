# NetWiv plugin

This plugin template belongs to the independent NetWiv project.

`[Command]` generates the `ping` handler. `Initialize` explicitly selects it with
`RegisterGeneratedCommands<__PLUGIN_PASCAL__Plugin>("ping")`. The existing
`[Plugin]` attribute generates a plugin factory; the consuming application must
reference `NetWiv.Generators` as an analyzer to discover it from this assembly.
Grant `__PLUGIN_NAME__:default` in the application's capability before calling
`invoke('plugin:__PLUGIN_NAME__|ping')` from `@netwiv/api/core`.

Additional commands are selected by their local external names. For typed
arguments/results, add a System.Text.Json source-generation context and an
assembly-level `CommandJsonContext` attribute in this plugin project.

TAURI® is a trademark of The Tauri Programme within the Commons Conservancy.

NetWiv is an independent project. Its framework, plugins and templates are not affiliated with, endorsed by, or officially approved by The Tauri Programme within the Commons Conservancy.
