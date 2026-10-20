#if NETWIV_BLAZOR_HYBRID
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using NetWiv.BlazorHybrid.Aot;
using NetWiv.BlazorHybrid.Shared;

namespace NetWiv.BlazorHybrid.Desktop;

internal static class HybridUiRegistration
{
    internal static void Configure(BlazorAotOptions options)
    {
        options.JsonTypeInfoResolver = UiInteropJsonContext.Default;
        options.Components.Register<Shared.App>();
        options.Components.Register<EditForm>();
        options.Components.Register<InputText>();
        options.Components.Register<ValidationSummary>();
        options.Components.Register<CascadingValue<EditContext>>();
        options.Interop.RegisterInstance<UiInteropCallbacks, UiInteropRequest, UiInteropReply>(
            nameof(UiInteropCallbacks.DescribeAsync),
            static (callbacks, request) => new(callbacks.DescribeAsync(request)),
            UiInteropJsonContext.Default.UiInteropRequest, UiInteropJsonContext.Default.UiInteropReply);
    }
}

internal sealed class HybridUiInteropReferenceFactory : IUiInteropReferenceFactory
{
    public IUiInteropReference Create(UiInteropCallbacks callbacks) => new Reference(BlazorAotObjectReference.Create(callbacks));

    private sealed class Reference(BlazorAotObjectReference<UiInteropCallbacks> reference) : IUiInteropReference
    {
        public object Value => reference;
        public void Dispose() => reference.Dispose();
    }
}
#endif
