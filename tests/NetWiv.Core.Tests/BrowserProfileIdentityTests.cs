using NetWiv;

internal static class BrowserProfileIdentityTests
{
    internal static Task RuntimeContext()
    {
        Check(new Builder(), true);
        Check(new Builder().Metadata(new("Default", "1.0", "COM.NETWIV.APP")), true);
        Check(new Builder().Metadata(new("Configured", "1.0", "dev.netwiv.profile")), false);
        Check(new Builder().UseEmbeddedConfig(resourceName: "no-windows.json"), true);
        Check(new Builder().Permission(new("allow-scoped", "scoped")).UseEmbeddedConfig(), false);
        return Task.CompletedTask;

        static void Check(Builder builder, bool expected)
        {
            using var app = builder.UseRuntime(new FakeRuntime()).Build();
            var context = app.Handle.CreateRuntimeContext();
            Assert.Equal(expected, context.UsesDefaultAppIdentifier);
            Assert.Equal(expected, (context with { InitializationScript = "replacement" }).UsesDefaultAppIdentifier);
            Assert.Equal(Path.Combine(context.LocalDataDirectory!, app.Handle.Metadata.Identifier), context.AppLocalDataDirectory);
        }
    }
}
