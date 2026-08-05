using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using iNKORE.UI.WPF.Modern.Common.IconKeys;
using ReactiveUI;
using WinTabber.Events.Shortcuts;
using WinTabber.Events.Shortcuts.Detection;
using WinTabberUI.Models.Settings;

namespace WinTabberUI.ViewModels.Settings;

public class ShortcutsSettingsViewModel : SettingsViewModelBase, IDisposable
{
    private readonly ShortcutSettings _settings;
    private readonly IShortcutMapProvider _provider;
    private readonly CompositeDisposable _cleanUp = new();
    private readonly List<ShortcutRegistrationFailure> _registrationFailures = new();

    private int _revision;

    public ShortcutsSettingsViewModel(
        ShortcutSettings settings,
        IShortcutMapProvider provider,
        IShortcutTriggerSource triggerSource
    )
        : base("Shortcuts", FluentSystemIcons.Keyboard_24_Filled)
    {
        _settings = settings;
        _provider = provider;
        TriggerSource = triggerSource;

        Commands = ShortcutCommandExtensions
            .Bindable.Select(command => new ShortcutCommandViewModel(this, command))
            .ToList();

        Groups = Commands
            .GroupBy(vm => vm.Command.GetGroupName())
            .Select(group => new ShortcutGroupViewModel(group.Key, group.ToList()))
            .ToList();

        ResetAllCommand = ReactiveCommand.Create(ResetAll);
        _cleanUp.Add(ResetAllCommand);

        LoadFrom(provider.Current);

        // A RegisterHotKey rejection means another application already owns the chord. It is a
        // warning, never a blocker — the binding stays saved so it starts working if the other app
        // releases it.
        _cleanUp.Add(
            triggerSource
                .RegistrationFailures.ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(failure =>
                {
                    _registrationFailures.Add(failure);
                    RefreshConflicts();
                })
        );
    }

    public IShortcutTriggerSource TriggerSource { get; }

    public IReadOnlyList<ShortcutCommandViewModel> Commands { get; }

    public IReadOnlyList<ShortcutGroupViewModel> Groups { get; }

    public ReactiveCommand<Unit, Unit> ResetAllCommand { get; }

    /// <summary>
    /// Bumped on every edit. <c>SettingsViewModel</c> saves by merging each section's
    /// <c>Changed</c>, and that only fires for properties on the section itself — nested collection
    /// edits would otherwise never trigger a save.
    /// </summary>
    public int Revision
    {
        get => _revision;
        private set => this.RaiseAndSetIfChanged(ref _revision, value);
    }

    private void LoadFrom(ShortcutMap map)
    {
        foreach (var command in Commands)
        {
            command.LoadFrom(map.For(command.Command));
        }

        RefreshConflicts();
    }

    private void ResetAll()
    {
        LoadFrom(ShortcutMap.Default);
        Apply();
    }

    internal void ResetCommand(ShortcutCommandViewModel command)
    {
        command.LoadFrom(ShortcutMap.Default.For(command.Command));
        Apply();
    }

    /// <summary>
    /// Rebuilds the map from the current UI state, persists it and pushes it live. The detection
    /// layer re-registers hotkeys off the provider, so a save takes effect without a restart.
    /// </summary>
    internal void Apply()
    {
        var map = BuildMap();

        _registrationFailures.Clear();

        var serialized = ShortcutSettings.FromMap(map);
        _settings.Version = serialized.Version;
        _settings.Bindings = serialized.Bindings;

        _provider.Update(map);

        RefreshConflicts();
        Revision++;
    }

    private ShortcutMap BuildMap() =>
        new(
            Commands.SelectMany(command =>
                command
                    .Bindings.Where(b => b.Trigger is not null)
                    .Select(b => new ShortcutBinding(command.Command, b.Trigger!))
            )
        );

    private void RefreshConflicts()
    {
        var conflicts = BuildMap().FindConflicts();

        foreach (var command in Commands)
        {
            foreach (var binding in command.Bindings)
            {
                if (binding.Trigger is null)
                {
                    binding.ConflictMessage = null;
                    continue;
                }

                var conflict = conflicts.FirstOrDefault(c =>
                    string.Equals(c.Trigger.InputIdentity, binding.Trigger.InputIdentity, StringComparison.Ordinal)
                );

                if (conflict is not null)
                {
                    var others = conflict
                        .Commands.Where(c => c != command.Command)
                        .Select(c => c.GetDisplayName());
                    binding.ConflictMessage = $"Also assigned to {string.Join(", ", others)}.";
                    continue;
                }

                var rejected = _registrationFailures.Any(f =>
                    f.Command == command.Command
                    && string.Equals(f.Trigger.InputIdentity, binding.Trigger.InputIdentity, StringComparison.Ordinal)
                );

                binding.ConflictMessage = rejected
                    ? "Another application has already claimed this shortcut."
                    : null;
            }
        }
    }

    public void Dispose() => _cleanUp.Dispose();
}

public class ShortcutGroupViewModel(string name, IReadOnlyList<ShortcutCommandViewModel> commands)
{
    public string Name { get; } = name;

    public IReadOnlyList<ShortcutCommandViewModel> Commands { get; } = commands;
}

public class ShortcutCommandViewModel : ReactiveObject
{
    private readonly ShortcutsSettingsViewModel _owner;

    public ShortcutCommandViewModel(ShortcutsSettingsViewModel owner, ShortcutCommand command)
    {
        _owner = owner;
        Command = command;
        DisplayName = command.GetDisplayName();

        AddCommand = ReactiveCommand.Create(Add);
        ResetCommand = ReactiveCommand.Create(() => _owner.ResetCommand(this));
    }

    public ShortcutCommand Command { get; }

    public string DisplayName { get; }

    public ObservableCollection<ShortcutBindingViewModel> Bindings { get; } = new();

    public ReactiveCommand<Unit, Unit> AddCommand { get; }

    public ReactiveCommand<Unit, Unit> ResetCommand { get; }

    internal void LoadFrom(IReadOnlyList<ShortcutTrigger> triggers)
    {
        Bindings.Clear();
        foreach (var trigger in triggers)
        {
            Bindings.Add(new ShortcutBindingViewModel(this, trigger));
        }
    }

    private void Add()
    {
        // Added empty and immediately in capture mode: the user's next keystroke defines it.
        var binding = new ShortcutBindingViewModel(this, null) { IsEditing = true };
        Bindings.Add(binding);
    }

    internal void Remove(ShortcutBindingViewModel binding)
    {
        Bindings.Remove(binding);
        _owner.Apply();
    }

    internal void OnBindingChanged() => _owner.Apply();
}

public class ShortcutBindingViewModel : ReactiveObject
{
    private readonly ShortcutCommandViewModel _owner;
    private ShortcutTrigger? _trigger;
    private bool _isEditing;
    private string? _conflictMessage;

    public ShortcutBindingViewModel(ShortcutCommandViewModel owner, ShortcutTrigger? trigger)
    {
        _owner = owner;
        _trigger = trigger;

        EditCommand = ReactiveCommand.Create(() =>
        {
            IsEditing = true;
        });
        RemoveCommand = ReactiveCommand.Create(() => _owner.Remove(this));
    }

    public ShortcutTrigger? Trigger
    {
        get => _trigger;
        set
        {
            if (ReferenceEquals(_trigger, value))
            {
                return;
            }

            this.RaiseAndSetIfChanged(ref _trigger, value);
            IsEditing = false;
            _owner.OnBindingChanged();
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set => this.RaiseAndSetIfChanged(ref _isEditing, value);
    }

    /// <summary>Non-null when this binding collides with another command, or the OS rejected it.</summary>
    public string? ConflictMessage
    {
        get => _conflictMessage;
        internal set
        {
            this.RaiseAndSetIfChanged(ref _conflictMessage, value);
            this.RaisePropertyChanged(nameof(HasConflict));
        }
    }

    public bool HasConflict => _conflictMessage is not null;

    public ReactiveCommand<Unit, Unit> EditCommand { get; }

    public ReactiveCommand<Unit, Unit> RemoveCommand { get; }
}
