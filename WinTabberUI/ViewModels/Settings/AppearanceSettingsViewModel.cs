using iNKORE.UI.WPF.Modern.Common.IconKeys;
using ReactiveUI;
using System.Reactive.Linq;
using WinTabberUI.Models.Settings;

namespace WinTabberUI.ViewModels.Settings;

public class AppearanceSettingsViewModel : SettingsViewModelBase
{
    private float _scaleFactor;
    private bool _scaleTodpi;
    private double _windowTileWidth;

    public AppearanceSettingsViewModel(AppearanceSettings settings) 
        : base("Appearance", FluentSystemIcons.PaintBucket_24_Regular)
    {
        _settings = settings;
        ScaleFactor = settings.ScaleFactor;
        ScaleToDpi = settings.ScaleToDpi;
        WindowTileWidth = settings.WindowTileWidth;

        _windowTileWidthScaledEvents = this
            .WhenAnyValue(
                vm => vm.ScaleFactor,
                vm => vm.WindowTileWidth,
                (scale, width) => scale * width)
            .DistinctUntilChanged();

        _windowTileWidthScaled = _windowTileWidthScaledEvents.ToProperty(this, vm => vm.WindowTileWidthScaled);
    }

    private readonly AppearanceSettings _settings;

    public float ScaleFactor
    {
        get => _scaleFactor;
        set
        {
            _settings.ScaleFactor = value;
            this.RaiseAndSetIfChanged(ref _scaleFactor, value);
        }
    }

    public bool ScaleToDpi
    {
        get => _scaleTodpi;
        set
        {
            _settings.ScaleToDpi = value;
            this.RaiseAndSetIfChanged(ref _scaleTodpi, value);
        }
    }

    public double WindowTileWidth
    {
        get => _windowTileWidth;
        set
        {
            _settings.WindowTileWidth = value;
            this.RaiseAndSetIfChanged(ref _windowTileWidth, value);
        }
    }

    public double WindowTileWidthScaled
    {
        get => _windowTileWidthScaled.Value;
    }

    private readonly IObservable<double> _windowTileWidthScaledEvents;
    private readonly ObservableAsPropertyHelper<double> _windowTileWidthScaled;
}
