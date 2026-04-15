using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Text;
using System.Windows;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using Windows.Foundation;
using Windows.Media.Control;
using WinTabber.Api.Media.SMTC.Services;
using WinTabber.Common.Util;
using static System.Net.Mime.MediaTypeNames;

namespace WinTabber.UI.Media.ViewModels;

public partial class PlaybackControlsViewModel : ReactiveObject, IDisposable
{
    [Reactive]
    public partial SMTCSessionMonitor? Session { get; set; }
    private readonly ObservableAsPropertyHelper<TimeSpan> _duration;
    private readonly ObservableAsPropertyHelper<TimeSpan> _position;
    private readonly ObservableAsPropertyHelper<float> _progress;
    private readonly ObservableAsPropertyHelper<bool> _isPlaying;
    private readonly ObservableAsPropertyHelper<bool> _canSeek;
    private readonly CompositeDisposable _disposable;

    public PlaybackControlsViewModel(IScheduler scheduler)
    {
        SessionChanged = this.WhenAnyValue(vm => vm.Session);

        _isPlaying = SessionChanged
            .Select(session => session?.IsPlayingChanges)
            .OrDefault(false)
            .Switch()
            .ToProperty(this, vm => vm.IsPlaying);

        _canSeek = SessionChanged
            .Select(session => session?.CanSeekChanges)
            .OrDefault(false)
            .Switch()
            .ToProperty(this, vm => vm.CanSeek);

        _duration = SessionChanged
            .Select(session => session?.DurationChanges)
            .OrDefault(TimeSpan.Zero)
            .Switch()
            .ToProperty(this, vm => vm.Duration);

        var isSeekingChanges = this.WhenAnyValue(vm => vm.IsSeeking, true)
            .SelectMany(value =>
                value ? Observable.Return(value) : Observable.Timer(TimeSpan.FromMilliseconds(250)).Select(_ => value)
            );

        var positionObservable = SessionChanged
            .Select(session => session?.PositionChanges)
            .OrDefault(TimeSpan.Zero)
            .Switch()
            //.Do(_ => Debug.WriteLine("timeline"))
            .CombineLatest(isSeekingChanges)
            .Where(values => !values.Second)
            //.Do(_ => Debug.WriteLine("playback"))
            .Select((values) => values.First)
            //.Do(x => Debug.WriteLine(x))
            .Publish()
            .RefCount();
        _position = positionObservable
        //.Do(t => Debug.WriteLine($"Position {t}"))
        .ToProperty(this, vm => vm.Position, initialValue: TimeSpan.Zero);

        var durationObservable = SessionChanged
            .Select(session => session?.DurationChanges)
            .OrDefault(TimeSpan.Zero)
            .Switch();
        _progress = positionObservable
            .WithLatestFrom(durationObservable)
            .Select(values => values.Second.Ticks > 0 ? (float)(values.First.Ticks / values.Second.Ticks) : 0)
            .ToProperty(this, vm => vm.Progress, initialValue: 0);

        PlayPause = ReactiveCommand.CreateFromObservable(
            PlayPauseImpl,
            canExecute: SessionChanged
                .Log(x => $"Session changed {x?.Session?.SourceAppUserModelId}")
                .Select(session => session?.CanPlayPauseChanges)
                .Log(x => $"PlayPauseChanges null? {x is null}")
                .OrDefault(false)
                .Switch()
                .Log(x => $"Can play pause? {x}"),
            outputScheduler: scheduler
        );
        Next = ReactiveCommand.CreateFromObservable(
            NextImpl,
            canExecute: SessionChanged.Select(session => session?.CanNextChanges).OrDefault(false).Switch(),
            outputScheduler: scheduler
        );
        Prev = ReactiveCommand.CreateFromObservable(
            PrevImpl,
            canExecute: SessionChanged.Select(session => session?.CanPrevChanges).OrDefault(false).Switch(),
            outputScheduler: scheduler
        );

        //Mute = ReactiveCommand
        //    .CreateFromObservable(MuteImpl, canExecute: null, outputScheduler: scheduler);

        Pause = ReactiveCommand.CreateFromObservable(
            PauseImpl,
            canExecute: SessionChanged.Select(session => session?.CanPauseChanges).OrDefault(false).Switch(),
            outputScheduler: scheduler
        );

        Seek = ReactiveCommand.CreateFromObservable<TimeSpan, Unit>(
            SeekImpl,
            canExecute: SessionChanged.Select(session => session?.CanSeekChanges).OrDefault(false).Switch(),
            outputScheduler: scheduler
        );

        _disposable = new CompositeDisposable(
            PlayPause,
            Next,
            Prev,
            Pause,
            Seek,
            _isPlaying,
            _canSeek,
            _duration,
            _position,
            _progress
        );
    }

    //public IObservable<Exception> ThrownExceptions { get; }

    public ReactiveCommand<Unit, Unit> PlayPause { get; }
    public ReactiveCommand<Unit, Unit> Next { get; }
    public ReactiveCommand<Unit, Unit> Prev { get; }

    //public ReactiveCommand<Unit, Unit> Mute { get; }
    public ReactiveCommand<Unit, Unit> Pause { get; }
    public ReactiveCommand<TimeSpan, Unit> Seek { get; }

    public TimeSpan Duration => _duration.Value;

    public TimeSpan Position => _position.Value;

    public float Progress => _progress.Value;

    public bool IsPlaying => _isPlaying.Value;

    public bool CanSeek => _canSeek.Value;

    private bool _isSeeking = false;
    public bool IsSeeking
    {
        get => _isSeeking;
        set => this.RaiseAndSetIfChanged(ref _isSeeking, value);
    }

    private IObservable<SMTCSessionMonitor?> SessionChanged { get; }

    private IObservable<Unit> PlayPauseImpl()
    {
        return SessionChanged
            .Select(session => OperationToObservable(session?.Session.TryTogglePlayPauseAsync()))
            .Switch()
            .Take(1);
    }

    private IObservable<Unit> PrevImpl()
    {
        return SessionChanged
            .Select(session => OperationToObservable(session?.Session.TrySkipPreviousAsync()))
            .Switch();
    }

    private IObservable<Unit> NextImpl()
    {
        return SessionChanged.Select(session => OperationToObservable(session?.Session.TrySkipNextAsync())).Switch();
    }

    private IObservable<Unit> PauseImpl()
    {
        return SessionChanged.Select(session => OperationToObservable(session?.Session.TryPauseAsync())).Switch();
    }

    private static IObservable<Unit> OperationToObservable(IAsyncOperation<bool>? operation)
    {
        if (operation is null)
        {
            return Observable.Empty<Unit>();
        }
        return Observable.FromAsync(() => operation.AsTask()).Select(_ => Unit.Default);
    }

    private IObservable<Unit> SeekImpl(TimeSpan position)
    {
        if (position > TimeSpan.Zero && position < Duration)
        {
            return Observable.StartAsync(async () =>
            {
                if (Session is null)
                {
                    return;
                }
                var result = await Session.Session.TryChangePlaybackPositionAsync(position.Ticks).AsTask();
                Debug.WriteLine($"Seek success: {result}");
            });
            //return SessionChanged.Select(session => OperationToObservable(session?.Session.TryChangePlaybackPositionAsync)(position.Tic()k);
        }

        return Observable.Return(Unit.Default);
    }

    public void Dispose()
    {
        _disposable.Dispose();
    }
}
