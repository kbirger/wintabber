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
}
