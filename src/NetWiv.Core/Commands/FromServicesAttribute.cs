namespace NetWiv;

/// <summary>
/// Binds a command parameter from the invocation's service scope instead of the guest arguments.
/// Use <see cref="Microsoft.Extensions.DependencyInjection.FromKeyedServicesAttribute"/> for keyed services.
/// </summary>
/// <remarks>
/// Service parameters are never read from JSON and are omitted from generated frontend bindings.
/// A nullable parameter resolves to null when the service is not registered.
/// </remarks>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class FromServicesAttribute : Attribute;
