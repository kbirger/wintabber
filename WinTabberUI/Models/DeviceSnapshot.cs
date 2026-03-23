//using System;
//using System.Collections.Generic;
//using System.Text;
//using NAudio.CoreAudioApi;

//namespace WinTabberUI.Models;

//public class DeviceSnapshot
//{
//#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
//    public DeviceSnapshot(CoreAudioDeviceWrapper device)
//#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
//    {
//        Id = device.ID;
//        DataFlow = device.DataFlow;
//        Update(device);
//    }

//    public void Update(CoreAudioDeviceWrapper device)
//    {
//        Name = device.FriendlyName ?? device.DeviceFriendlyName ?? device.ID;
//        State = device.State;
//        Volume = device.Volume;
//        IsMuted = device.IsMuted;
//        IsDefault = device.IsDefault;
//    }

//    public string Id { get; init; }
//    public string Name { get; private set; }
//    public DataFlow DataFlow { get; init; }
//    public DeviceState State { get; private set; }
//    public bool IsDefault { get; private set; }

//    public float Volume { get; private set; }
//    public bool IsMuted { get; private set; }
//}
