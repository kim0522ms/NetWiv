using NetWiv;
using NetWiv.Platform.Wry;
using System.Text.Json.Serialization;

[assembly: CommandJsonContext(typeof(AppJsonContext))]
[assembly: GenerateFrontendBindings]

internal static class Program
{
  [STAThread]
  private static void Main()
  {
    new Builder()
      .UseRuntime(new WryDesktopRuntime())
      .InvokeHandler("greet")
      .Window("main", "__WINDOW_TITLE__", "index.html")
#if NETWIV_DEV
      .UseEmbeddedConfig(EmbeddedConfigMode.Development)
#else
      .UseEmbeddedConfig()
#endif
      .UseEmbeddedAssets()
      .Run();
  }
}

public static class Commands
{
  [Command]
  public static string Greet(string name) => $"Hello, {name}!";
}

[JsonSerializable(typeof(string))]
public partial class AppJsonContext : JsonSerializerContext;
