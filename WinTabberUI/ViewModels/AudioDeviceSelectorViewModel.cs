using CommunityToolkit.Mvvm.ComponentModel;
using CoreAudio;
using ReactiveUI;
using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Linq;

namespace WinTabberUI.ViewModels
{
    public partial class AudioDeviceSelectorViewModel : ReactiveObject
    {
        public static (AudioDeviceSelectorViewModel Playback, AudioDeviceSelectorViewModel Recording) Create()
        {
            var deviceEnum = new MMDeviceEnumerator(Guid.NewGuid());

            var groupsObservable = GetDevicesObservable(deviceEnum)
                //.SubscribeOn(Scheduler.Default)
                .Select(devices => devices.ToLookup(device => device.DataFlow))
                .Replay(1)
                .RefCount();

            groupsObservable.Subscribe(_ => { Debug.WriteLine("deviceees"); });

            return (
                new AudioDeviceSelectorViewModel(
                    groupsObservable.Select(groups => groups[DataFlow.Render]),
                    DataFlow.Render,
                    deviceEnum.SetDefaultAudioEndpoint
                ),
                new AudioDeviceSelectorViewModel(
                    groupsObservable.Select(groups => groups[DataFlow.Capture]),
                    DataFlow.Capture,
                    deviceEnum.SetDefaultAudioEndpoint
                )
            );

        }

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

        public partial class DeviceItem : ObservableObject, IComparable<DeviceItem>
        {
            public DeviceItem(MMDevice device)
            {
                Name = device.DeviceFriendlyName;
                Id = device.ID;
                _isSelected = device.Selected;
                _device = device;
            }
            public string Name { get; }

            public string Id { get; }

            private readonly MMDevice _device;

            public MMDevice Device => _device;

            public bool IsSelected
            {
                get => _device.Selected;
                set
                {
                    _isSelected = value;
                    //_device.Selected = value;
                    
                    OnPropertyChanged();
                }
            }
            private bool _isSelected;

            public int CompareTo(DeviceItem? other)
            {
                return string.Compare(Name, other?.Name, StringComparison.Ordinal);
            }

            //public bool IsSelected
            //{
            //    get => _isSelected;
            //    set
            //    {
            //        _device.Selected = value;
            //        this.RaiseAndSetIfChanged(ref _isSelected, value);
            //    }
            //}
        }

        public AudioDeviceSelectorViewModel(IObservable<IEnumerable<MMDevice>> devicesObservable, DataFlow dataFlow, Action<MMDevice> activateFunction)
        {
            _dataFlow = dataFlow;
            _activateFunction = activateFunction;
            var deviceItems = devicesObservable
                .Select(devices => devices.Select(device => new DeviceItem(device)).ToArray());
            _devices = deviceItems
                .ToProperty(this, vm => vm.Devices, initialValue: []);

            deviceItems.Take(1).Subscribe(devices =>
            {
                SelectedDevice = devices.SingleOrDefault(device => device.IsSelected);
            });
        }

        private readonly ObservableAsPropertyHelper<DeviceItem[]> _devices;

        private readonly DataFlow _dataFlow;
        private readonly Action<MMDevice> _activateFunction;

        public DeviceItem[] Devices => _devices.Value;

        public DeviceItem? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                _selectedDevice = value;
                if (_selectedDevice is not null)
                {
                    _selectedDevice.IsSelected = true;
                    _activateFunction(_selectedDevice.Device);
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
