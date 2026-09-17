using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using NAudio.CoreAudioApi;
using ReactiveUI;
using WinTabber.Api.Media.CoreAudio.Dtos;
using WinTabber.Api.Media.CoreAudio.Services;

namespace WinTabber.UI.Media.ViewModels
{
    public partial class AudioDeviceSelectorViewModel : ReactiveObject, IDisposable
    {
        public AudioDeviceSelectorViewModel(IAudioDeviceService deviceService, DataFlow flow)
        {
            _deviceService = deviceService;
            var devices = deviceService.Devices.Connect().Filter(device => device.DataFlow == flow);
            // ResetThreshold: int.MaxValue, not DynamicData's default (25): a real device add/remove
            // fires several simultaneous cache changes at once (CoreAudioDeviceRepository.GetDevices's
            // defaultSubscription alone calls the parameterless cache.Refresh(), refreshing every
            // device in one changeset), and once a changeset's change count crosses the default
            // threshold, Bind() collapses it into a single CollectionChanged Reset instead of granular
            // Add/Remove/Replace notifications. WinUI 3's Selector-derived controls (ComboBox here)
            // clear SelectedItem on a Reset notification -- confirmed live: the device dropdown loses
            // its selection specifically when the device list changes (add/remove), not on an ordinary
            // default-device switch, matching this exact threshold-crossing behavior.
            devices
                .ObserveOn(RxApp.MainThreadScheduler)
                .Bind(out _devices, new BindingOptions(ResetThreshold: int.MaxValue))
                .Subscribe()
                .DisposeWith(_cleanUp);

            // ObserveOn before Subscribe, not left off like the WPF original tolerated: raising
            // PropertyChanged off the UI thread is harmless in WPF (plain CLR EventArgs), but WinUI
            // 3's WinRT-projected PropertyChangedEventArgs must be created on the UI thread --
            // confirmed live via a real RPC_E_WRONG_THREAD COMException crash on startup without
            // this, from GetDefaultDevice's CoreAudio callback thread setting SelectedDevice.
            deviceService
                .GetDefaultDevice(flow)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(defaultDevice =>
                {
                    SelectedDevice = defaultDevice;
                })
                .DisposeWith(_cleanUp);

            _pendingEndpointChange.DisposeWith(_cleanUp);
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
                        // A SerialDisposable, not _cleanUp: this fires once per selection change,
                        // so adding each subscription to the composite would grow it without
                        // bound for the life of the view model. Assigning here also disposes the
                        // previous one, which is the behaviour we want anyway — a newer endpoint
                        // change supersedes one still in flight.
                        // todo: catch errors
                        _pendingEndpointChange.Disposable = _deviceService
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
        private readonly IAudioDeviceService _deviceService;
        private readonly CompositeDisposable _cleanUp = new();
        private readonly SerialDisposable _pendingEndpointChange = new();

        public void Dispose()
        {
            _cleanUp.Dispose();
        }
    }
}
