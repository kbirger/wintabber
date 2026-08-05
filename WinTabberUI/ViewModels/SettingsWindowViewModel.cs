using ReactiveUI;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using WinTabber.Events;
using WinTabber.Events.Shortcuts;
using WinTabberUI.Models.Settings;
using WinTabberUI.ViewModels.Settings;

namespace WinTabberUI.ViewModels;

public class SettingsViewModel : ReactiveObject, IDisposable
{
    private object? _selectedView;

    private BehaviorSubject<bool> _isShown;

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }

    private ApplicationSettings _settings;

    public IEnumerable<SettingsViewModelBase> Sections { get; } 

    public AppearanceSettingsViewModel Appearance { get; }
    public GeneralSettingsViewModel General { get; }
    public ShortcutsSettingsViewModel Shortcuts { get; }

    private CompositeDisposable _cleanUp;

    public SettingsViewModel(
        WinTabberEventManager winTabberEventManager,
        ApplicationSettings settings,
        IShortcutMapProvider shortcutMapProvider
    )
    {
        _isShown = new BehaviorSubject<bool>(false);

        CloseCommand = ReactiveCommand.Create(() => _isShown.OnNext(false));
        SaveCommand = ReactiveCommand.Create(() => Save());
        _settings = settings;


        Appearance = new AppearanceSettingsViewModel(_settings.Appearance);
        General = new GeneralSettingsViewModel(_settings.General);
        Shortcuts = new ShortcutsSettingsViewModel(
            _settings.Shortcuts,
            shortcutMapProvider,
            winTabberEventManager.TriggerSource
        );
        SelectedView = General;
        Sections = [
            General,
            Appearance,
            Shortcuts
        ];

        _cleanUp = new CompositeDisposable(
            SubscribeToSettingsChanges(),
            SubscribetoShowEvents(winTabberEventManager),
            CloseCommand,
            SaveCommand
        );
    }

    private IDisposable SubscribetoShowEvents(WinTabberEventManager winTabberEventManager)
    {
        return winTabberEventManager.CommandEvents
                        .Where(evt => evt.Type == EventType.CmdShowSettings)
                        .Subscribe(_ => _isShown.OnNext(true));
    }

    private IDisposable SubscribeToSettingsChanges()
    {
        return Observable.Merge(Sections.Select(section => section.Changed)).Subscribe(_ => Save());
    }

    private void Save()
    {
        _settings.Save();
    }

    public IObservable<bool> IsSettingsShown => _isShown;

    public void Hide()
    {
        _isShown.OnNext(false);
    }

    public object? SelectedView
    {
        get => _selectedView;
        set => this.RaiseAndSetIfChanged(ref _selectedView, value);
    }

    public void Dispose()
    {
        _cleanUp.Dispose();
    }
}
