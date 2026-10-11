namespace NetWiv;

/// <summary>Exports frontend bindings for generated commands without exposing them to guests.</summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class GenerateFrontendBindingsAttribute : Attribute;
