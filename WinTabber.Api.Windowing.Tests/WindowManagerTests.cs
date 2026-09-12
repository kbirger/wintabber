using System.Diagnostics;
using WinTabber.Api.Windowing.Tests.Fakes;
using WinTabber.Interop;

namespace WinTabber.Api.Windowing.Tests;

public class WindowManagerTests
{
    [Test]
    public async Task MinimizeWindows_MinimizesNonElevatedDirectly_AndBatchesElevatedIntoOneCall()
    {
        var interop = new FakeWindowInterop();
        var manager = new WindowManager(interop, new FakeProcessRepository());
        var application = new ApplicationRef("app", manager);

        // Two windows on one elevated process, one window on a separate, non-elevated process —
        // proves the elevated handles are genuinely batched into one call, not called once per
        // window (mirrors ApplicationRefTests.CloseAllWindows_ClosesNonElevatedDirectly_AndBatchesElevatedIntoOneCall).
        var elevatedProcess = Process.GetCurrentProcess();
        var normalProcess = Process.GetCurrentProcess();
        interop.ElevatedProcesses.Add(elevatedProcess);

        var elevatedProcessRef = new WindowProcessRef(elevatedProcess, application);
        var normalProcessRef = new WindowProcessRef(normalProcess, application);

        var windowA = new WindowRef(401, elevatedProcessRef);
        var windowB = new WindowRef(402, elevatedProcessRef);
        var windowC = new WindowRef(403, normalProcessRef);

        manager.MinimizeWindows([windowA, windowB, windowC]);

        await Assert.That(interop.MinimizedHandles.Count).IsEqualTo(1);
        await Assert.That(interop.MinimizedHandles).Contains(403);
        await Assert.That(interop.ElevatedActionCalls.Count).IsEqualTo(1);
        await Assert.That(interop.ElevatedActionCalls[0].Action).IsEqualTo(ElevatedWindowAction.Minimize);
        await Assert.That(interop.ElevatedActionCalls[0].Handles.Count).IsEqualTo(2);
        await Assert.That(interop.ElevatedActionCalls[0].Handles).Contains(401);
        await Assert.That(interop.ElevatedActionCalls[0].Handles).Contains(402);
    }

    [Test]
    public async Task MinimizeWindows_NoElevatedWindows_NeverCallsRunElevatedAction()
    {
        var interop = new FakeWindowInterop();
        var manager = new WindowManager(interop, new FakeProcessRepository());
        var application = new ApplicationRef("app", manager);

        var normalProcessRef = new WindowProcessRef(Process.GetCurrentProcess(), application);
        var window = new WindowRef(501, normalProcessRef);

        manager.MinimizeWindows([window]);

        await Assert.That(interop.MinimizedHandles).Contains(501);
        await Assert.That(interop.ElevatedActionCalls.Count).IsEqualTo(0);
    }
}
