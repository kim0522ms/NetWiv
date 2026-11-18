namespace NetWiv.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var stop = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, signal) => { signal.Cancel = true; stop.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            return await CliApp.RunAsync(args, Console.Out, Console.Error, Directory.GetCurrentDirectory(),
                Console.IsInputRedirected || Console.IsOutputRedirected ? null : Console.In, stop.Token);
        }
        finally { Console.CancelKeyPress -= cancel; }
    }
}
