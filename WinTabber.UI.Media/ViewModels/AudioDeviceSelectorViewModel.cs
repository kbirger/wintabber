using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive.Linq;
using DynamicData;
using NAudio.CoreAudioApi;
using ReactiveUI;
using WinTabber.Api.Media.CoreAudio.Dtos;
using WinTabber.Api.Media.CoreAudio.Services;

namespace WinTabber.UI.Media.ViewModels
{
    public partial class AudioDeviceSelectorViewModel : ReactiveObject
    {
        //public static (AudioDeviceSelectorViewModel Playback, AudioDeviceSelectorViewModel Recording) Create()
        //{
        //    var deviceEnum = new MMDeviceEnumerator(Guid.NewGuid());
        //    var notif = new MMNotificationClient(deviceEnum);

        //    var groupsObservable = GetDevicesObservable(deviceEnum)
        //        //.SubscribeOn(Scheduler.Default)
        //        .Select(devices => devices.ToLookup(device => device.DataFlow))
        //        .Replay(1)
        //        .RefCount();

        //    groupsObservable.Subscribe(_ => { Debug.WriteLine("deviceees"); });

        //    return (
        //        new AudioDeviceSelectorViewModel(
        //            groupsObservable.Select(groups => groups[DataFlow.Render])
        //        ),
        //        new AudioDeviceSelectorViewModel(
        //            groupsObservable.Select(groups => groups[DataFlow.Capture]),
        //            DataFlow.Capture,
        //            deviceEnum.SetDefaultAudioEndpoint
        //        )
        //    );

        //}

        private static IObservable<MMDeviceCollection> GetDevicesObservable(MMDeviceEnumerator deviceEnum)
        {
            return Observable.Create<MMDeviceCollection>(observer =>
            {
                var devices = deviceEnum.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active);

                observer.OnNext(devices);
                observer.OnCompleted();

                return () => { };
            });
        }

        public AudioDeviceSelectorViewModel(AudioDeviceService deviceService, DataFlow flow)
        {
            _deviceService = deviceService;
            var devices = deviceService.Devices.Connect().Filter(device => device.DataFlow == flow);
            devices.ObserveOn(RxApp.MainThreadScheduler).Bind(out _devices).Subscribe();

            deviceService
                .GetDefaultDevice(flow)
                .Subscribe(defaultDevice =>
                {
                    SelectedDevice = defaultDevice;
                });
            //_dataFlow = dataFlow;
            //_activateFunction = activateFunction;
            //var deviceItems = devicesObservable
            //.Select(devices => devices.Select(device => new DeviceDto(device)).ToArray());
            //_devices = deviceItems
            //.ToProperty(this, vm => vm.Devices, initialValue: []);

            //deviceItems.Take(1).Subscribe(devices =>
            //{
            //    SelectedDevice = devices.SingleOrDefault(device => device.IsSelected);
            //});
        }

        //private readonly ObservableAsPropertyHelper<DeviceDto[]> _devices;
        private readonly ReadOnlyObservableCollection<DeviceDto> _devices;

        //private readonly DataFlow _dataFlow;
        //private readonly Action<MMDevice> _activateFunction;

        //public DeviceDto[] Devices => _devices.Value;
        public ReadOnlyObservableCollection<DeviceDto> Devices => _devices;

        public DeviceDto? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                if (_selectedDevice != value)
                {
                    _selectedDevice = value;
                    if (_selectedDevice is not null)
                    {
                        // todo: catch errors
                        _deviceService
                            .SetDefaultAudioEndpoint(_selectedDevice.DeviceId)
                            .Subscribe(
                                (_) => { },
                                onError: (ex) =>
                                {
                                    Debug.WriteLine($"error setting default device {ex.Message}");
                                }
                            );
                    }
                    this.RaisePropertyChanged();
                }
            }
        }
        private DeviceDto? _selectedDevice;
        private readonly AudioDeviceService _deviceService;
    }
}
