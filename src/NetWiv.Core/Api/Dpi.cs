namespace NetWiv;

/// <summary>A typed physical position, corresponding to Tauri's PhysicalPosition&lt;T&gt;.</summary>
public readonly record struct PhysicalPosition<T>(T X, T Y) where T : System.Numerics.INumber<T>
{
    public LogicalPosition ToLogical(double scaleFactor)
    {
        DpiValidation.Scale(scaleFactor);
        return new(double.CreateChecked(X) / scaleFactor, double.CreateChecked(Y) / scaleFactor);
    }
}

public interface IDpiSize
{
    LogicalSize ToLogical(double scaleFactor);
    PhysicalSize ToPhysical(double scaleFactor);
}

public readonly record struct LogicalSize : IDpiSize
{
    public LogicalSize(double width, double height)
    {
        Width = width;
        Height = height;
    }

    public double Width { get; }
    public double Height { get; }

    public LogicalSize ToLogical(double scaleFactor)
    {
        DpiValidation.Scale(scaleFactor);
        return this;
    }

    public PhysicalSize ToPhysical(double scaleFactor)
    {
        DpiValidation.Scale(scaleFactor);
        return new(
            DpiValidation.ToUInt(Width * scaleFactor),
            DpiValidation.ToUInt(Height * scaleFactor));
    }
}

public readonly record struct PhysicalSize(uint Width, uint Height) : IDpiSize
{
    public LogicalSize ToLogical(double scaleFactor)
    {
        DpiValidation.Scale(scaleFactor);
        return new(Width / scaleFactor, Height / scaleFactor);
    }

    public PhysicalSize ToPhysical(double scaleFactor)
    {
        DpiValidation.Scale(scaleFactor);
        return this;
    }
}

public interface IDpiPosition
{
    LogicalPosition ToLogical(double scaleFactor);
    PhysicalPosition ToPhysical(double scaleFactor);
}

public readonly record struct LogicalPosition : IDpiPosition
{
    public LogicalPosition(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double X { get; }
    public double Y { get; }

    public LogicalPosition ToLogical(double scaleFactor)
    {
        DpiValidation.Scale(scaleFactor);
        return this;
    }

    public PhysicalPosition ToPhysical(double scaleFactor)
    {
        DpiValidation.Scale(scaleFactor);
        return new(
            DpiValidation.ToInt(X * scaleFactor),
            DpiValidation.ToInt(Y * scaleFactor));
    }
}

public readonly record struct PhysicalPosition(int X, int Y) : IDpiPosition
{
    public LogicalPosition ToLogical(double scaleFactor)
    {
        DpiValidation.Scale(scaleFactor);
        return new(X / scaleFactor, Y / scaleFactor);
    }

    public PhysicalPosition ToPhysical(double scaleFactor)
    {
        DpiValidation.Scale(scaleFactor);
        return this;
    }
}

internal static class DpiValidation
{
    internal static void Scale(double value)
    {
        if (!double.IsNormal(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Scale factor must be positive and normal.");
        }
    }

    internal static void Finite(double value, string name)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(name, "DPI coordinates must be finite.");
        }
    }

    // dpi::Pixel rounds ties away from zero, then uses Rust's saturating float
    // cast. JavaScript's DPI objects instead retain their fractional numbers.
    internal static int ToInt(double value)
    {
        if (double.IsNaN(value)) return 0;
        return (int)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);
    }

    internal static uint ToUInt(double value)
    {
        if (double.IsNaN(value)) return 0;
        return (uint)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, uint.MaxValue);
    }
}
