using ReactiveUI;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Windows;
using static Microsoft.WindowsAPICodePack.Shell.PropertySystem.SystemProperties.System;

namespace WinTabber.UI.Media.ViewModels;

public class VolumeControlsViewModel : ReactiveObject, IDisposable
{
    private readonly CompositeDisposable _disposable = new CompositeDisposable();

    public VolumeControlsViewModel(
        IObservable<bool> canSetVolumeChanges,
        IObservable<bool> canMuteChanges,
        IObservable<float> volumeChanges,
        IObservable<bool> muteChanges,
        Func<bool, IObservable<Unit>> muteImpl,
        Func<float, IObservable<Unit>> setVolumeImpl,
        string volumeHintText,
        string muteHintText
    )
    {
        var scheduler = RxSchedulers.MainThreadScheduler;
        _canSetVolume = canSetVolumeChanges.ToProperty(this, vm => vm.CanSetVolume);
        _isMuted = muteChanges.ToProperty(this, vm => vm.IsMuted);

        //Mute = ReactiveCommand
        //    .CreateFromObservable(muteImpl, canExecute: null, outputScheduler: scheduler)
        //    .DisposeWith(_disposable);

        _setVolume = setVolumeImpl;
        //SetVolume = ReactiveCommand
        //    .CreateFromObservable(
        //        setVolumeImpl,
        //        canExecute: canSetVolumeChanges,
        //        outputScheduler: scheduler
        //    )
        //.DisposeWith(_disposable);

        //this.WhenAnyValue(vm => vm.Volume, true)
        //    .ObserveOn(scheduler)
        //    .Sample(TimeSpan.FromMicroseconds(100))
        //    .InvokeCommand(this, vm => vm.SetVolume)
        //    .DisposeWith(_disposable);

        SetMuted = ReactiveCommand
            .CreateFromObservable((bool x) => muteImpl(x), canExecute: canMuteChanges, outputScheduler: scheduler)
            .DisposeWith(_disposable);


        VolumeHintText = volumeHintText;
        MuteHintText = muteHintText;

        volumeChanges
            .Subscribe(volume => UpdateVolume(volume))
            .DisposeWith(_disposable);
    }

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
                _setVolume(value);
            }
        }
    }

    private ObservableAsPropertyHelper<bool> _canSetVolume;
    private readonly Func<float, IObservable<Unit>> _setVolume;
    private float _volume = 0;

    //private readonly ObservableAsPropertyHelper<float> _volume;

    public ReactiveCommand<float, Unit> SetVolume { get; }
    public ReactiveCommand<bool, Unit> SetMuted { get; }

    private readonly ObservableAsPropertyHelper<bool> _isMuted;

    public string VolumeHintText { get; }
    public string MuteHintText { get; }
    public bool IsMuted => _isMuted.Value;

    public bool CanSetVolume => _canSetVolume.Value;

    public void Dispose()
    {
        _disposable?.Dispose();
    }
}
