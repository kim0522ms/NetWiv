using NetWiv;
using System.Text.Json.Serialization;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(PolyWireText), "text")]
[JsonDerivedType(typeof(PolyWireCount), -7)]
public abstract class PolyWire { public required string Name { get; set; } }
public sealed class PolyWireText : PolyWire
{
    public required string Text { get; set; }
    public PolyWire? Next { get; set; }
}
public sealed class PolyWireCount : PolyWire { public required int Count { get; set; } }
public static class PolymorphicWireCommands
{
    [Command] public static PolyWire? PolymorphicProfile(PolyWire? value) => value;
}
