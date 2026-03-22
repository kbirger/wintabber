//using NAudio.CoreAudioApi;
//using System.Reactive.Linq;

//namespace WinTabberUI.Repositories;

//public class CoreAudioDeviceVolumeRepository
//{  
//    public IObservable<AudioVolumeNotificationData> GetVolumeChanged(AudioEndpointVolume audioEndpointVolume)
//    {
//        return Observable.FromEvent<AudioEndpointVolumeNotificationDelegate, AudioVolumeNotificationData>(
//            h =>
//            {
//                if (audioEndpointVolume is not null)
//                    audioEndpointVolume.OnVolumeNotification += h;
//            },
//            h =>
//            {
//                if (audioEndpointVolume is not null)
//                    audioEndpointVolume.OnVolumeNotification -= h;
//            })
//            .Publish()
//            .RefCount();
//    }
//}
