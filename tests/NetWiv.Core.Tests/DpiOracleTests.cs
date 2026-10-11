using System.Globalization;
using System.Text.Json;
using NetWiv;

internal static class DpiOracleTests
{
    internal static Task Conversions()
    {
        using var oracle = Read();
        foreach (var vector in oracle.RootElement.GetProperty("conversions").EnumerateArray())
        {
            var value = Number(vector, "valueBits");
            var scale = Number(vector, "scaleBits");
            var position = new LogicalPosition(value, value).ToPhysical(scale);
            var size = new LogicalSize(value, value).ToPhysical(scale);
            Assert.Equal(vector.GetProperty("position").GetInt32(), position.X);
            Assert.Equal(position.X, position.Y);
            Assert.Equal(vector.GetProperty("size").GetUInt32(), size.Width);
            Assert.Equal(size.Width, size.Height);
        }
        return Task.CompletedTask;
    }

    internal static Task ScaleValidation()
    {
        using var oracle = Read();
        foreach (var vector in oracle.RootElement.GetProperty("scaleValidation").EnumerateArray())
        {
            var scale = Number(vector, "bits");
            if (vector.GetProperty("valid").GetBoolean())
            {
                DpiValidation.Scale(scale);
                Assert.Equal(1 / scale, new PhysicalPosition(1, 1).ToLogical(scale).X);
                Assert.Equal(1 / scale, new PhysicalSize(1, 1).ToLogical(scale).Width);
            }
            else
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new LogicalPosition(1, 1).ToPhysical(scale));
                Assert.Throws<ArgumentOutOfRangeException>(() => new LogicalSize(1, 1).ToPhysical(scale));
                Assert.Throws<ArgumentOutOfRangeException>(() => new PhysicalPosition(1, 1).ToLogical(scale));
                Assert.Throws<ArgumentOutOfRangeException>(() => new PhysicalSize(1, 1).ToLogical(scale));
            }
        }
        return Task.CompletedTask;
    }

    private static double Number(JsonElement value, string property) => BitConverter.UInt64BitsToDouble(
        ulong.Parse(value.GetProperty(property).GetString()!, NumberStyles.HexNumber, CultureInfo.InvariantCulture));

    private static JsonDocument Read()
    {
        using var stream = typeof(DpiOracleTests).Assembly.GetManifestResourceStream("netwiv.tests.dpi-0.1.2.json")
            ?? throw new InvalidOperationException("Missing pinned DPI oracle.");
        return JsonDocument.Parse(stream);
    }
}
