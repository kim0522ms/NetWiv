using System.Diagnostics;

namespace NetWiv;

public sealed partial class AppHandle
{
    private ProcessStartInfo? _restart;

    /// <summary>Requests exit and relaunch after Run returns and application resources have been disposed.</summary>
    public void Restart()
    {
        lock (_invocationGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_restart is not null) return;
            var executable = Environment.ProcessPath ?? throw new InvokeException("The executable path is unavailable.");
            var original = Environment.GetCommandLineArgs();
            var info = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
            if (string.Equals(Path.GetFileNameWithoutExtension(executable), "dotnet", StringComparison.OrdinalIgnoreCase))
                info.ArgumentList.Add(original[0]);
            foreach (var argument in original.Skip(1)) info.ArgumentList.Add(argument);
            _restart = info;
            try { Exit(); }
            catch { _restart = null; throw; }
        }
    }

    internal ProcessStartInfo? TakeRestart()
    {
        lock (_invocationGate)
        {
            var result = _restart;
            _restart = null;
            return result;
        }
    }
}
