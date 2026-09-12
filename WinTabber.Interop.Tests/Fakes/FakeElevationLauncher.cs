namespace WinTabber.Interop.Tests.Fakes;

public sealed class FakeElevationLauncher : IElevationLauncher
{
    public bool IsAvailable { get; set; } = true;
    public List<(ElevatedWindowAction Action, IReadOnlyList<int> Handles)> Calls { get; } = [];

    public void RunElevated(ElevatedWindowAction action, IEnumerable<int> handles) =>
        Calls.Add((action, handles.ToList()));
}
