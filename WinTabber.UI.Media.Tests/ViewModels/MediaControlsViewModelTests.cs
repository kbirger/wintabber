using System.Reactive.Concurrency;
using ReactiveUI;
using WinTabber.UI.Media.Tests.Fakes;
using WinTabber.UI.Media.ViewModels;
using WinTabber.UI.Media.ViewModels.Factories;

namespace WinTabber.UI.Media.Tests.ViewModels;

/// <summary>
/// T6.6 restored <see cref="MediaControlsViewModel"/>'s commented-out <c>WhenActivated</c>, so
/// the session pipeline and the two device selectors now start on activation instead of
/// construction, and tear down on deactivation instead of only at process-exit disposal. Before
/// T6.1 there was no way to construct this view model in a test at all — every dependency was a
/// concrete, COM-reaching service.
/// </summary>
/// <remarks>
/// <c>NotInParallel</c>: <see cref="RxSchedulers.MainThreadScheduler"/> is global, and this view
/// model observes on it.
/// </remarks>
[NotInParallel]
public class MediaControlsViewModelTests
{
    private static MediaControlsViewModel Create(FakeAudioDeviceService deviceService)
    {
        // No dispatcher here, so the default main-thread scheduler would never pump.
        RxSchedulers.MainThreadScheduler = CurrentThreadScheduler.Instance;
        return new MediaControlsViewModel(
            new FakeMediaSessionService(),
            new FakeMediaControlsStateService(),
            new MediaSessionViewModelFactory(new FakeAudioSessionService(), deviceService),
            new AudioDeviceSelectorViewModelFactory(deviceService),
            // Stored nowhere: MediaControlsViewModel takes this but has no field for it.
            null!
        );
    }

    [Test]
    public async Task Construction_DoesNotCreateDeviceSelectorsOrActiveSession()
    {
        var deviceService = new FakeAudioDeviceService();
        var vm = Create(deviceService);

        await Assert.That(vm.Playback).IsNull();
        await Assert.That(vm.Recording).IsNull();
        await Assert.That(vm.ActiveSession).IsNull();
        await Assert.That(deviceService.LiveDefaultDeviceSubscriptions).IsEqualTo(0);
    }

    [Test]
    public async Task Activate_CreatesDeviceSelectorsAndActiveSession()
    {
        var deviceService = new FakeAudioDeviceService();
        var vm = Create(deviceService);

        vm.Activator.Activate();

        await Assert.That(vm.Playback).IsNotNull();
        await Assert.That(vm.Recording).IsNotNull();
        await Assert.That(vm.ActiveSession).IsNotNull();
        await Assert.That(deviceService.LiveDefaultDeviceSubscriptions).IsEqualTo(2);
    }

    /// <summary>
    /// The reactivation hazard this fix has to avoid: MediaControlsWindow now activates the view
    /// model on every show and deactivates it on every hide, so the WhenActivated block runs
    /// repeatedly over the view model's life. Disposing into the per-activation bag rather than a
    /// permanent composite is what keeps a second activation from stacking on top of the first.
    /// </summary>
    [Test]
    public async Task Deactivate_ThenReactivate_DoesNotLeakDeviceSelectors()
    {
        var deviceService = new FakeAudioDeviceService();
        var vm = Create(deviceService);

        vm.Activator.Activate();
        await Assert.That(deviceService.LiveDefaultDeviceSubscriptions).IsEqualTo(2);

        vm.Activator.Deactivate();
        await Assert.That(deviceService.LiveDefaultDeviceSubscriptions).IsEqualTo(0);

        vm.Activator.Activate();
        // Still 2, not 4: reactivation must not stack subscriptions from the prior cycle.
        await Assert.That(deviceService.LiveDefaultDeviceSubscriptions).IsEqualTo(2);
    }
}
