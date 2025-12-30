// See https://aka.ms/new-console-template for more information
//using CoreAudio;
using NAudio;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wasapi;
using System.Diagnostics;
using WinTabberUI.ViewModels;

Debug.WriteLine("Hello, World!");

MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
List<Monitor> monitors = new List<Monitor>();
//var monitor = new Monitor();
List<AudioSessionControl> _sessions = new();
foreach (var endpoint in endpoints)
{
    endpoint.AudioSessionManager.OnSessionCreated += AudioSessionManager_OnSessionCreated;
    
    for(int i = 0; i < endpoint.AudioSessionManager.Sessions.Count; i++)
    {
        var session = endpoint.AudioSessionManager.Sessions[i];
        if(session.IsSystemSoundsSession)
        {
            continue;
        }
        _sessions.Add(session);
        var monitor = new Monitor(session, _sessions);
        session.RegisterEventClient(monitor);
        var name = session.DisplayName;
        var pid = session.GetProcessID;
        var proc = Process.GetProcessById(Convert.ToInt32(pid));
        Debug.WriteLine($"existing session '{name}': {proc.MainModule?.FileName}"); 

        if(i == endpoint.AudioSessionManager.Sessions.Count - 1)
        {
            monitor.Print();
        }
    }
}

void AudioSessionManager_OnSessionCreated(object sender, NAudio.CoreAudioApi.Interfaces.IAudioSessionControl newSession)
{
    //newSession.GetDisplayName(out var name);
    var session = new AudioSessionControl(newSession);
    _sessions.Add(session);
    var name = session.DisplayName;
    var pid = session.GetProcessID;
    var proc = Process.GetProcessById(Convert.ToInt32(pid));
    Debug.WriteLine($"new session '{name}': {proc.MainModule?.FileName}");
    //newSession.RegisterAudioSessionNotification(new Monitor());
    var monitor = new Monitor(session, _sessions);
    newSession.RegisterAudioSessionNotification(new AudioSessionEventsCallback(monitor));
    monitor.Print();
}

//var watchers = endpoints.Select(x => new DeviceSessionWatcher());
while(true)
{
    await Task.Delay(100);

}

public record Session(string name, string process)
{

}

public class Monitor : IAudioSessionEventsHandler
{
    private AudioSessionControl _session;
    private readonly List<AudioSessionControl> _sessions;

    public Monitor(AudioSessionControl session, List<AudioSessionControl> sessions)
    {
        _session = session;
        _sessions = sessions;
    }

    public void OnVolumeChanged(float volume, bool isMuted)
    {
        //throw new NotImplementedException();
    }

    public void OnDisplayNameChanged(string displayName)
    {
        Debug.WriteLine($"New display name {displayName}");
    }

    public void OnIconPathChanged(string iconPath)
    {
        //throw new NotImplementedException();
    }

    public void OnChannelVolumeChanged(uint channelCount, nint newVolumes, uint channelIndex)
    {
        //throw new NotImplementedException();
    }

    public void OnGroupingParamChanged(ref Guid groupingId)
    {
        Debug.WriteLine($"New grouping {groupingId}");
    }

    public void OnStateChanged(AudioSessionState state)
    {
        Debug.WriteLine($"New session state {state}");
        if(state == AudioSessionState.AudioSessionStateInactive || state == AudioSessionState.AudioSessionStateExpired)
        {
            _sessions.Remove(_session);
        }
        Print();
    }

    public void OnSessionDisconnected(AudioSessionDisconnectReason disconnectReason)
    {
        Debug.WriteLine($"session2 disconnected");
    }

    public void Print()
    {
        Debug.WriteLine("Sessions:");
        foreach(var session in _sessions)
        {
            PrintSession(session);
        }
        Debug.WriteLine("");
    }

    private void PrintSession(AudioSessionControl session)
    {
        Debug.WriteLine($"- {session.State} Session {session.DisplayName}: {GetFilePath(session.GetProcessID)}");
    }

    public string GetFilePath(uint pid)
    {
        return Process.GetProcessById(Convert.ToInt32(pid)).MainModule.FileName;
    }
}

//foreach(var watcher in watchers)
//{
//    watcher.Connect().Subscribe(changeSet =>
//    {
//        foreach(var change in changeSet)
//        {
//            switch(change.Reason)
//            {
//                case DynamicData.ChangeReason.Add:
//                    Debug.WriteLine($"Session Added: {change.Key}");
//                    break;
//                case DynamicData.ChangeReason.Remove:
//                    Debug.WriteLine($"Session Removed: {change.Key}");
//                    break;
//                case DynamicData.ChangeReason.Update:
//                    Debug.WriteLine($"Session Updated: {change.Key}");
//                    break;
//            }
//        }
//    });
//}
//DeviceSessionWatcher watcher = );

