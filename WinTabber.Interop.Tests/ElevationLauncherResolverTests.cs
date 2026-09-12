using WinTabber.Interop.Tests.Fakes;

namespace WinTabber.Interop.Tests;

public class ElevationLauncherResolverTests
{
    [Test]
    public async Task RunElevated_BackendBuiltIn_AlwaysUsesBuiltInLauncher()
    {
        var builtIn = new FakeElevationLauncher();
        var gsudo = new FakeElevationLauncher { IsAvailable = true };
        var backendProvider = new FakeElevationBackendProvider { Backend = ElevationBackend.BuiltIn };
        var resolver = new ElevationLauncherResolver(builtIn, gsudo, backendProvider);

        resolver.RunElevated(ElevatedWindowAction.Close, [123]);

        await Assert.That(builtIn.Calls.Count).IsEqualTo(1);
        await Assert.That(gsudo.Calls.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RunElevated_BackendGsudoAndAvailable_UsesGsudoLauncher()
    {
        var builtIn = new FakeElevationLauncher();
        var gsudo = new FakeElevationLauncher { IsAvailable = true };
        var backendProvider = new FakeElevationBackendProvider { Backend = ElevationBackend.Gsudo };
        var resolver = new ElevationLauncherResolver(builtIn, gsudo, backendProvider);

        resolver.RunElevated(ElevatedWindowAction.Minimize, [456]);

        await Assert.That(gsudo.Calls.Count).IsEqualTo(1);
        await Assert.That(gsudo.Calls[0].Action).IsEqualTo(ElevatedWindowAction.Minimize);
        await Assert.That(builtIn.Calls.Count).IsEqualTo(0);
    }

    [Test]
    public async Task RunElevated_BackendGsudoButNotAvailable_FallsBackToBuiltInLauncher()
    {
        var builtIn = new FakeElevationLauncher();
        var gsudo = new FakeElevationLauncher { IsAvailable = false };
        var backendProvider = new FakeElevationBackendProvider { Backend = ElevationBackend.Gsudo };
        var resolver = new ElevationLauncherResolver(builtIn, gsudo, backendProvider);

        resolver.RunElevated(ElevatedWindowAction.Close, [789]);

        await Assert.That(builtIn.Calls.Count).IsEqualTo(1);
        await Assert.That(gsudo.Calls.Count).IsEqualTo(0);
    }
}
