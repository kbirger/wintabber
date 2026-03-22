using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using ReactiveUI;
using Windows.Media.Control;
using WinTabberUI.ViewModels;
using SMTCMediaProps = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionMediaProperties;
using SMTCPlaybackInfo = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackInfo;
using SMTCSessionTimelineProps = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionTimelineProperties;
using SMTCSSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;

namespace WinTabberUI.Repositories;

public static class SMTCMediaChangeExtensions
{
    extension(SMTCSSession session)
    {
        public IObservable<SMTCSessionTimelineProps> ObserveTimelineProperties()
        {
            return EventHelper
                .EventOrEmpty<SMTCSSession, TimelinePropertiesChangedEventArgs, SMTCSessionTimelineProps>(
                    session,
                    h => session.TimelinePropertiesChanged += h,
                    h => session.TimelinePropertiesChanged -= h,
                    events =>
                        events
                            .Select(_ => Unit.Default)
                            .StartWith(Unit.Default)
                            .Select(_ => session.GetTimelineProperties())
                )
                .Replay(1)
                .RefCount();
        }

        public IObservable<SMTCPlaybackInfo> ObservePlaybackProperties()
        {
            return EventHelper
                .EventOrEmpty<SMTCSSession, PlaybackInfoChangedEventArgs, SMTCPlaybackInfo>(
                    session,
                    h => session.PlaybackInfoChanged += h,
                    h => session.PlaybackInfoChanged -= h,
                    events =>
                        events.Select(_ => Unit.Default).StartWith(Unit.Default).Select(_ => session.GetPlaybackInfo())
                )
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Replay(1)
                .RefCount();
        }

        public IObservable<SMTCMediaProps> ObserveMediaProperties()
        {
            return EventHelper
                .EventOrEmpty<SMTCSSession, MediaPropertiesChangedEventArgs, SMTCMediaProps>(
                    session,
                    h => session.MediaPropertiesChanged += h,
                    h => session.MediaPropertiesChanged -= h,
                    events =>
                        events
                            //.Do(p => { Debug.WriteLine($"MEDIA PROPERTIES CHANGED"); })
                            .SelectMany(_ => session.TryGetMediaPropertiesAsync())
                            .Catch(Observable.Empty<SMTCMediaProps>())
                )
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Replay(1)
                .RefCount();
        }
    }
}
