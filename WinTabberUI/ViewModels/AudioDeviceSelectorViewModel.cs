using CommunityToolkit.Mvvm.ComponentModel;
using CoreAudio;
using DynamicData;
using ReactiveUI;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Linq;

namespace WinTabberUI.ViewModels
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



        //public AudioDeviceSelectorViewModel(IObservable<IEnumerable<MMDevice>> devicesObservable, DataFlow dataFlow, Action<MMDevice> activateFunction)
        public AudioDeviceSelectorViewModel(IObservable<IChangeSet<DeviceItem, string>> devices)
        {
            devices.Bind(out _devices)
                .Subscribe();


            devices.QueryWhenChanged(query => query.Items.FirstOrDefault(item => item.IsSelected))
                .Subscribe(selectedItem =>
                {
                    SelectedDevice = selectedItem;
                });
            devices
                .MergeMany(device => device
                    .ObservableForProperty(d => d.IsSelected))
                    .AsObservable()
                    .Subscribe(change =>
                    {
                        if (change.Value)
                        {
                            SelectedDevice = change.Sender;
                        }
                    });
            //_dataFlow = dataFlow;
            //_activateFunction = activateFunction;
            //var deviceItems = devicesObservable
            //.Select(devices => devices.Select(device => new DeviceItem(device)).ToArray());
            //_devices = deviceItems
            //.ToProperty(this, vm => vm.Devices, initialValue: []);

            //deviceItems.Take(1).Subscribe(devices =>
            //{
            //    SelectedDevice = devices.SingleOrDefault(device => device.IsSelected);
            //});

        }

        //private readonly ObservableAsPropertyHelper<DeviceItem[]> _devices;
        private readonly ReadOnlyObservableCollection<DeviceItem> _devices;

        //private readonly DataFlow _dataFlow;
        //private readonly Action<MMDevice> _activateFunction;

        //public DeviceItem[] Devices => _devices.Value;
        public ReadOnlyObservableCollection<DeviceItem> Devices => _devices;

        public DeviceItem? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                _selectedDevice = value;
                if (_selectedDevice is not null)
                {
                    //_selectedDevice.IsSelected = true;
                    _selectedDevice.Activate();
                    //foreach (var device in Devices.Where(device => device != _selectedDevice))
                    //{
                    //    device.IsSelected = false;
                    //}
                }
                this.RaisePropertyChanged();
            }
        }
        private DeviceItem? _selectedDevice;
    }
}
