using System.Text.Json;

namespace NetWiv.Plugin.Positioner;

/// <summary>Moves windows to official screen and tray anchor positions.</summary>
public sealed class PositionerPlugin : IPlugin
{
    private readonly object _trayGate = new();
    private (double X, double Y, double Width, double Height)? _tray;

    public string Name => "positioner";

    public void Initialize(PluginContext context)
    {
        context.RegisterCommand("move_window", (invoke, args, token) => MoveAsync(invoke, args, false, token));
        context.RegisterCommand("move_window_constrained", (invoke, args, token) => MoveAsync(invoke, args, true, token));
        context.RegisterCommand("set_tray_icon_state", (_, args, _) =>
        {
            var position = args.GetProperty("position");
            var size = args.GetProperty("size");
            var rect = (ReadNumber(position, "x"), ReadNumber(position, "y"), ReadNumber(size, "width"), ReadNumber(size, "height"));
            if (rect.Item3 < 0 || rect.Item4 < 0) throw new InvokeException("Tray size cannot be negative.");
            lock (_trayGate) _tray = rect;
            return ValueTask.FromResult(JsonValue.Null);
        });
        foreach (var command in new[] { "move_window", "move_window_constrained", "set_tray_icon_state" })
        {
            var permission = command.Replace('_', '-');
            context.Permission(new("allow-" + permission, [command], []));
            context.Permission(new("deny-" + permission, [], [command]));
        }
        context.PermissionSet("default", "allow-move-window", "allow-move-window-constrained", "allow-set-tray-icon-state");
    }

    private async ValueTask<JsonElement> MoveAsync(InvokeContext invoke, JsonElement args, bool constrained, CancellationToken token)
    {
        if (!args.TryGetProperty("position", out var value) || value.ValueKind != JsonValueKind.Number ||
            !value.TryGetInt32(out var anchor) || anchor is < 0 or > 14)
            throw new InvokeException("Unknown window position.");
        var window = invoke.App.GetWindow(invoke.WindowLabel) ?? throw new InvokeException("Window not found.");
        var monitor = await window.CurrentMonitorAsync(token) ?? throw new InvokeException("No monitor found for the window.");
        var size = await window.OuterSizeAsync(token);
        var x = (double)monitor.Position.X;
        var y = (double)monitor.Position.Y;
        var width = (double)monitor.Size.Width;
        var height = (double)monitor.Size.Height;
        var windowWidth = (double)size.Width;
        var windowHeight = (double)size.Height;
        if (anchor < 9)
        {
            x += anchor switch { 1 or 3 or 7 => width - windowWidth, 4 or 5 or 8 => Math.Truncate(width / 2) - Math.Truncate(windowWidth / 2), _ => 0 };
            y += anchor switch { 2 or 3 or 5 => height - windowHeight, 6 or 7 or 8 => Math.Truncate(height / 2) - Math.Truncate(windowHeight / 2), _ => 0 };
        }
        else
        {
            (double X, double Y, double Width, double Height)? tray;
            lock (_trayGate) tray = _tray;
            if (tray is null) throw new InvokeException("Tray position not set.");
            var rect = tray.Value;
            x = rect.X + (anchor switch { 11 or 12 => rect.Width, 13 or 14 => Math.Truncate(rect.Width / 2) - Math.Truncate(windowWidth / 2), _ => 0 });
            y = anchor is 10 or 12 or 14 ? rect.Y : rect.Y - windowHeight;
            if (OperatingSystem.IsWindows() && anchor is 9 or 11 or 13 && y < 0)
                y = rect.Y + rect.Height;
            if (constrained)
            {
                var trayMonitor = await window.MonitorFromPointAsync(rect.X, rect.Y, token);
                if (trayMonitor is not null)
                {
                    x = Math.Clamp(x, trayMonitor.Position.X,
                        Math.Max(trayMonitor.Position.X, (double)trayMonitor.Position.X + trayMonitor.Size.Width - windowWidth));
                    y = Math.Clamp(y, trayMonitor.Position.Y,
                        Math.Max(trayMonitor.Position.Y, (double)trayMonitor.Position.Y + trayMonitor.Size.Height - windowHeight));
                }
            }
        }
        await window.SetPositionAsync(new PhysicalPosition(ToCoordinate(x), ToCoordinate(y)), token);
        return JsonValue.Null;
    }

    private static double ReadNumber(JsonElement value, string name)
    {
        if (!value.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.Number ||
            !property.TryGetDouble(out var number) || !double.IsFinite(number))
            throw new InvokeException($"Invalid tray {name}.");
        return number;
    }

    private static int ToCoordinate(double value) => (int)Math.Clamp(value, int.MinValue, int.MaxValue);
}
