using System.Diagnostics;
using WinTabber.Api.Windowing.Tests.Fakes;
using WinTabber.Interop;

namespace WinTabber.Api.Windowing.Tests;

public class ApplicationRefTests
{
    [Test]
    public async Task CloseAllWindows_ClosesNonElevatedDirectly_AndBatchesElevatedIntoOneCall()
    {
        var interop = new FakeWindowInterop();
        var manager = new WindowManager(interop, new FakeProcessRepository());
        var application = new ApplicationRef("app", manager);

        // Two windows on one elevated process, one window on a separate, non-elevated process —
        // Process.GetCurrentProcess() returns a fresh object each call, which is all that's
        // needed since FakeWindowInterop compares by reference, not PID.
        var elevatedProcess = Process.GetCurrentProcess();
        var normalProcess = Process.GetCurrentProcess();
        interop.ElevatedProcesses.Add(elevatedProcess);

        var elevatedProcessRef = new WindowProcessRef(elevatedProcess, application);
        var normalProcessRef = new WindowProcessRef(normalProcess, application);

        var windowA = new WindowRef(101, elevatedProcessRef);
        var windowB = new WindowRef(102, elevatedProcessRef);
        var windowC = new WindowRef(201, normalProcessRef);

        application.CloseAllWindows([windowA, windowB, windowC]);

        await Assert.That(interop.ClosedHandles.Count).IsEqualTo(1);
        await Assert.That(interop.ClosedHandles).Contains(201);
        await Assert.That(interop.ElevatedActionCalls.Count).IsEqualTo(1);
        await Assert.That(interop.ElevatedActionCalls[0].Action).IsEqualTo(ElevatedWindowAction.Close);
        await Assert.That(interop.ElevatedActionCalls[0].Handles.Count).IsEqualTo(2);
        await Assert.That(interop.ElevatedActionCalls[0].Handles).Contains(101);
        await Assert.That(interop.ElevatedActionCalls[0].Handles).Contains(102);
    }

    [Test]
    public async Task CloseAllWindows_NoElevatedWindows_NeverCallsRunElevatedAction()
    {
        var interop = new FakeWindowInterop();
        var manager = new WindowManager(interop, new FakeProcessRepository());
        var application = new ApplicationRef("app", manager);

        var normalProcessRef = new WindowProcessRef(Process.GetCurrentProcess(), application);
        var window = new WindowRef(301, normalProcessRef);

        application.CloseAllWindows([window]);

        await Assert.That(interop.ClosedHandles).Contains(301);
        await Assert.That(interop.ElevatedActionCalls.Count).IsEqualTo(0);
    }
}
