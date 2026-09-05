using System.Linq;
using System.Reactive.Concurrency;
using NAudio.CoreAudioApi;
using WinTabber.Api.Media.CoreAudio.Repositories;
using WinTabber.Api.Media.Tests.Fakes;

namespace WinTabber.Api.Media.Tests.CoreAudio;

public class CoreAudioDeviceRepositoryTests
{
    [Test]
    public async Task GetDefaultPlaybackDevice_ReturnsNull_WhenNoDefaultRenderEndpoint()
    {
        var enumerator = new FakeMMDeviceEnumeratorWrapper();
        using var repository = new CoreAudioDeviceRepository(ImmediateScheduler.Instance, enumerator);

        var result = repository.GetDefaultPlaybackDevice();

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task GetDefaultRecordingDevice_ReturnsNull_WhenNoDefaultCaptureEndpoint()
    {
        var enumerator = new FakeMMDeviceEnumeratorWrapper();
        using var repository = new CoreAudioDeviceRepository(ImmediateScheduler.Instance, enumerator);

        var result = repository.GetDefaultRecordingDevice();

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Constructor_RegistersEndpointNotificationCallback()
    {
        var enumerator = new FakeMMDeviceEnumeratorWrapper();

        using var repository = new CoreAudioDeviceRepository(ImmediateScheduler.Instance, enumerator);

        await Assert.That(enumerator.RegisteredCallbacks.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Dispose_UnregistersEndpointNotificationCallback()
    {
        var enumerator = new FakeMMDeviceEnumeratorWrapper();
        var repository = new CoreAudioDeviceRepository(ImmediateScheduler.Instance, enumerator);

        repository.Dispose();

        await Assert.That(enumerator.UnregisteredCallbacks.Count).IsEqualTo(1);
    }

    [Test]
    public async Task GetDefaultPlaybackDevice_ReturnsDevice_WhenDefaultRenderEndpointExists()
    {
        var enumerator = new FakeMMDeviceEnumeratorWrapper();
        var device = new FakeAudioDevice { Id = "device-1", DataFlow = DataFlow.Render };
        enumerator.SetDefaultDevice(DataFlow.Render, Role.Multimedia, device);
        using var repository = new CoreAudioDeviceRepository(ImmediateScheduler.Instance, enumerator);

        var result = repository.GetDefaultPlaybackDevice();

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo("device-1");
    }

    [Test]
    public async Task GetDefaultRecordingDevice_ReturnsDevice_WhenDefaultCaptureEndpointExists()
    {
        var enumerator = new FakeMMDeviceEnumeratorWrapper();
        var device = new FakeAudioDevice { Id = "device-2", DataFlow = DataFlow.Capture };
        enumerator.SetDefaultDevice(DataFlow.Capture, Role.Multimedia, device);
        using var repository = new CoreAudioDeviceRepository(ImmediateScheduler.Instance, enumerator);

        var result = repository.GetDefaultRecordingDevice();

        await Assert.That(result).IsNotNull();
        await Assert.That(result!.Id).IsEqualTo("device-2");
    }

    [Test]
    public async Task Devices_PopulatesFromEnumerateAudioEndPoints()
    {
        var enumerator = new FakeMMDeviceEnumeratorWrapper();
        enumerator.AddDevice(new FakeAudioDevice { Id = "device-1", DataFlow = DataFlow.Render, State = DeviceState.Active });
        enumerator.AddDevice(new FakeAudioDevice { Id = "device-2", DataFlow = DataFlow.Capture, State = DeviceState.Active });
        using var repository = new CoreAudioDeviceRepository(ImmediateScheduler.Instance, enumerator);

        var devices = repository.Devices.Items.ToList();

        await Assert.That(devices.Count).IsEqualTo(2);
        await Assert.That(devices.Any(d => d.Id == "device-1")).IsTrue();
        await Assert.That(devices.Any(d => d.Id == "device-2")).IsTrue();
    }

    [Test]
    public async Task Devices_AddsDevice_WhenDeviceAddedWhileActive()
    {
        var enumerator = new FakeMMDeviceEnumeratorWrapper();
        using var repository = new CoreAudioDeviceRepository(ImmediateScheduler.Instance, enumerator);
        var initial = repository.Devices.Items.ToList();
        await Assert.That(initial.Count).IsEqualTo(0);

        var device = new FakeAudioDevice { Id = "device-1", DataFlow = DataFlow.Render, State = DeviceState.Active };
        enumerator.AddDevice(device);
        var callback = enumerator.RegisteredCallbacks.Single();
        callback.OnDeviceAdded(device.Id);

        var devices = repository.Devices.Items.ToList();
        await Assert.That(devices.Count).IsEqualTo(1);
        await Assert.That(devices[0].Id).IsEqualTo("device-1");
    }

    [Test]
    public async Task Devices_RemovesDevice_WhenStateChangesToUnplugged()
    {
        var enumerator = new FakeMMDeviceEnumeratorWrapper();
        var device = new FakeAudioDevice { Id = "device-1", DataFlow = DataFlow.Render, State = DeviceState.Active };
        enumerator.AddDevice(device);
        using var repository = new CoreAudioDeviceRepository(ImmediateScheduler.Instance, enumerator);
        var initial = repository.Devices.Items.ToList();
        await Assert.That(initial.Count).IsEqualTo(1);

        var callback = enumerator.RegisteredCallbacks.Single();
        callback.OnDeviceStateChanged(device.Id, DeviceState.Unplugged);

        var devices = repository.Devices.Items.ToList();
        await Assert.That(devices.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Watch_EmitsVolumeAndMuteChanges_FromDeviceVolumeChangedObservable()
    {
        var enumerator = new FakeMMDeviceEnumeratorWrapper();
        var device = new FakeAudioDevice
        {
            Id = "device-1",
            DataFlow = DataFlow.Render,
            MasterVolumeLevelScalar = 0.5f,
            Mute = false,
        };
        using var repository = new CoreAudioDeviceRepository(ImmediateScheduler.Instance, enumerator);

        var events = repository.Watch(device);
        var volumeChanges = new List<float>();
        var muteChanges = new List<bool>();
        using var volumeSub = events.VolumeChanges.Subscribe(volumeChanges.Add);
        using var muteSub = events.MuteChanges.Subscribe(muteChanges.Add);

        device.VolumeChangedSubject.OnNext((0.75f, true));

        await Assert.That(volumeChanges.Count).IsEqualTo(2);
        await Assert.That(volumeChanges[0]).IsEqualTo(0.5f);
        await Assert.That(volumeChanges[1]).IsEqualTo(0.75f);
        await Assert.That(muteChanges.Count).IsEqualTo(2);
        await Assert.That(muteChanges[0]).IsFalse();
        await Assert.That(muteChanges[1]).IsTrue();
    }
}
