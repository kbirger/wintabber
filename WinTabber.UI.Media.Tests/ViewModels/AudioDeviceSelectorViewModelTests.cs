using System.Reactive.Concurrency;
using NAudio.CoreAudioApi;
using ReactiveUI;
using WinTabber.Api.Media.CoreAudio.Dtos;
using WinTabber.UI.Media.Tests.Fakes;
using WinTabber.UI.Media.ViewModels;

namespace WinTabber.UI.Media.Tests.ViewModels;

/// <summary>
/// These exist because T6.1 made <c>IAudioDeviceService</c> fakeable. Before the interface there
/// was no way to construct this view model in a test at all.
/// </summary>
/// <remarks>
/// <c>NotInParallel</c>: <see cref="RxApp.MainThreadScheduler"/> is global, and the view model
/// observes on it.
/// </remarks>
[NotInParallel]
public class AudioDeviceSelectorViewModelTests
{
    private static DeviceDto Device(string id) =>
        new()
        {
            DeviceId = id,
            DeviceName = id,
            DeviceFriendlyName = id,
            DataFlow = DataFlow.Render,
        };

    private static AudioDeviceSelectorViewModel Create(FakeAudioDeviceService service)
    {
        // No dispatcher here, so the default main-thread scheduler would never pump.
        RxApp.MainThreadScheduler = CurrentThreadScheduler.Instance;
        return new AudioDeviceSelectorViewModel(service, DataFlow.Render);
    }

    [Test]
    public async Task Dispose_ReleasesTheConstructorSubscriptions()
    {
        var service = new FakeAudioDeviceService(Device("a"));
        var vm = Create(service);

        await Assert.That(service.LiveDefaultDeviceSubscriptions).IsEqualTo(1);

        vm.Dispose();

        await Assert.That(service.LiveDefaultDeviceSubscriptions).IsEqualTo(0);
    }

    /// <summary>
    /// The leak this fix targets. The endpoint subscription is made in a property setter, so it
    /// fires once per selection change; held in a CompositeDisposable it would accumulate one
    /// live subscription per change for the life of the view model.
    /// </summary>
    [Test]
    public async Task ChangingSelection_DoesNotAccumulateEndpointSubscriptions()
    {
        var service = new FakeAudioDeviceService(Device("a"), Device("b"), Device("c"));
        var vm = Create(service);

        vm.SelectedDevice = Device("a");
        await Assert.That(service.LiveEndpointSubscriptions).IsEqualTo(1);

        vm.SelectedDevice = Device("b");
        vm.SelectedDevice = Device("c");

        // Still one: each assignment disposes the previous subscription rather than stacking.
        await Assert.That(service.LiveEndpointSubscriptions).IsEqualTo(1);

        vm.Dispose();

        await Assert.That(service.LiveEndpointSubscriptions).IsEqualTo(0);
    }

    [Test]
    public async Task Dispose_ReleasesAPendingEndpointChange()
    {
        var service = new FakeAudioDeviceService(Device("a"));
        var vm = Create(service);
        vm.SelectedDevice = Device("a");

        await Assert.That(service.LiveEndpointSubscriptions).IsEqualTo(1);

        vm.Dispose();

        await Assert.That(service.LiveEndpointSubscriptions).IsEqualTo(0);
    }
}
