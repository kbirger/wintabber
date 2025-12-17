using CoreAudio;
using DynamicData;
using Microsoft.WindowsAPICodePack.Shell.PropertySystem;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using WinTabberUI.ViewModels;

namespace WinTabberUI.Services;

public partial class AudioDeviceManager : IAudioDeviceManager
{
    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    public class MMDeviceEnumeratorX { }

    [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceCollection
    {
        uint GetCount();
        [return: MarshalAs(UnmanagedType.Interface)]
        IMMDevice Item(uint nDevice);
    }
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        void Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.Interface)] out object ppInterface);
        [return: MarshalAs(UnmanagedType.Interface)]
        IPropertyStore OpenPropertyStore(STGM stgmAccess);
        [return: MarshalAs(UnmanagedType.LPWStr)]
        string GetId();
        DeviceState GetState();
    }

    public enum STGM
    {
        STGM_READ = 0,
        STGM_WRITE = 1,
        STGM_READWRITE = 2,
        // ...
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        [PreserveSig]
        int GetCount([Out] out uint cProps);
        [PreserveSig]
        int GetAt([In] uint iProp, out PROPERTYKEY pkey);
        PropVariant GetValue([In] ref PROPERTYKEY key);
        [PreserveSig]
        int SetValue([In] ref PROPERTYKEY key, [In] ref PropVariant pv);
        [PreserveSig]
        int Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPERTYKEY
    {
        public Guid fmtid;
        public UIntPtr pid;

        public override bool Equals(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            var pkey = ((PROPERTYKEY)obj);
            return pkey.fmtid == fmtid && pkey.pid == pid;
        }

        public override int GetHashCode()
        {
            return fmtid.GetHashCode() + pid.GetHashCode();
        }
    }

    MMDeviceEnumerator _enumerator = new MMDeviceEnumerator(Guid.NewGuid());
    MMNotificationClient _notificationClient;
    private readonly SourceCache<DeviceItem, string> _devices;
    public AudioDeviceManager()
    {
        _notificationClient = new MMNotificationClient(_enumerator);
        _devices = new SourceCache<DeviceItem, string>(device => device.Id);
    }

    private MMDeviceCollection GetDevices()
    {
        return _enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active);
    }

    public IDisposable Init()
    {
        DevicesObservable.Take(1).Subscribe();
        return this;
    }
    public IObservable<IChangeSet<DeviceItem, string>> Connect()
    {
        return Observable.Create<IChangeSet<DeviceItem, string>>(observer =>
        {
            var connection = _devices.Connect();
            var disposables = new CompositeDisposable();


            DevicesObservable
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(devices =>
                {
                    _devices.Edit(updater =>
                    {
                        updater.Clear();
                        updater.AddOrUpdate(devices);
                    });

                }).DisposeWith(disposables);

            DeviceAdditions.Subscribe(added =>
            {
                _devices.AddOrUpdate(new DeviceItem(_enumerator.GetDevice(added.DeviceId), _enumerator));
            }).DisposeWith(disposables);

            DeviceRemovals.Subscribe(removed =>
            {
                _devices.Remove(removed.DeviceId);
            }).DisposeWith(disposables);

            connection.Subscribe(devices => observer.OnNext(devices))
                .DisposeWith(disposables);

            return () =>
            {
                disposables.Dispose();
            };
        }).Replay(1).RefCount();
    }

    [Lazy]
    private IObservable<DefaultDeviceChangedEventArgs> GetDefaultDeviceChanges()
    {
        return Observable.FromEventPattern<DefaultDeviceChangedEventArgs>(
            h => _notificationClient.DefaultDeviceChanged += h,
            h => _notificationClient.DefaultDeviceChanged -= h)
            .Select(x => x.EventArgs);
    }

    [Lazy]
    private IObservable<DeviceNotificationEventArgs> GetDeviceAdditions()
    {
        return Observable.FromEventPattern<DeviceNotificationEventArgs>(
            h => _notificationClient.DeviceAdded += h,
            h => _notificationClient.DeviceAdded -= h)
            .Select(x => x.EventArgs);
    }

    [Lazy]
    private IObservable<DeviceNotificationEventArgs> GetDeviceRemovals()
    {
        return Observable.FromEventPattern<DeviceNotificationEventArgs>(
            h => _notificationClient.DeviceRemoved += h,
            h => _notificationClient.DeviceRemoved -= h)
            .Select(x => x.EventArgs);
    }

    [Lazy]
    private IObservable<DefaultDeviceChangedEventArgs> GetDefaultPlaybackDeviceChanges()
    {
        return DefaultDeviceChanges
            .Where(e => e.DataFlow == DataFlow.Render);
    }

    [Lazy]
    private IObservable<DefaultDeviceChangedEventArgs> GetDefaultRecordingDeviceChanges()
    {
        return DefaultDeviceChanges
            .Where(e => e.DataFlow == DataFlow.Capture);
    }


    [Lazy]
    public IObservable<DeviceItem[]> GetDevicesObservable()
    {
        var obs = Observable.Create<DeviceItem[]>(observer =>
        {
            Task.Factory.StartNew(() =>
            {

                Debug.WriteLine("Fetching devices from thread");
                var devices = _enumerator.EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active)
                    .Select(device => new DeviceItem(device, _enumerator))
                    .ToArray();
                observer.OnNext(devices);

                observer.OnCompleted();
            }, CancellationToken.None, TaskCreationOptions.None, TaskScheduler.FromCurrentSynchronizationContext());

            return () => { };
        })
        .Replay(1);
        obs.Connect();
        return obs;
        
    }

    public void Dispose()
    {
        
    }
}
