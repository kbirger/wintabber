using DynamicData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using WinTabberUI.ViewModels;

namespace WinTabberUI.Services;

public interface IAudioDeviceManager : IDisposable
{
    public IObservable<IChangeSet<DeviceItem, string>> Connect();
    IDisposable Init();
}
