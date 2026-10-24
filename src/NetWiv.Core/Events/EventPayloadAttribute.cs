namespace NetWiv;

/// <summary>
/// Exports an event payload in the generated TypeScript EventPayloads map.
/// Register the payload in the assembly's CommandJsonContext and serialize events
/// with that context's JsonTypeInfo. This attribute does not grant event permissions.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class EventPayloadAttribute(string eventName, Type payloadType) : Attribute
{
    public string EventName { get; } = eventName ?? throw new ArgumentNullException(nameof(eventName));
    public Type PayloadType { get; } = payloadType ?? throw new ArgumentNullException(nameof(payloadType));
}
