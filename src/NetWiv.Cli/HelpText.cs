namespace NetWiv.Cli;

internal static class HelpText
{
    public const string Root = """
netwiv - build .NET desktop applications with the Tauri v2 JavaScript API

Usage: netwiv <COMMAND> [OPTIONS]

Commands:
  new           Create a new desktop app with SDK, bindings and frontend
  init          Initialize a NetWiv project in an existing directory
  dev           Run the application in managed development mode
  build         NativeAOT-publish and optionally bundle the application
  bindings      Export generated JavaScript and TypeScript declarations
  bundle        Bundle an already published application
  info          Show environment and project information
  add/remove    Add or remove a .NET NetWiv plugin package
  plugin        Create or initialize a .NET NetWiv plugin
  icon          Generate desktop icons
  signer        Generate keys or sign updater artifacts with minisign
  completions   Generate shell completions
  permission    Manage application permissions
  capability    Manage application capabilities
  inspect       Inspect derived build values

Options:
  -h, --help       Print help
  -V, --version    Print version
""";
}
