namespace WinTabber.Interop.Tests.Fakes;

public sealed class FakeElevationBackendProvider : IElevationBackendProvider
{
    public ElevationBackend Backend { get; set; } = ElevationBackend.BuiltIn;
}
