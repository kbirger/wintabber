using CommunityToolkit.Mvvm.ComponentModel;
using CoreAudio;

namespace WinTabberUI.ViewModels
{
    public partial class AudioDeviceSelectorViewModel : ObservableObject
    {
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
                    _device.Selected = value;
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

        private static readonly MMDeviceEnumerator _deviceEnum = new MMDeviceEnumerator(Guid.NewGuid());
        public AudioDeviceSelectorViewModel(DataFlow dataFlow)
        {
            Devices = _deviceEnum.EnumerateAudioEndPoints(dataFlow, DeviceState.Active)
                .Select(device => new DeviceItem(device))
                .ToArray();

            SelectedDevice = Devices.SingleOrDefault(device => device.IsSelected);
        }

        public DeviceItem[] Devices { get; }

        public DeviceItem? SelectedDevice
        {
            get => _selectedDevice;
            set
            {
                _selectedDevice = value;
                if (_selectedDevice is not null)
                {
                    _selectedDevice.IsSelected = true;
                    _deviceEnum.SetDefaultAudioEndpoint(_selectedDevice.Device);
                    //foreach (var device in Devices.Where(device => device != _selectedDevice))
                    //{
                    //    device.IsSelected = false;
                    //}
                }
                OnPropertyChanged();
            }
        }
        private DeviceItem? _selectedDevice;
    }
}
