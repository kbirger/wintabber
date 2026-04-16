using ReactiveUI;
using ReactiveUI.SourceGenerators;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Windows;
using WinTabber.Api.Media.CoreAudio.Dtos;
using WinTabber.Common.Util;
using static Microsoft.WindowsAPICodePack.Shell.PropertySystem.SystemProperties.System;

namespace WinTabber.UI.Media.ViewModels;

public class VolumeControlsViewModel : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _disposable = new CompositeDisposable();


    public VolumeControlsViewModel(
        IObservable<IObservableVolumeDto> volumeDto,
        string volumeHintText,
        string muteHintText

    ) 
    {
        VolumeHintText = volumeHintText;
        MuteHintText = muteHintText;
        _volumeDtos = volumeDto;
        var scheduler = RxSchedulers.MainThreadScheduler;

        var canSetVolume = CanSetVolumeImpl();
        var canSetMuted = CanSetMutedImpl();
        SetMuted = ReactiveCommand.CreateFromObservable<bool, Unit>(
            execute: SetMutedImpl,
            canExecute: canSetMuted,
            outputScheduler: scheduler
        );

        SetVolume = ReactiveCommand.CreateFromObservable<float, Unit>(
            execute: SetVolumeImpl,
            canExecute: canSetVolume,
            outputScheduler: scheduler
        );
        _canMute = canSetMuted.ToProperty(this, vm => vm.CanMute, scheduler: scheduler);
        _canSetVolume = canSetVolume.ToProperty(this, vm => vm.CanSetVolume, scheduler: scheduler);
        _isMuted = volumeDto
            .Select(dto => dto?.IsMutedChanges)
            .OrDefault(false)
            .Switch()
            .Do(x => { Debug.WriteLine($"Muted? {x}");  })
            .ToProperty(this, vm => vm.IsMuted, scheduler: scheduler);

        //this.WhenAnyValue(vm => vm.Volume, true)
        //    .ObserveOn(scheduler)
        //    .Sample(TimeSpan.FromMicroseconds(100))
        //    .InvokeCommand(this, vm => vm.SetVolume)
        //    .DisposeWith(_disposable);

        volumeDto.Select(dto => dto?.VolumeChanges ?? Observable.Empty<float>())
            .Switch()
            .Subscribe(volume => UpdateVolume(volume))
            .DisposeWith(_disposable);
    }

    private static IObservable<Unit> NoopImpl()
    {
        return Observable.Empty<Unit>();
    }


    private IObservable<bool> CanSetMutedImpl()
    {
        return _volumeDtos.Select(dto => dto?.CanMuteChanges).OrDefault(false).Switch();
    }

    private IObservable<bool> CanSetVolumeImpl()
    {
        return _volumeDtos.Select(dto => dto?.CanSetVolumeChanges).OrDefault(false).Switch();
    }

    private IObservable<Unit> SetMutedImpl(bool value)
    {
        return _volumeDtos.Select(dto => dto?.SetMute(value) ?? NoopImpl()).Switch().Take(1);
    }

    private IObservable<Unit> SetVolumeImpl(float value)
    {
        return _volumeDtos.Select(dto => dto?.SetVolume(value) ?? NoopImpl()).Switch().Take(1);
    }
    //public VolumeControlsViewModel(
    //    ReactiveCommand<float, Unit> setVolumeCommand,
    //    ReactiveCommand<bool, Unit> setMutedCommand,
    //    IObservable<float> volumeChanges,
    //    IObservable<bool> muteChanges,
    //    string volumeHintText,
    //    string muteHintText
    //)
    //{
    //    var scheduler = RxSchedulers.MainThreadScheduler;
    //    _canSetVolume = setVolumeCommand.CanExecute.ToProperty(this, vm => vm.CanSetVolume, scheduler: scheduler);
    //    _canMute = setMutedCommand.CanExecute.ToProperty(this, vm => vm.CanMute, scheduler: scheduler);
    //    _isMuted = muteChanges.ToProperty(this, vm => vm.IsMuted);

    //    //Mute = ReactiveCommand
    //    //    .CreateFromObservable(muteImpl, canExecute: null, outputScheduler: scheduler)
    //    //    .DisposeWith(_disposable);

    //    //SetVolume = ReactiveCommand
    //    //    .CreateFromObservable(
    //    //        setVolumeImpl,
    //    //        canExecute: canSetVolumeChanges,
    //    //        outputScheduler: scheduler
    //    //    )
    //    //.DisposeWith(_disposable);

    //    //this.WhenAnyValue(vm => vm.Volume, true)
    //    //    .ObserveOn(scheduler)
    //    //    .Sample(TimeSpan.FromMicroseconds(100))
    //    //    .InvokeCommand(this, vm => vm.SetVolume)
    //    //    .DisposeWith(_disposable);

    //    SetMuted = setMutedCommand;
    //    SetVolume = setVolumeCommand;
    //    VolumeHintText = volumeHintText;
    //    MuteHintText = muteHintText;

    //    volumeChanges
    //        .Subscribe(volume => UpdateVolume(volume))
    //        .DisposeWith(_disposable);
    //}

    //public float Volume => _volume.Value;

    //public static readonly DependencyProperty VolumeProperty = DependencyProperty.Register(
    //    nameof(Volume),
    //    typeof(float),
    //    typeof(VolumeControlsViewModel),
    //    new PropertyMetadata(0f)
    //);

    //public float Volume     {
    //    get => _volume;
    //    set => this.RaiseAndSetIfChanged(ref _volume, value);
    //}

    private bool UpdateVolume(float value)
    {
        if (_volume != value)
        {
            _volume = value;
            this.RaisePropertyChanged(nameof(Volume));
            return true;
        }

        return false;
    }
    public float Volume
    {
        get => _volume;
        set
        {
            if (CanSetVolume && UpdateVolume(value))
            {
                SetVolume.Execute(value).Subscribe();
            }
        }
    }

    private ObservableAsPropertyHelper<bool> _canSetVolume;
    private ObservableAsPropertyHelper<bool> _canMute;
    private float _volume = 0;

    //private readonly ObservableAsPropertyHelper<float> _volume;

    public ReactiveCommand<float, Unit> SetVolume { get; }
    public ReactiveCommand<bool, Unit> SetMuted { get; }

    private readonly ObservableAsPropertyHelper<bool> _isMuted;
    private readonly IObservable<IObservableVolumeDto> _volumeDtos;

    public string VolumeHintText { get; }
    public string MuteHintText { get; }
    public bool IsMuted => _isMuted.Value;

    public bool CanSetVolume => _canSetVolume.Value;
    public bool CanMute => _canMute.Value;

    public void Dispose()
    {
        _disposable?.Dispose();
    }
}
