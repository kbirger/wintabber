# WPF to WinUI 3 Migration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `iNKORE.UI.WPF.Modern` and the homegrown `WinTabber.UI.Common/Chrome` acrylic implementation with native WinUI 3 controls and `SystemBackdrop`, built as a second app that runs side by side with the existing WPF app.

**Architecture:** A new `winui3/` folder holds a second, independent desktop app (`WinTabberUI`, `WinTabber.UI.Common`, `WinTabber.UI.Media` + test projects) targeting Windows App SDK / WinUI 3. It references the same UI-framework-agnostic projects the WPF app already uses (`Api.Windowing`, `Api.Media`, `Events`, `Interop`, `Infrastructure`, `Common.Util`) plus a new `WinTabber.ViewModels` project extracted from the WPF app. The WPF app is not retired by this plan; it keeps building and running throughout.

**Tech Stack:** Windows App SDK / WinUI 3, WinUIEx (`WindowEx`), CommunityToolkit.WinUI.Controls.SettingsControls, ReactiveUI.WinUI, H.NotifyIcon.WinUI, CommunityToolkit.Mvvm, TUnit.

**Spec:** `docs/superpowers/specs/2026-09-12-wpf-to-winui3-migration-design.md`

## Global Constraints

- New WinUI 3 projects target `net10.0-windows10.0.26100.0` with `<UseWinUI>true</UseWinUI>`, `<WindowsPackageType>None</WindowsPackageType>`, `<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>` — all three are required or the app crashes at startup with `COMException: ClassFactory cannot supply requested class`.
- All work happens on a feature branch (not `master`). Task 1.1 below creates it.
- The WPF app (`WinTabberUI`, `WinTabber.UI.Common`, `WinTabber.UI.Media`) must keep building and passing its existing tests after every task, including every Phase 0 task.
- New `winui3/` projects use the **same base names** as their WPF counterparts (`WinTabberUI`, `WinTabber.UI.Common`, `WinTabber.UI.Media`, plus `.Tests` siblings) — the `winui3/` folder is what disambiguates them, not a name suffix.
- Deferred icon glyphs (Phase 2–4): every `FontIcon` that previously used an iNKORE glyph key and has no immediately obvious WinUI equivalent gets `Glyph="&#xE897;"` (Segoe Fluent Icons "Help" glyph) and a same-line comment `<!-- TODO(icon): originally iNKORE FluentSystemIcons.<Name> -->` (XAML) or `// TODO(icon): originally iNKORE FluentSystemIcons.<Name>` (C#). Phase 6 is the only phase allowed to replace these.
- No behavior change to the running WPF app from Phase 0 — it is a pure internal refactor, verified by the existing test suite.
- `WinTabber.ViewModels` (new, Task 1.2) references neither `System.Windows.*` nor any WinUI/iNKORE type. Both apps reference it.

---

## Phase 0 — WPF-only ViewModel cleanup

Ships independently of everything else in this plan. No WinUI3 project exists yet; every task here modifies only the current WPF app and is verified by the existing test suite.

### Task 0.1: `IconKey` enum and WPF-side icon mapping

**Files:**
- Create: `WinTabber.Infrastructure/IconKey.cs`
- Create: `WinTabber.Infrastructure.Tests/IconKeyTests.cs`
- Modify: `WinTabber.Infrastructure/Settings/ShortcutCommandCatalog.cs`
- Modify: `WinTabber.Infrastructure/WinTabber.Infrastructure.csproj` (drop the `iNKORE.UI.WPF.Modern` `PackageReference`)
- Create: `WinTabber.UI.Common/ValueConverters/IconKeyToFontIconDataConverter.cs`
- Modify: `WinTabber.UI.Common/Resources/ValueConvertersResources.xaml` (register the new converter — follow the existing entries' pattern in that file, e.g. `<c:BoolToVisibilityConverter x:Key="BoolToVisibilityConverter" />`; add `<c:IconKeyToFontIconDataConverter x:Key="IconKeyToFontIconDataConverter" />` using that file's existing `xmlns:c` prefix for `WinTabber.UI.Common.ValueConverters`)
- Modify: `WinTabberUI/ViewModels/Settings/SettingsViewModelBase.cs`
- Modify: `WinTabberUI/ViewModels/Settings/AppearanceSettingsViewModel.cs`
- Modify: `WinTabberUI/ViewModels/Settings/GeneralSettingsViewModel.cs`
- Modify: `WinTabberUI/ViewModels/Settings/ShortcutsSettingsViewModel.cs` (the `ShortcutCommandViewModel.Icon` property in the same file)
- Modify: `WinTabberUI/Views/SettingsWindow.xaml:47` (`<ui:FontIcon Icon="{Binding Icon}"/>` inside `NavigationView.MenuItemTemplate`)
- Modify: `WinTabberUI/Views/ShortcutsSettingsPage.xaml:51` (`<ui:FontIcon Icon="{Binding Icon}" />` inside `CommandRowTemplate`)

**Interfaces:**
- Produces: `WinTabber.Infrastructure.IconKey` (enum, 15 members, listed below), `WinTabber.Infrastructure.Settings.ShortcutCommandCatalog.GetIcon(this ShortcutCommand command) : IconKey`, `WinTabber.UI.Common.ValueConverters.IconKeyToFontIconDataConverter : IValueConverter`.

- [ ] **Step 1: Write the failing test for `ShortcutCommandCatalog.GetIcon`**

```csharp
// WinTabber.Infrastructure.Tests/IconKeyTests.cs
using WinTabber.Events.Shortcuts;
using WinTabber.Infrastructure;
using WinTabber.Infrastructure.Settings;

namespace WinTabber.Infrastructure.Tests;

public class IconKeyTests
{
    [Test]
    public async Task GetIcon_ReturnsIconKeyMatchingCatalogJson()
    {
        var icon = ShortcutCommand.NextWindow.GetIcon();

        await Assert.That(icon).IsEqualTo(IconKey.ArrowNext_24_Filled);
    }

    [Test]
    public async Task GetIcon_ThrowsForEveryBindableCommand()
    {
        // Every command in ShortcutCommandExtensions.Bindable must resolve to a real IconKey —
        // this is the same guarantee ShortcutCommandCatalog.For() gave before the IconKey change,
        // just asserted directly instead of only failing lazily the first time a page renders.
        foreach (var command in ShortcutCommandExtensions.Bindable)
        {
            var icon = command.GetIcon();
            await Assert.That(Enum.IsDefined(icon)).IsTrue();
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test WinTabber.Infrastructure.Tests -- --treenode-filter "/*/*/IconKeyTests/*"`
Expected: FAIL to compile — `IconKey` does not exist yet.

- [ ] **Step 3: Add the `IconKey` enum**

```csharp
// WinTabber.Infrastructure/IconKey.cs
namespace WinTabber.Infrastructure;

/// <summary>
/// UI-framework-agnostic icon identifier. Member names match the iNKORE
/// <c>FluentSystemIcons</c> field names they originally came from (see
/// <c>ShortcutCommands.json</c> and the Settings page ViewModels) so no
/// renaming pass was needed to introduce this type. Each UI project maps
/// these to its own icon representation — see
/// <c>WinTabber.UI.Common.ValueConverters.IconKeyToFontIconDataConverter</c>
/// for the WPF mapping.
/// </summary>
public enum IconKey
{
    ArrowNext_24_Filled,
    ArrowPrevious_24_Filled,
    Checkmark_24_Filled,
    Dock_24_Filled,
    ArrowMinimize_24_Filled,
    Maximize_24_Filled,
    Speaker2_24_Filled,
    Settings_24_Filled,
    PictureInPicture_24_Filled,
    AppsList_24_Filled,
    Sleep_24_Filled,
    Dismiss_24_Filled,
    PaintBucket_24_Regular,
    Settings_32_Filled,
    Keyboard_24_Filled,
}
```

- [ ] **Step 4: Change `ShortcutCommandCatalog.GetIcon` to return `IconKey`**

```csharp
// WinTabber.Infrastructure/Settings/ShortcutCommandCatalog.cs
// Replace the "using iNKORE.UI.WPF.Modern.Common.IconKeys;" using with:
using WinTabber.Infrastructure;

// Replace the GetIcon method body:
public static IconKey GetIcon(this ShortcutCommand command)
{
    var entry = For(command);
    if (!Enum.TryParse<IconKey>(entry.Icon, out var icon))
    {
        throw new InvalidOperationException($"IconKey has no member named '{entry.Icon}'.");
    }

    return icon;
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test WinTabber.Infrastructure.Tests -- --treenode-filter "/*/*/IconKeyTests/*"`
Expected: PASS

- [ ] **Step 6: Drop the `iNKORE.UI.WPF.Modern` reference from `WinTabber.Infrastructure`**

```xml
<!-- WinTabber.Infrastructure/WinTabber.Infrastructure.csproj — remove this line: -->
<PackageReference Include="iNKORE.UI.WPF.Modern" />
```

- [ ] **Step 7: Add the WPF-side `IconKey` → `FontIconData` converter**

```csharp
// WinTabber.UI.Common/ValueConverters/IconKeyToFontIconDataConverter.cs
using System.Globalization;
using System.Windows.Data;
using iNKORE.UI.WPF.Modern.Common.IconKeys;
using WinTabber.Infrastructure;

namespace WinTabber.UI.Common.ValueConverters;

/// <summary>
/// Maps the UI-framework-agnostic <see cref="IconKey"/> to the iNKORE glyph the WPF app renders
/// today. This is the only place in the WPF app allowed to translate an IconKey — everywhere else
/// (ViewModels, Infrastructure) is deliberately unaware that iNKORE exists.
/// </summary>
public sealed class IconKeyToFontIconDataConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IconKey key)
        {
            return Binding.DoNothing;
        }

        return key switch
        {
            IconKey.ArrowNext_24_Filled => FluentSystemIcons.ArrowNext_24_Filled,
            IconKey.ArrowPrevious_24_Filled => FluentSystemIcons.ArrowPrevious_24_Filled,
            IconKey.Checkmark_24_Filled => FluentSystemIcons.Checkmark_24_Filled,
            IconKey.Dock_24_Filled => FluentSystemIcons.Dock_24_Filled,
            IconKey.ArrowMinimize_24_Filled => FluentSystemIcons.ArrowMinimize_24_Filled,
            IconKey.Maximize_24_Filled => FluentSystemIcons.Maximize_24_Filled,
            IconKey.Speaker2_24_Filled => FluentSystemIcons.Speaker2_24_Filled,
            IconKey.Settings_24_Filled => FluentSystemIcons.Settings_24_Filled,
            IconKey.PictureInPicture_24_Filled => FluentSystemIcons.PictureInPicture_24_Filled,
            IconKey.AppsList_24_Filled => FluentSystemIcons.AppsList_24_Filled,
            IconKey.Sleep_24_Filled => FluentSystemIcons.Sleep_24_Filled,
            IconKey.Dismiss_24_Filled => FluentSystemIcons.Dismiss_24_Filled,
            IconKey.PaintBucket_24_Regular => FluentSystemIcons.PaintBucket_24_Regular,
            IconKey.Settings_32_Filled => FluentSystemIcons.Settings_32_Filled,
            IconKey.Keyboard_24_Filled => FluentSystemIcons.Keyboard_24_Filled,
            _ => throw new ArgumentOutOfRangeException(nameof(value), key, "Unmapped IconKey."),
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

- [ ] **Step 8: Register the converter and update the four ViewModel files**

Add one line to `WinTabber.UI.Common/Resources/ValueConvertersResources.xaml`, next to the other converter entries (matching the file's existing `xmlns:c` prefix for `WinTabber.UI.Common.ValueConverters`):

```xml
<c:IconKeyToFontIconDataConverter x:Key="IconKeyToFontIconDataConverter" />
```

```csharp
// WinTabberUI/ViewModels/Settings/SettingsViewModelBase.cs — replace the iNKORE using and the Icon type:
using ReactiveUI;
using WinTabber.Infrastructure;

namespace WinTabberUI.ViewModels.Settings
{
    public abstract class SettingsViewModelBase : ReactiveObject
    {
        public string Name { get; }

        public IconKey Icon { get; }

        protected SettingsViewModelBase(string name, IconKey icon)
        {
            Name = name;
            Icon = icon;
        }
    }
}
```

```csharp
// WinTabberUI/ViewModels/Settings/AppearanceSettingsViewModel.cs — replace the iNKORE using and base() call:
using ReactiveUI;
using System.Reactive.Linq;
using WinTabber.Infrastructure;
using WinTabberUI.Models.Settings;

namespace WinTabberUI.ViewModels.Settings;

public class AppearanceSettingsViewModel : SettingsViewModelBase
{
    // ...
    public AppearanceSettingsViewModel(AppearanceSettings settings)
        : base("Appearance", IconKey.PaintBucket_24_Regular)
    {
        // body unchanged
    }
    // ... rest of file unchanged
}
```

```csharp
// WinTabberUI/ViewModels/Settings/GeneralSettingsViewModel.cs — replace the iNKORE using and base() call:
using WinTabber.Infrastructure;
// (remove "using iNKORE.UI.WPF.Modern.Common.IconKeys;")
// ...
public GeneralSettingsViewModel(GeneralSettings settings, GsudoElevationLauncher gsudoElevationLauncher)
    : base("General", IconKey.Settings_32_Filled)
{
    // body unchanged
}
```

```csharp
// WinTabberUI/ViewModels/Settings/ShortcutsSettingsViewModel.cs — replace the iNKORE using, the base() call, and ShortcutCommandViewModel.Icon's type:
using WinTabber.Infrastructure;
// (remove "using iNKORE.UI.WPF.Modern.Common.IconKeys;")
// ...
public ShortcutsSettingsViewModel(
    ShortcutSettings settings,
    IShortcutMapProvider provider,
    IShortcutTriggerSource triggerSource
)
    : base("Shortcuts", IconKey.Keyboard_24_Filled)
{
    // body unchanged
}

// ... further down in the same file:
public class ShortcutCommandViewModel : ReactiveObject
{
    // ...
    public IconKey Icon { get; }   // was FontIconData
    // constructor body unchanged (still `Icon = command.GetIcon();`)
}
```

```xml
<!-- WinTabberUI/Views/SettingsWindow.xaml:47 -->
<ui:FontIcon Icon="{Binding Icon, Converter={StaticResource IconKeyToFontIconDataConverter}}"/>
```

```xml
<!-- WinTabberUI/Views/ShortcutsSettingsPage.xaml:51 -->
<ui:FontIcon Icon="{Binding Icon, Converter={StaticResource IconKeyToFontIconDataConverter}}" />
```

- [ ] **Step 9: Build and run the full existing test suite**

Run: `dotnet build WinTabber.slnx` then `dotnet test --solution WinTabber.slnx`
Expected: builds clean, all existing tests still pass, `IconKeyTests` passes.

- [ ] **Step 10: Manually verify no visual change**

Run: `dotnet run --project WinTabberUI/WinTabberUI.csproj`, open Settings. The three NavigationView icons (Home/Apps/Games are unrelated `ui:SegoeFluentIcons` and are untouched) plus every Shortcuts-page command icon must look identical to before this task.

- [ ] **Step 11: Commit**

```bash
git add WinTabber.Infrastructure/IconKey.cs WinTabber.Infrastructure.Tests/IconKeyTests.cs \
  WinTabber.Infrastructure/Settings/ShortcutCommandCatalog.cs WinTabber.Infrastructure/WinTabber.Infrastructure.csproj \
  WinTabber.UI.Common/ValueConverters/IconKeyToFontIconDataConverter.cs WinTabber.UI.Common/Resources/ValueConvertersResources.xaml \
  WinTabberUI/ViewModels/Settings/SettingsViewModelBase.cs WinTabberUI/ViewModels/Settings/AppearanceSettingsViewModel.cs \
  WinTabberUI/ViewModels/Settings/GeneralSettingsViewModel.cs WinTabberUI/ViewModels/Settings/ShortcutsSettingsViewModel.cs \
  WinTabberUI/Views/SettingsWindow.xaml WinTabberUI/Views/ShortcutsSettingsPage.xaml
git commit -m "refactor: replace iNKORE IconKeys with UI-agnostic IconKey enum

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

### Task 0.2: `WindowRenameViewModel` off `DependencyObject`

**Files:**
- Modify: `WinTabberUI/ViewModels/WindowRenameViewModel.cs`
- Modify: `WinTabberUI/Bootstrapper.cs:160` (registration is `AddTransient<WindowRenameViewModel>()` — unaffected by this change, listed so the implementer can confirm nothing else needs updating)

**Interfaces:**
- Produces: `WinTabberUI.ViewModels.WindowRenameViewModel : ReactiveObject` with `WindowItem? WindowItem { get; set; }`, `string NewTitle { get; set; }`, `void Apply()` (all unchanged in signature, changed in implementation).

- [ ] **Step 1: Find every consumer of `WindowRenameViewModel`'s properties**

Run: `grep -rn "WindowRenameViewModel" WinTabberUI --include=*.xaml --include=*.cs`
Expected: bindings only ever read/write `WindowItem` and `NewTitle` as plain CLR properties (no `DependencyProperty`-specific API like `GetValue`/`SetValue`/`RegisterAttached` used from outside this class) — if that is not what the search shows, stop and re-scope this task before continuing, since a WPF binding depending on `DependencyObject`-specific behavior (e.g. attached-property syntax) would need a different fix.

- [ ] **Step 2: Rewrite the class onto `ReactiveObject`**

```csharp
// WinTabberUI/ViewModels/WindowRenameViewModel.cs
using ReactiveUI;

namespace WinTabberUI.ViewModels;

public class WindowRenameViewModel : ReactiveObject
{
    private WindowItem? _windowItem;
    private string _newTitle = "";

    public WindowItem? WindowItem
    {
        get => _windowItem;
        set => this.RaiseAndSetIfChanged(ref _windowItem, value);
    }

    public string NewTitle
    {
        get => _newTitle;
        set => this.RaiseAndSetIfChanged(ref _newTitle, value);
    }

    public void Apply()
    {
        if (WindowItem is not null)
        {
            WindowItem.Title = NewTitle;
        }
    }
}
```

- [ ] **Step 3: Build and manually verify the rename flow still works**

Run: `dotnet build WinTabber.slnx`. Then `dotnet run --project WinTabberUI/WinTabberUI.csproj`, open the window selector, rename a window tile's title via its `EditableTextBlock`, confirm the new title is applied and the WPF binding (a plain `{Binding NewTitle}`-style binding, since this is no longer a `DependencyObject`) still updates live as you type.

- [ ] **Step 4: Commit**

```bash
git add WinTabberUI/ViewModels/WindowRenameViewModel.cs
git commit -m "refactor: rewrite WindowRenameViewModel off WPF DependencyObject

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

### Task 0.3: Abstract application shutdown

**Files:**
- Create: `WinTabberUI/Services/IAppLifecycle.cs`
- Create: `WinTabberUI/Services/WpfAppLifecycle.cs`
- Modify: `WinTabberUI/ViewModels/NotifyIconViewModel.cs`
- Modify: `WinTabberUI/Bootstrapper.cs` (register the new service)

**Interfaces:**
- Produces: `WinTabberUI.Services.IAppLifecycle` with `void Shutdown()`; `WinTabberUI.Services.WpfAppLifecycle : IAppLifecycle`.
- Consumes: `System.Windows.Application` (already registered as a singleton by `Bootstrapper.RegisterApplication`, `WinTabberUI/Bootstrapper.cs:148-152`).

- [ ] **Step 1: Add the interface**

```csharp
// WinTabberUI/Services/IAppLifecycle.cs
namespace WinTabberUI.Services;

/// <summary>
/// Abstracts application-level lifecycle actions away from any specific UI framework, so a
/// ViewModel that needs to shut the app down does not have to reference System.Windows.Application
/// directly. See WinTabberUI/ViewModels/NotifyIconViewModel.cs.
/// </summary>
public interface IAppLifecycle
{
    void Shutdown();
}
```

- [ ] **Step 2: Add the WPF implementation**

```csharp
// WinTabberUI/Services/WpfAppLifecycle.cs
using System.Windows;

namespace WinTabberUI.Services;

public sealed class WpfAppLifecycle : IAppLifecycle
{
    private readonly Application _application;

    public WpfAppLifecycle(Application application)
    {
        _application = application;
    }

    public void Shutdown() => _application.Shutdown();
}
```

- [ ] **Step 3: Register it in the DI container**

```csharp
// WinTabberUI/Bootstrapper.cs — inside AddCoreServices, add:
private static IServiceCollection AddCoreServices(this IServiceCollection services)
{
    return services
        .AddSingleton<AutoStartupService>()
        .AddSingleton<BackgroundServiceContainer>()
        .AddSingleton<IAppLifecycle, WpfAppLifecycle>();
}
```

Add `using WinTabberUI.Services;` at the top of `Bootstrapper.cs` if not already present (it already is, per the existing using list).

- [ ] **Step 4: Update `NotifyIconViewModel` to depend on `IAppLifecycle`**

```csharp
// WinTabberUI/ViewModels/NotifyIconViewModel.cs
using ReactiveUI;
using System.Reactive;
using WinTabber.Api.Windowing.Suspension;
using WinTabber.Events;
using WinTabberUI.Services;

namespace WinTabberUI.ViewModels;

public partial class NotifyIconViewModel : ReactiveObject
{
    private readonly IAppLifecycle _appLifecycle;

    public NotifyIconViewModel(
        WinTabberEventManager eventManager,
        IProcessSuspensionService suspensionService,
        MediaDebugStateService mediaDebugState,
        IAppLifecycle appLifecycle
    )
    {
        _appLifecycle = appLifecycle;

        ExitApplicationCommand = ReactiveCommand.Create(ExitApplication);
        ShowSettingsCommand = ReactiveCommand.Create(ShowSettings(eventManager));
        ShowWindowCommand = ReactiveCommand.Create(ShowSelector(eventManager));
        PauseHooksCommand = ReactiveCommand.Create(() =>
        {
            if (eventManager.IsRunning)
            {
                eventManager.Pause();
            }
            else
            {
                eventManager.Start();
            }
        });

        _areHooksActive = eventManager.WhenAnyValue(em => em.IsRunning)
            .ToProperty(this, vm => vm.AreHooksActive);

        MediaDebugCommand = ReactiveCommand.Create(mediaDebugState.Toggle);
        _isMediaDebugEnabled = mediaDebugState.IsEnabledChanges
            .ToProperty(this, vm => vm.IsMediaDebugEnabled);

        ResumeAllSuspendedCommand = ReactiveCommand.Create(
            suspensionService.ResumeAll,
            suspensionService.HasSuspendedChanges);
    }

    private ObservableAsPropertyHelper<bool> _areHooksActive;
    private ObservableAsPropertyHelper<bool> _isMediaDebugEnabled;
    public bool AreHooksActive => _areHooksActive.Value;
    public bool IsMediaDebugEnabled => _isMediaDebugEnabled.Value;
    public ReactiveCommand<Unit, Unit> MediaDebugCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> ExitApplicationCommand { get; }
    public ReactiveCommand<Unit, Unit> ShowWindowCommand { get; }
    public ReactiveCommand<Unit, Unit> PauseHooksCommand { get; }
    public ReactiveCommand<Unit, Unit> ResumeAllSuspendedCommand { get; }

    public Action ShowSettings(WinTabberEventManager eventManager) =>
        () => eventManager.SendEvent(EventType.CmdShowSettings);

    public Action ShowSelector(WinTabberEventManager eventManager) =>
        () => eventManager.SendEvent(EventType.CmdNextWindow);

    /// <summary>Shuts down the application.</summary>
    public void ExitApplication() => _appLifecycle.Shutdown();
}
```

Note: `SysColorsCommand` and its `SysColor` dialog usage are removed from this class in Task 0.4, not here — this step only removes the `Application.Current.Shutdown()` call. If Task 0.4 has not run yet, leave `SysColorsCommand` in place unchanged when writing this step; the final file after both 0.3 and 0.4 have run is shown in Task 0.4's Step 2.

- [ ] **Step 5: Build**

Run: `dotnet build WinTabber.slnx`
Expected: builds clean. (No automated test exists for this ViewModel today — this is a straight DI-constructor change with no new branching logic to unit test; manual verification happens in Step 6.)

- [ ] **Step 6: Manually verify exit still works**

Run: `dotnet run --project WinTabberUI/WinTabberUI.csproj`, right-click the tray icon, choose Exit, confirm the app closes.

- [ ] **Step 7: Commit**

```bash
git add WinTabberUI/Services/IAppLifecycle.cs WinTabberUI/Services/WpfAppLifecycle.cs \
  WinTabberUI/ViewModels/NotifyIconViewModel.cs WinTabberUI/Bootstrapper.cs
git commit -m "refactor: abstract app shutdown behind IAppLifecycle

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

### Task 0.4: Remove the SysColors tray menu entry

**Files:**
- Find and modify: the tray context-menu XAML that exposes `SysColorsCommand` (locate with the search in Step 1 below — it is wherever `H.NotifyIcon.Wpf`'s tray menu is declared, most likely `WinTabberUI/App.xaml` or a dedicated resource dictionary merged into it; it was not one of the files read while producing this plan, so the implementer must find it first rather than trust a guessed path)
- Modify: `WinTabberUI/ViewModels/NotifyIconViewModel.cs` (remove only the `SysColorsCommand`'s *menu wiring is external*, so this file itself does not change here unless the menu XAML binds `Command="{Binding SysColorsCommand}"` from inside a `DataTemplate` this task also touches — leave `SysColorsCommand`, its constructor wiring, and the `SysColor` dialog call fully intact in this .cs file, per the spec's explicit instruction to keep them for a future revisit)

- [ ] **Step 1: Find the menu item**

Run: `grep -rn "SysColorsCommand" WinTabberUI`
Expected: one hit in `NotifyIconViewModel.cs` (the command definition — unchanged) and one hit in a XAML file (a `MenuItem Command="{Binding SysColorsCommand}"` or equivalent, inside whatever declares the tray's context menu).

- [ ] **Step 2: Delete only the found `MenuItem`**

Remove the single `<MenuItem ... Command="{Binding SysColorsCommand}" .../>` element found in Step 1. Do not touch any sibling menu items (Settings, Exit, etc.) or any other part of the file.

- [ ] **Step 3: Build and manually verify**

Run: `dotnet build WinTabber.slnx`, then `dotnet run --project WinTabberUI/WinTabberUI.csproj`, right-click the tray icon, confirm the "Sys Colors" (or however it is labeled) entry is gone and every other tray menu entry still works.

- [ ] **Step 4: Commit**

```bash
git add -A  # after confirming `git status` shows only the intended menu file changed
git commit -m "chore: remove SysColors tray menu entry, keep command for later

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Phase 1 — Scaffold the WinUI 3 app and `WinTabber.ViewModels`

### Task 1.1: Create the feature branch

- [ ] **Step 1: Branch**

```bash
git checkout -b winui3-migration
```

### Task 1.2: Extract `WinTabber.ViewModels`

**Files:**
- Create: `WinTabber.ViewModels/WinTabber.ViewModels.csproj`
- Move (git mv, preserving history): every file under `WinTabberUI/ViewModels/**` to the same relative path under `WinTabber.ViewModels/` (i.e. `WinTabberUI/ViewModels/WindowItem.cs` → `WinTabber.ViewModels/WindowItem.cs`, `WinTabberUI/ViewModels/Settings/AppearanceSettingsViewModel.cs` → `WinTabber.ViewModels/Settings/AppearanceSettingsViewModel.cs`, and so on for all 17 files: `ApplicationStateViewModel.cs`, `ApplicationStateViewModelFactory.cs`, `DockWindowViewModel.cs`, `MediaDebugRows.cs`, `MediaDebugViewModel.cs`, `NotifyIconViewModel.cs`, `SectionItemViewModel.cs`, `SettingsWindowViewModel.cs`, `SuspendedWindowItemViewModel.cs`, `SuspendedWindowsViewModel.cs`, `ThumbnailWindowViewModel.cs`, `WindowItem.cs`, `WindowRenameViewModel.cs`, `WindowSelectorViewModel.cs`, and the four files already under `Settings/`)
- Modify: every moved file's `namespace` line, `WinTabberUI.ViewModels` → `WinTabber.ViewModels`, `WinTabberUI.ViewModels.Settings` → `WinTabber.ViewModels.Settings`
- Modify: `WinTabberUI/WinTabberUI.csproj` (add `<ProjectReference Include="..\WinTabber.ViewModels\WinTabber.ViewModels.csproj" />`)
- Modify: `WinTabber.slnx` (add the new project)
- Modify: every `.cs` file under `WinTabberUI/` (outside `ViewModels/`) and `WinTabberUI/Views/*.xaml` that references `WinTabberUI.ViewModels` or `WinTabberUI.ViewModels.Settings` — `using WinTabberUI.ViewModels;` → `using WinTabber.ViewModels;`, `xmlns:vm="clr-namespace:WinTabberUI.ViewModels"` → `xmlns:vm="clr-namespace:WinTabber.ViewModels;assembly=WinTabber.ViewModels"`, and the `settingsvm` prefix equivalently for the `.Settings` sub-namespace (`WinTabberUI/Bootstrapper.cs`, `WinTabberUI/Views/SettingsWindow.xaml`, `WinTabberUI/Views/GeneralSettingsPage.xaml`, `WinTabberUI/Views/AppearanceSettingsPage.xaml`, `WinTabberUI/Views/ShortcutsSettingsPage.xaml`, `WinTabberUI/Views/WindowSelectorWindow.xaml`, `WinTabberUI/Views/ShortcutCaptureDialog.xaml.cs`, `WinTabberUI/EditableTextBlock.xaml`, and any other file `grep -rl "WinTabberUI.ViewModels" WinTabberUI` finds — run that search as Step 1 below rather than trusting this list to be exhaustive)

**Interfaces:**
- Produces: `WinTabber.ViewModels` assembly containing every type that was under `WinTabberUI.ViewModels`/`WinTabberUI.ViewModels.Settings`, same type and member names, new namespace only.

- [ ] **Step 1: Enumerate every consumer before moving anything**

Run: `grep -rl "WinTabberUI\.ViewModels" WinTabberUI --include=*.cs --include=*.xaml`
Keep this file list; every one of them needs its namespace reference updated in Step 4.

- [ ] **Step 2: Create the new project**

```xml
<!-- WinTabber.ViewModels/WinTabber.ViewModels.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
        <Platform>x64</Platform>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="ReactiveUI" />
        <PackageReference Include="DynamicData" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\WinTabber.Api.Media\WinTabber.Api.Media.csproj" />
        <ProjectReference Include="..\WinTabber.Api.Windowing\WinTabber.Api.Windowing.csproj" />
        <ProjectReference Include="..\WinTabber.Events\WinTabber.Events.csproj" />
        <ProjectReference Include="..\WinTabber.Infrastructure\WinTabber.Infrastructure.csproj" />
        <ProjectReference Include="..\WinTabber.Interop\WinTabber.Interop.csproj" />
        <ProjectReference Include="..\WinTabber.Common.Util\WinTabber.Common.Util.csproj" />
        <ProjectReference Include="..\WinTabber.Generators\WinTabber.Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    </ItemGroup>
</Project>
```

Note deliberately **no** `<UseWPF>` and no `iNKORE`/`H.NotifyIcon` package — that absence is what this task is for. If a later build error names a type this csproj doesn't yet reference (e.g. a settings model type), add the specific missing `ProjectReference`/`PackageReference` rather than widening this list speculatively.

- [ ] **Step 3: Move every file**

```bash
mkdir -p WinTabber.ViewModels/Settings
git mv WinTabberUI/ViewModels/ApplicationStateViewModel.cs WinTabber.ViewModels/
git mv WinTabberUI/ViewModels/ApplicationStateViewModelFactory.cs WinTabber.ViewModels/
git mv WinTabberUI/ViewModels/DockWindowViewModel.cs WinTabber.ViewModels/
git mv WinTabberUI/ViewModels/MediaDebugRows.cs WinTabber.ViewModels/
git mv WinTabberUI/ViewModels/MediaDebugViewModel.cs WinTabber.ViewModels/
git mv WinTabberUI/ViewModels/NotifyIconViewModel.cs WinTabber.ViewModels/
git mv WinTabberUI/ViewModels/SectionItemViewModel.cs WinTabber.ViewModels/
git mv WinTabberUI/ViewModels/SettingsWindowViewModel.cs WinTabber.ViewModels/
git mv WinTabberUI/ViewModels/SuspendedWindowItemViewModel.cs WinTabber.ViewModels/
git mv WinTabberUI/ViewModels/SuspendedWindowsViewModel.cs WinTabber.ViewModels/
git mv WinTabberUI/ViewModels/ThumbnailWindowViewModel.cs WinTabber.ViewModels/
git mv WinTabberUI/ViewModels/WindowItem.cs WinTabber.ViewModels/
git mv WinTabberUI/ViewModels/WindowRenameViewModel.cs WinTabber.ViewModels/
git mv WinTabberUI/ViewModels/WindowSelectorViewModel.cs WinTabber.ViewModels/
git mv WinTabberUI/ViewModels/Settings/AppearanceSettingsViewModel.cs WinTabber.ViewModels/Settings/
git mv WinTabberUI/ViewModels/Settings/GeneralSettingsViewModel.cs WinTabber.ViewModels/Settings/
git mv WinTabberUI/ViewModels/Settings/SettingsViewModelBase.cs WinTabber.ViewModels/Settings/
git mv WinTabberUI/ViewModels/Settings/ShortcutsSettingsViewModel.cs WinTabber.ViewModels/Settings/
```

- [ ] **Step 4: Fix namespaces**

In every file moved in Step 3, change `namespace WinTabberUI.ViewModels;` (or the block form `namespace WinTabberUI.ViewModels { ... }`) to `namespace WinTabber.ViewModels;`, and `WinTabberUI.ViewModels.Settings` to `WinTabber.ViewModels.Settings`, preserving each file's existing brace-vs-semicolon namespace style.

In every file the Step 1 search found (outside the moved set), change:
- `using WinTabberUI.ViewModels;` → `using WinTabber.ViewModels;`
- `using WinTabberUI.ViewModels.Settings;` → `using WinTabber.ViewModels.Settings;`
- `xmlns:vm="clr-namespace:WinTabberUI.ViewModels"` → `xmlns:vm="clr-namespace:WinTabber.ViewModels;assembly=WinTabber.ViewModels"`
- `xmlns:settingsvm="clr-namespace:WinTabberUI.ViewModels.Settings"` → `xmlns:settingsvm="clr-namespace:WinTabber.ViewModels.Settings;assembly=WinTabber.ViewModels"`

- [ ] **Step 5: Wire the new project into the WPF app and the solution**

```xml
<!-- WinTabberUI/WinTabberUI.csproj — add alongside the other ProjectReferences: -->
<ProjectReference Include="..\WinTabber.ViewModels\WinTabber.ViewModels.csproj" />
```

```xml
<!-- WinTabber.slnx — add alongside the other <Project> entries: -->
<Project Path="WinTabber.ViewModels/WinTabber.ViewModels.csproj" />
```

- [ ] **Step 6: Build and run the full existing test suite**

Run: `dotnet build WinTabber.slnx` then `dotnet test --solution WinTabber.slnx`
Expected: builds clean, all existing tests pass. Fix any missed reference the compiler surfaces (a missed file in Step 1's search, a missing `ProjectReference` in the new csproj) before proceeding.

- [ ] **Step 7: Manually verify the WPF app still runs unchanged**

Run: `dotnet run --project WinTabberUI/WinTabberUI.csproj`, exercise the window selector, settings, and tray menu — no behavior should differ from before this task.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "refactor: extract WinTabberUI.ViewModels into shared WinTabber.ViewModels project

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

### Task 1.3: Scaffold `winui3/WinTabberUI` — empty shell app boots

**Files:**
- Create: `winui3/WinTabberUI/WinTabberUI.csproj`
- Create: `winui3/WinTabberUI/app.manifest`
- Create: `winui3/WinTabberUI/App.xaml`
- Create: `winui3/WinTabberUI/App.xaml.cs`
- Create: `winui3/WinTabberUI/MainWindow.xaml` (temporary — a placeholder window proving the app boots; replaced by the real `SettingsWindow`/tray-only startup in Phase 3/5)
- Create: `winui3/WinTabberUI/MainWindow.xaml.cs`
- Modify: `WinTabber.slnx`

- [ ] **Step 1: Project file**

```xml
<!-- winui3/WinTabberUI/WinTabberUI.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
        <TargetPlatformMinVersion>10.0.19041.0</TargetPlatformMinVersion>
        <UseWinUI>true</UseWinUI>
        <Platform>x64</Platform>
        <OutputType>WinExe</OutputType>
        <ApplicationManifest>app.manifest</ApplicationManifest>
        <WindowsPackageType>None</WindowsPackageType>
        <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>

        <Product>WinTabber</Product>
        <Company>Kirill Birger</Company>
        <Description>A Windows desktop utility for window switching and window management (WinUI 3).</Description>
        <Copyright>Copyright (c) 2026 Kirill Birger</Copyright>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.WindowsAppSDK" />
        <PackageReference Include="Microsoft.Windows.SDK.BuildTools" />
        <PackageReference Include="WinUIEx" />
        <PackageReference Include="CommunityToolkit.Mvvm" />
        <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
        <PackageReference Include="ReactiveUI" />
        <PackageReference Include="ReactiveUI.WinUI" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\..\WinTabber.Api.Media\WinTabber.Api.Media.csproj" />
        <ProjectReference Include="..\..\WinTabber.Api.Windowing\WinTabber.Api.Windowing.csproj" />
        <ProjectReference Include="..\..\WinTabber.Events\WinTabber.Events.csproj" />
        <ProjectReference Include="..\..\WinTabber.Infrastructure\WinTabber.Infrastructure.csproj" />
        <ProjectReference Include="..\..\WinTabber.Interop\WinTabber.Interop.csproj" />
        <ProjectReference Include="..\..\WinTabber.Common.Util\WinTabber.Common.Util.csproj" />
        <ProjectReference Include="..\..\WinTabber.ViewModels\WinTabber.ViewModels.csproj" />
        <ProjectReference Include="..\WinTabber.UI.Common\WinTabber.UI.Common.csproj" />
        <ProjectReference Include="..\WinTabber.UI.Media\WinTabber.UI.Media.csproj" />
    </ItemGroup>
</Project>
```

`Directory.Packages.props` (repo root, central package management) needs version entries added for any of these packages not already listed: `Microsoft.WindowsAppSDK`, `Microsoft.Windows.SDK.BuildTools`, `WinUIEx`, `ReactiveUI.WinUI`, `CommunityToolkit.WinUI.Controls.SettingsControls`, `H.NotifyIcon.WinUI`. Add each with `<PackageVersion Include="..." Version="..." />`, picking the latest stable version available at implementation time (check `dotnet package search <name>` — do not guess a version number here, since this plan was written without querying NuGet).

- [ ] **Step 2: Minimal app manifest**

```xml
<!-- winui3/WinTabberUI/app.manifest -->
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <activeCodePage xmlns="http://schemas.microsoft.com/SMI/2019/WindowsSettings">UTF-8</activeCodePage>
    </windowsSettings>
  </application>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
</assembly>
```

- [ ] **Step 3: App.xaml / App.xaml.cs**

```xml
<!-- winui3/WinTabberUI/App.xaml -->
<Application
    x:Class="WinTabberUI.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

```csharp
// winui3/WinTabberUI/App.xaml.cs
using Microsoft.UI.Xaml;

namespace WinTabberUI;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
```

- [ ] **Step 4: Placeholder `MainWindow`**

```xml
<!-- winui3/WinTabberUI/MainWindow.xaml -->
<Window
    x:Class="WinTabberUI.MainWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid>
        <TextBlock Text="WinTabber (WinUI 3 shell — Phase 1)" HorizontalAlignment="Center" VerticalAlignment="Center" />
    </Grid>
</Window>
```

```csharp
// winui3/WinTabberUI/MainWindow.xaml.cs
using Microsoft.UI.Xaml;

namespace WinTabberUI;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 5: Add to the solution**

```xml
<!-- WinTabber.slnx -->
<Project Path="winui3/WinTabberUI/WinTabberUI.csproj" />
```

- [ ] **Step 6: Build and run**

Run: `dotnet build WinTabber.slnx` then `dotnet run --project winui3/WinTabberUI/WinTabberUI.csproj`
Expected: a window titled with the placeholder text opens. If it instead crashes with `COMException: ClassFactory cannot supply requested class`, re-check `WindowsPackageType`/`WindowsAppSDKSelfContained` in Step 1 before anything else — this is the single most common WinUI3-unpackaged-app failure per the migration skill's troubleshooting table.

- [ ] **Step 7: Commit**

```bash
git add winui3/WinTabberUI WinTabber.slnx Directory.Packages.props
git commit -m "feat: scaffold winui3/WinTabberUI shell app

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

### Task 1.4: Scaffold `winui3/WinTabber.UI.Common` and `winui3/WinTabber.UI.Common.Tests`

**Files:**
- Create: `winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj`
- Create: `winui3/WinTabber.UI.Common.Tests/WinTabber.UI.Common.Tests.csproj`
- Modify: `WinTabber.slnx`
- Modify: `winui3/WinTabberUI/WinTabberUI.csproj` (the `ProjectReference` to this project already exists from Task 1.3, Step 1 — nothing further needed here)

- [ ] **Step 1: Project file (empty of behaviors/controls for now — Phase 2 fills it in)**

```xml
<!-- winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
        <TargetPlatformMinVersion>10.0.19041.0</TargetPlatformMinVersion>
        <UseWinUI>true</UseWinUI>
        <Platform>x64</Platform>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.WindowsAppSDK" />
        <PackageReference Include="CommunityToolkit.WinUI.Controls.SettingsControls" />
        <PackageReference Include="WinUIEx" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\..\WinTabber.Common.Util\WinTabber.Common.Util.csproj" />
        <ProjectReference Include="..\..\WinTabber.Events\WinTabber.Events.csproj" />
        <ProjectReference Include="..\..\WinTabber.Interop\WinTabber.Interop.csproj" />
    </ItemGroup>

    <ItemGroup>
        <InternalsVisibleTo Include="WinTabber.UI.Common.Tests" />
    </ItemGroup>
</Project>
```

- [ ] **Step 2: Test project, mirroring the WPF one's shape**

```xml
<!-- winui3/WinTabber.UI.Common.Tests/WinTabber.UI.Common.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
        <TargetPlatformMinVersion>10.0.19041.0</TargetPlatformMinVersion>
        <UseWinUI>true</UseWinUI>
        <Platform>x64</Platform>
        <IsPackable>false</IsPackable>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="TUnit" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\WinTabber.UI.Common\WinTabber.UI.Common.csproj" />
    </ItemGroup>
</Project>
```

- [ ] **Step 3: Add both to the solution**

```xml
<!-- WinTabber.slnx -->
<Project Path="winui3/WinTabber.UI.Common/WinTabber.UI.Common.csproj" />
<Project Path="winui3/WinTabber.UI.Common.Tests/WinTabber.UI.Common.Tests.csproj" />
```

- [ ] **Step 4: Build**

Run: `dotnet build WinTabber.slnx`
Expected: builds clean (empty projects with no source files yet still produce a valid, empty assembly).

- [ ] **Step 5: Commit**

```bash
git add winui3/WinTabber.UI.Common winui3/WinTabber.UI.Common.Tests WinTabber.slnx
git commit -m "feat: scaffold winui3/WinTabber.UI.Common and its test project

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

### Task 1.5: Scaffold `winui3/WinTabber.UI.Media` and `winui3/WinTabber.UI.Media.Tests`

**Files:**
- Create: `winui3/WinTabber.UI.Media/WinTabber.UI.Media.csproj`
- Create: `winui3/WinTabber.UI.Media.Tests/WinTabber.UI.Media.Tests.csproj`
- Modify: `WinTabber.slnx`

- [ ] **Step 1: Project file**

```xml
<!-- winui3/WinTabber.UI.Media/WinTabber.UI.Media.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
        <TargetPlatformMinVersion>10.0.19041.0</TargetPlatformMinVersion>
        <UseWinUI>true</UseWinUI>
        <Platform>x64</Platform>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Microsoft.WindowsAppSDK" />
        <PackageReference Include="CommunityToolkit.Mvvm" />
        <PackageReference Include="Microsoft.Extensions.DependencyInjection" />
        <PackageReference Include="ReactiveUI" />
        <PackageReference Include="ReactiveUI.WinUI" />
        <PackageReference Include="ReactiveUI.SourceGenerators" />
        <PackageReference Include="WinUIEx" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\..\WinTabber.Api.Media\WinTabber.Api.Media.csproj" />
        <ProjectReference Include="..\..\WinTabber.Common.Util\WinTabber.Common.Util.csproj" />
        <ProjectReference Include="..\..\WinTabber.Events\WinTabber.Events.csproj" />
        <ProjectReference Include="..\..\WinTabber.Generators\WinTabber.Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
        <ProjectReference Include="..\..\WinTabber.Interop\WinTabber.Interop.csproj" />
        <ProjectReference Include="..\WinTabber.UI.Common\WinTabber.UI.Common.csproj" />
    </ItemGroup>
</Project>
```

- [ ] **Step 2: Test project**

```xml
<!-- winui3/WinTabber.UI.Media.Tests/WinTabber.UI.Media.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net10.0-windows10.0.26100.0</TargetFramework>
        <TargetPlatformMinVersion>10.0.19041.0</TargetPlatformMinVersion>
        <UseWinUI>true</UseWinUI>
        <Platform>x64</Platform>
        <IsPackable>false</IsPackable>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="TUnit" />
    </ItemGroup>

    <ItemGroup>
        <ProjectReference Include="..\WinTabber.UI.Media\WinTabber.UI.Media.csproj" />
    </ItemGroup>
</Project>
```

- [ ] **Step 3: Add both to the solution**

```xml
<!-- WinTabber.slnx -->
<Project Path="winui3/WinTabber.UI.Media/WinTabber.UI.Media.csproj" />
<Project Path="winui3/WinTabber.UI.Media.Tests/WinTabber.UI.Media.Tests.csproj" />
```

- [ ] **Step 4: Build**

Run: `dotnet build WinTabber.slnx`
Expected: builds clean.

- [ ] **Step 5: Commit**

```bash
git add winui3/WinTabber.UI.Media winui3/WinTabber.UI.Media.Tests WinTabber.slnx
git commit -m "feat: scaffold winui3/WinTabber.UI.Media and its test project

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Phase 2 — Port `WinTabber.UI.Common`'s converters and window commands off WPF

Every file currently under `WinTabber.UI.Common` (excluding `Chrome/`, which is
deleted per the design spec, not ported) was read in full before writing this
phase. Two groups of files port cleanly with mechanical, verified changes
(Tasks 2.1–2.3, below). A third group — the hint-overlay system
(`Behaviors/HintBehavior.cs`, its kernels, `Hints/**`, `HintAdorner.cs`,
`HintPosition.cs`) and the shortcut-capture custom controls
(`Controls/ShortcutCaptureBox.cs`, `ShortcutChip.cs`, `ShortcutPresenter.cs`,
plus their `Themes/Generic.xaml` templates) — is **deferred to Phase 2b**,
detailed in the scope note at the end of this section, because porting them
requires real design decisions this plan has not made, not just translation.

Note on file locations, corrected from an earlier assumption in this
document: `SpatialNavigationListView.cs` and `WindowThumbnail.cs` live in
`WinTabberUI/Controls/` (the app project), not `WinTabber.UI.Common`. They
are in scope for the window-conversion phases (3–4), not Phase 2.

### Task 2.1: Port `ValueConverters.cs`

**Files:**
- Create: `winui3/WinTabber.UI.Common/ValueConverters/ValueConverters.cs`
- Create: `winui3/WinTabber.UI.Common.Tests/ValueConverters/ValueConvertersTests.cs`
- Delete: `winui3/WinTabber.UI.Common.Tests/PlaceholderTests.cs` (added in the final-review fix wave only to keep this project's test count above zero for CI — this task's real tests supersede it)

**Interfaces:**
- Produces: `WinTabber.UI.Common.ValueConverters.{BoolToThicknessConverter, BoolToBrushConverter, InverseBoolConverter, BoolToVisibilityConverter, InverseBoolToVisibilityConverter, WindowStateToVisibilityConverter, NullToVisibilityConverter, EnumToStringConverter, StringToEnumConverter, EnumValuesConverter, TimeSpanToFloatConverter, TimeSpanToStringConverter, FloatToPercentageConverter, BoolToContentConverter}`, each `: Microsoft.UI.Xaml.Data.IValueConverter`. Same type and member names as the WPF originals; `IconKeyToFontIconDataConverter` is NOT ported here — its WinUI mapping is built in Phase 6 (Global Constraints), not now.

The WinUI 3 `IValueConverter` signature differs from WPF's in one place: the
last parameter is `string language` (a language tag), not
`System.Globalization.CultureInfo culture` — none of these converters use
that parameter, so it is a rename with no logic change.

`BoolToBrushConverter`'s `EditBrush`/`ViewBrush` properties default to
`Brushes.White`/`Brushes.Transparent` in WPF. WinUI 3 has no static `Brushes`
helper class — construct `new SolidColorBrush(Colors.White)` /
`new SolidColorBrush(Colors.Transparent)` instead. `WindowStateToVisibilityConverter`'s
`TargetState`/`WindowState` type needs `Microsoft.UI.Xaml.WindowState` (the
`Normal`/`Minimized`/`Maximized` enum WinAppSDK added for windowing) — before
writing Step 3, confirm this type and its member names against the actual
installed `Microsoft.WindowsAppSDK` package (via IntelliSense or the SDK's
reference docs), since this plan was written without access to browse that
package's API surface directly; if it does not exist or is named
differently, use whatever the installed package's real windowing-state type
is instead, and note the substitution in the commit message.

- [ ] **Step 1: Write failing tests for the converters that have real branching logic**

Not every converter needs a test — several are one-line pass-throughs
(`InverseBoolConverter`, `BoolToVisibilityConverter`) where a test would just
restate the implementation. Cover the ones with a decision or a format:

```csharp
// winui3/WinTabber.UI.Common.Tests/ValueConverters/ValueConvertersTests.cs
using Microsoft.UI.Xaml;
using WinTabber.UI.Common.ValueConverters;

namespace WinTabber.UI.Common.Tests.ValueConverters;

public class ValueConvertersTests
{
    [Test]
    public async Task BoolToThicknessConverter_True_ReturnsOneUnitThickness()
    {
        var converter = new BoolToThicknessConverter();

        var result = (Thickness)converter.Convert(true, typeof(Thickness), null!, "en-US");

        await Assert.That(result.Left).IsEqualTo(1d);
        await Assert.That(result.Top).IsEqualTo(1d);
    }

    [Test]
    public async Task BoolToThicknessConverter_False_ReturnsZeroThickness()
    {
        var converter = new BoolToThicknessConverter();

        var result = (Thickness)converter.Convert(false, typeof(Thickness), null!, "en-US");

        await Assert.That(result.Left).IsEqualTo(0d);
    }

    [Test]
    public async Task TimeSpanToFloatConverter_RoundTrips()
    {
        var converter = new TimeSpanToFloatConverter();
        var span = TimeSpan.FromSeconds(90);

        var seconds = converter.Convert(span, typeof(double), null!, "en-US");
        var back = converter.ConvertBack(seconds, typeof(TimeSpan), null!, "en-US");

        await Assert.That(seconds).IsEqualTo(90d);
        await Assert.That(back).IsEqualTo(span);
    }

    [Test]
    public async Task TimeSpanToStringConverter_UnderOneHour_FormatsAsMinutesSeconds()
    {
        var converter = new TimeSpanToStringConverter();

        var result = converter.Convert(TimeSpan.FromSeconds(65), typeof(string), null!, "en-US");

        await Assert.That(result).IsEqualTo("01:05");
    }

    [Test]
    public async Task FloatToPercentageConverter_ConvertsFractionToPercent()
    {
        var converter = new FloatToPercentageConverter();

        var result = converter.Convert(0.5f, typeof(double), null!, "en-US");

        await Assert.That(result).IsEqualTo(50f);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test winui3/WinTabber.UI.Common.Tests -- --treenode-filter "/*/*/ValueConvertersTests/*"`
Expected: FAIL to compile — the converters don't exist in this project yet.

- [ ] **Step 3: Port every converter**

```csharp
// winui3/WinTabber.UI.Common/ValueConverters/ValueConverters.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace WinTabber.UI.Common.ValueConverters;

public class BoolToThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => (bool)value ? new Thickness(1) : new Thickness(0);

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public class BoolToBrushConverter : IValueConverter
{
    public Brush EditBrush { get; set; } = new SolidColorBrush(Colors.White);
    public Brush ViewBrush { get; set; } = new SolidColorBrush(Colors.Transparent);

    public object Convert(object value, Type targetType, object parameter, string language)
        => (bool)value ? EditBrush : ViewBrush;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => !(bool)value;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => !(bool)value;
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => (bool)value ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => (Visibility)value == Visibility.Visible;
}

public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => (bool)value ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => (Visibility)value != Visibility.Visible;
}

public class WindowStateToVisibilityConverter : IValueConverter
{
    // TODO(verify): confirm Microsoft.UI.Xaml.WindowState is the correct type for the installed
    // Microsoft.WindowsAppSDK version before this compiles — see Task 2.1's Interfaces note.
    public WindowState TargetState { get; set; } = WindowState.Normal;

    public object Convert(object value, Type targetType, object parameter, string language)
        => (WindowState)value == TargetState ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => (Visibility)value == Visibility.Visible ? TargetState : WindowState.Normal;
}

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value == null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public class EnumToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value?.ToString() ?? (object)0;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => Enum.Parse(targetType, (string)value);
}

public class StringToEnumConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => Enum.Parse(targetType, (string)value);

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public class EnumValuesConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => Enum.GetNames(value.GetType());

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public class TimeSpanToFloatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => ((TimeSpan)value).TotalSeconds;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => TimeSpan.FromSeconds((double)value);
}

public class TimeSpanToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var ts = (TimeSpan)value;
        if (ts.TotalHours > 1)
        {
            return ts.ToString(@"dd\:hh\:mm\:ss");
        }
        if (ts.TotalHours > 1)
        {
            return ts.ToString(@"hh\:mm\:ss");
        }

        return ts.ToString(@"mm\:ss");
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}

public class FloatToPercentageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => (float)value * 100;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => (double)value / 100;
}

public class BoolToContentConverter : IValueConverter
{
    public required FrameworkElement TrueContent { get; set; }
    public required FrameworkElement FalseContent { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
        => (bool)value ? TrueContent : FalseContent;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
```

The `TimeSpanToStringConverter.Convert` body above preserves the original's
duplicated `ts.TotalHours > 1` condition verbatim (the second branch is
unreachable dead code in the WPF original — this is a pre-existing bug, not
introduced by porting). Do not fix it in this task; port faithfully and note
it as a follow-up, consistent with how this plan has handled other
pre-existing issues found during migration rather than silently fixing scope
outside the current task.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test winui3/WinTabber.UI.Common.Tests -- --treenode-filter "/*/*/ValueConvertersTests/*"`
Expected: PASS

- [ ] **Step 5: Build the whole solution**

Run: `dotnet build WinTabber.slnx`
Expected: builds clean. If `WindowState`/`Colors` do not resolve, apply the substitution noted in Step 3's comment and re-build.

- [ ] **Step 6: Remove the now-redundant placeholder test**

```bash
git rm winui3/WinTabber.UI.Common.Tests/PlaceholderTests.cs
dotnet test winui3/WinTabber.UI.Common.Tests
```

Expected: still passes — the real `ValueConvertersTests` from this task keep the project's test count above zero.

- [ ] **Step 7: Commit**

```bash
git add winui3/WinTabber.UI.Common/ValueConverters/ValueConverters.cs \
  winui3/WinTabber.UI.Common.Tests/ValueConverters/ValueConvertersTests.cs \
  winui3/WinTabber.UI.Common.Tests/PlaceholderTests.cs
git commit -m "feat: port WinTabber.UI.Common's ValueConverters to WinUI3

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

### Task 2.2: Port the window-state commands (dead code, ported for parity)

**Files:**
- Create: `winui3/WinTabber.UI.Common/Commands/WindowCommands.cs`
- Create: `winui3/WinTabber.UI.Common/Commands/MinimizeCommand.cs`
- Create: `winui3/WinTabber.UI.Common/Commands/RestoreMaximizeCommand.cs`

**Interfaces:**
- Produces: `WinTabber.UI.Common.Commands.WindowCommands` (static, `Maximize`/`Minimize` properties), `MinimizeCommand : System.Windows.Input.ICommand`, `RestoreMaximizeCommand : System.Windows.Input.ICommand`.

A repo-wide search (`grep -rn "WindowCommands\.\|MinimizeCommand\|RestoreMaximizeCommand"`)
found these three classes are not referenced anywhere else in the WPF app —
the same situation Task 0.2 found with `WindowRenameViewModel`. Port them
faithfully for structural parity with the source project (this phase's job
is porting `WinTabber.UI.Common`, not pruning it), but do not expect to
exercise them through any real UI path; there is none today.

`System.Windows.Input.ICommand` is a BCL interface (not a WPF-specific
type — it lives in `System.ObjectModel`/`System.Windows` contracts shared
across UI frameworks), so it is unchanged. The only real port work is the
`Window`/`WindowState` parameter type each command's `Execute` inspects.

- [ ] **Step 1: Port `WindowCommands`, `MinimizeCommand`, `RestoreMaximizeCommand`**

```csharp
// winui3/WinTabber.UI.Common/Commands/WindowCommands.cs
using System.Windows.Input;

namespace WinTabber.UI.Common.Commands;

public static class WindowCommands
{
    public static ICommand Maximize { get; } = new RestoreMaximizeCommand();
    public static ICommand Minimize { get; } = new MinimizeCommand();
}
```

```csharp
// winui3/WinTabber.UI.Common/Commands/MinimizeCommand.cs
using System.Windows.Input;
using Microsoft.UI.Xaml;

namespace WinTabber.UI.Common.Commands;

public class MinimizeCommand : ICommand
{
    // WPF's CommandManager.RequerySuggested has no WinUI3 equivalent; these commands' CanExecute
    // is always true and never changes, so the event is a legal no-op add/remove rather than a
    // real subscription. If a future caller needs CanExecute to actually vary, raise this event
    // manually from Execute or a property setter instead of reaching for RequerySuggested.
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter)
    {
        // TODO(verify): confirm WindowEx (WinUIEx) exposes a settable WindowState property with
        // this name/enum before this compiles — see Task 2.1's Interfaces note on WindowState.
        if (parameter is Window window)
        {
            window.WindowState = WindowState.Minimized;
        }
    }
}
```

```csharp
// winui3/WinTabber.UI.Common/Commands/RestoreMaximizeCommand.cs
using System.Windows.Input;
using Microsoft.UI.Xaml;

namespace WinTabber.UI.Common.Commands;

public class RestoreMaximizeCommand : ICommand
{
    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter)
    {
        if (parameter is Window window)
        {
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build WinTabber.slnx`
Expected: builds clean once the `WindowState` question from Task 2.1 is resolved (both tasks share the same open verification point — resolve it once, apply the same answer to both).

- [ ] **Step 3: Commit**

```bash
git add winui3/WinTabber.UI.Common/Commands
git commit -m "feat: port WinTabber.UI.Common's window-state commands to WinUI3

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

### Task 2.3: Register converters as WinUI3 resources, mirroring `ValueConvertersResources.xaml`

**Files:**
- Create: `winui3/WinTabber.UI.Common/Resources/ValueConvertersResources.xaml`
- Create: `winui3/WinTabber.UI.Common/Resources/ValueConvertersResources.xaml.cs` (WinUI 3 XAML resource dictionaries need a code-behind partial class the way WPF's do not strictly require one for a bare `ResourceDictionary`, but WinUI 3's XAML compiler generates one regardless — an empty partial class matching the file is the minimal, correct shape)

**Interfaces:**
- Produces: a mergeable `ResourceDictionary` exposing the same keys as the WPF file, minus `IconKeyToFontIconDataConverter` (Phase 6, per Global Constraints).

- [ ] **Step 1: Create the resource dictionary**

```xml
<!-- winui3/WinTabber.UI.Common/Resources/ValueConvertersResources.xaml -->
<ResourceDictionary
    x:Class="WinTabber.UI.Common.Resources.ValueConvertersResources"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:c="using:WinTabber.UI.Common.ValueConverters"
>
    <c:BoolToThicknessConverter x:Key="BoolToThicknessConverter" />
    <c:BoolToBrushConverter x:Key="BoolToBrushConverter" />
    <c:BoolToVisibilityConverter x:Key="BoolToVisibilityConverter" />
    <c:InverseBoolConverter x:Key="InverseBoolConverter" />
    <c:NullToVisibilityConverter x:Key="NullToVisibilityConverter" />
    <c:TimeSpanToFloatConverter x:Key="TimeSpanToFloatConverter" />
    <c:TimeSpanToStringConverter x:Key="TimeSpanToStringConverter" />
    <c:FloatToPercentageConverter x:Key="FloatToPercentageConverter" />
</ResourceDictionary>
```

```csharp
// winui3/WinTabber.UI.Common/Resources/ValueConvertersResources.xaml.cs
namespace WinTabber.UI.Common.Resources;

public sealed partial class ValueConvertersResources
{
    public ValueConvertersResources()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build WinTabber.slnx`
Expected: builds clean.

- [ ] **Step 3: Commit**

```bash
git add winui3/WinTabber.UI.Common/Resources
git commit -m "feat: add winui3 ValueConvertersResources resource dictionary

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Phase 2b — Shortcut-capture custom controls

`WinTabber.UI.Common/Controls/ShortcutCaptureBox.cs`, `ShortcutChip.cs`,
`ShortcutPresenter.cs`, and `WinTabber.UI.Common/Themes/Generic.xaml` were
read in full before writing this phase. `WinTabber.Events/Shortcuts/ShortcutDisplayNames.cs`
was also read in full to resolve the open `KeyInterop` fallback question:
`ShortcutChips.GetDisplayName` only reaches the WPF-side `KeyInterop.KeyFromVirtualKey`
fallback when `ShortcutDisplayNames.GetCanonicalName` returns null — i.e. for
virtual keys entirely outside the canonical table (letters, digits, F-keys,
NumPad, navigation, and the common OEM punctuation keys are all in that
table; media keys, browser keys, and volume keys are not). The fallback is
**not droppable** — a user of this global-hotkey app can plausibly capture a
media/volume/browser key, and without a name fallback those would render as
a bare `0x** hex code instead of a readable name. It also is not a
mechanical port: WinRT's `Windows.System.VirtualKey` enum does not
necessarily have a member for every virtual-key code WPF's `System.Windows.Input.Key`
covers (verify this against the actual enum before Task 2b.1's Step 3
compiles, per that task's note) — where no `VirtualKey` member exists, the
code must fall through to `ShortcutDisplayNames.GetDisplayName`'s hex format,
exactly as the WPF original falls through when `KeyInterop` throws or
returns `Key.None`.

No existing test in `WinTabber.UI.Common.Tests` covers these three controls
(confirmed by search) — Phase 2b's tests are new coverage, not ports of
existing coverage.

### Task 2b.1: Port `ShortcutChip` and `ShortcutChips`

**Files:**
- Create: `winui3/WinTabber.UI.Common/Controls/ShortcutChip.cs`
- Create: `winui3/WinTabber.UI.Common.Tests/Controls/ShortcutChipsTests.cs`

**Interfaces:**
- Produces: `WinTabber.UI.Common.Controls.{ChipKind, ShortcutChip, ShortcutChips}`. `ShortcutChip` stays a plain `record` (no WPF/WinUI type in its own definition) — same as the WPF original. `ShortcutChips.GetDisplayName(ShortcutKey)`, `Build(ShortcutTrigger?, bool)`, and `BuildInProgress(ShortcutModifiers)` keep the same signatures.

- [ ] **Step 1: Write failing tests for the parts of `ShortcutChips` that don't depend on the unresolved `VirtualKey` fallback**

```csharp
// winui3/WinTabber.UI.Common.Tests/Controls/ShortcutChipsTests.cs
using WinTabber.Events.Shortcuts;
using WinTabber.UI.Common.Controls;

namespace WinTabber.UI.Common.Tests.Controls;

public class ShortcutChipsTests
{
    [Test]
    public async Task Build_NullTrigger_ReturnsEmpty()
    {
        var chips = ShortcutChips.Build(null, showEdgeHint: true);

        await Assert.That(chips).IsEmpty();
    }

    [Test]
    public async Task Build_KeyboardTrigger_ProducesModifierAndKeyChips()
    {
        var trigger = new ShortcutTrigger.Keyboard
        {
            Modifiers = ShortcutModifiers.Ctrl | ShortcutModifiers.Alt,
            Key = new ShortcutKey(VirtualKeys.Delete),
        };

        var chips = ShortcutChips.Build(trigger, showEdgeHint: true);

        await Assert.That(chips.Select(c => c.Text)).IsEquivalentTo(["Ctrl", "Alt", "Delete"]);
        await Assert.That(chips[^1].Kind).IsEqualTo(ChipKind.Key);
    }

    [Test]
    public async Task Build_ReleaseEdgeWithHint_AppendsHintChip()
    {
        var trigger = new ShortcutTrigger.Keyboard
        {
            Modifiers = ShortcutModifiers.None,
            Key = new ShortcutKey(VirtualKeys.Delete),
            Edge = TriggerEdge.Release,
        };

        var chips = ShortcutChips.Build(trigger, showEdgeHint: true);

        await Assert.That(chips[^1]).IsEqualTo(new ShortcutChip("release", ChipKind.Hint));
    }

    [Test]
    public async Task BuildInProgress_ReturnsOnlyModifierChipsInCanonicalOrder()
    {
        var chips = ShortcutChips.BuildInProgress(ShortcutModifiers.Win | ShortcutModifiers.Ctrl);

        await Assert.That(chips.Select(c => c.Text)).IsEquivalentTo(["Ctrl", "Win"]);
    }

    [Test]
    public async Task GetDisplayName_KeyInCanonicalTable_UsesCanonicalDisplayName()
    {
        // VirtualKeys.Delete is in ShortcutDisplayNames' canonical table, so this must never reach
        // the VirtualKey fallback — pins the "canonical table wins" branch independent of whatever
        // Task 2b.1 Step 3 resolves for the fallback branch.
        var name = ShortcutChips.GetDisplayName(new ShortcutKey(VirtualKeys.Delete));

        await Assert.That(name).IsEqualTo(ShortcutDisplayNames.GetDisplayName(new ShortcutKey(VirtualKeys.Delete)));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test winui3/WinTabber.UI.Common.Tests -- --treenode-filter "/*/*/ShortcutChipsTests/*"`
Expected: FAIL to compile — `ShortcutChip`/`ShortcutChips` don't exist in this project yet.

- [ ] **Step 3: Port `ShortcutChip.cs`**

```csharp
// winui3/WinTabber.UI.Common/Controls/ShortcutChip.cs
using WinTabber.Events.Shortcuts;

namespace WinTabber.UI.Common.Controls;

public enum ChipKind
{
    Modifier,
    Key,
    Mouse,
    Hint,
}

public sealed record ShortcutChip(string Text, ChipKind Kind);

/// <summary>
/// The WinUI3 half of the display-name story. <see cref="ShortcutDisplayNames" /> covers every key
/// the app can bind and stays UI-framework-free so the model is referenceable from non-UI
/// assemblies; this adds a WinRT <c>VirtualKey</c> fallback for exotic keys outside that table
/// (media, volume, browser keys), and turns a trigger into the chip list the presenter renders.
/// </summary>
public static class ShortcutChips
{
    public static string GetDisplayName(ShortcutKey key)
    {
        if (ShortcutDisplayNames.GetCanonicalName(key) is not null)
        {
            return ShortcutDisplayNames.GetDisplayName(key);
        }

        // Outside the canonical table — ask WinRT what it thinks this virtual key is.
        // TODO(verify): confirm Windows.System.VirtualKey actually defines a member for the raw
        // value before this compiles for a given key; Enum.IsDefined below already guards against
        // an undefined member falling through silently, so this is safe either way, but the set of
        // keys this recovers a friendly name for depends on VirtualKey's actual member list, which
        // was not enumerated while writing this plan (see Phase 2b's scope note).
        var vk = (Windows.System.VirtualKey)key.VirtualKey;
        if (Enum.IsDefined(vk) && vk != Windows.System.VirtualKey.None)
        {
            return vk.ToString();
        }

        return ShortcutDisplayNames.GetDisplayName(key);
    }

    /// <summary>
    /// Chips for a trigger, with modifiers always in the canonical Ctrl, Alt, Shift, Win order
    /// regardless of the order the user pressed them.
    /// </summary>
    public static IReadOnlyList<ShortcutChip> Build(ShortcutTrigger? trigger, bool showEdgeHint)
    {
        if (trigger is null)
        {
            return [];
        }

        var chips = ShortcutDisplayNames
            .Split(trigger.Modifiers)
            .Select(m => new ShortcutChip(ShortcutDisplayNames.GetDisplayName(m), ChipKind.Modifier))
            .ToList();

        switch (trigger)
        {
            case ShortcutTrigger.Keyboard keyboard:
                if (!keyboard.Key.IsNone)
                {
                    chips.Add(new ShortcutChip(GetDisplayName(keyboard.Key), ChipKind.Key));
                }

                if (showEdgeHint && keyboard.Edge == TriggerEdge.Release)
                {
                    chips.Add(new ShortcutChip("release", ChipKind.Hint));
                }
                break;

            case ShortcutTrigger.KeyMouse mouse:
                chips.Add(new ShortcutChip(ShortcutDisplayNames.GetDisplayName(mouse.Button), ChipKind.Mouse));
                break;
        }

        return chips;
    }

    /// <summary>Chips for an in-progress capture: modifiers only, nothing committed yet.</summary>
    public static IReadOnlyList<ShortcutChip> BuildInProgress(ShortcutModifiers modifiers) =>
        ShortcutDisplayNames
            .Split(modifiers)
            .Select(m => new ShortcutChip(ShortcutDisplayNames.GetDisplayName(m), ChipKind.Modifier))
            .ToList();
}
```

The port is otherwise byte-for-byte identical to the WPF original (no `using System.Windows.Input;`, since that is what carried the `KeyInterop` dependency) — only `GetDisplayName`'s fallback body changed.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test winui3/WinTabber.UI.Common.Tests -- --treenode-filter "/*/*/ShortcutChipsTests/*"`
Expected: PASS

- [ ] **Step 5: Build the whole solution**

Run: `dotnet build WinTabber.slnx`
Expected: builds clean.

- [ ] **Step 6: Commit**

```bash
git add winui3/WinTabber.UI.Common/Controls/ShortcutChip.cs winui3/WinTabber.UI.Common.Tests/Controls/ShortcutChipsTests.cs
git commit -m "feat: port ShortcutChip/ShortcutChips to WinUI3

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

### Task 2b.2: Port `ShortcutPresenter`

**Files:**
- Create: `winui3/WinTabber.UI.Common/Controls/ShortcutPresenter.cs`

**Interfaces:**
- Produces: `WinTabber.UI.Common.Controls.ShortcutPresenter : Microsoft.UI.Xaml.Controls.Control`, with `Trigger` (`ShortcutTrigger?`), `Orientation` (`Microsoft.UI.Xaml.Controls.Orientation`), `ShowEdgeHint` (`bool`), `Chips` (`IReadOnlyList<ShortcutChip>`, read-only), `IsEmpty` (`bool`, read-only), `EmptyText` (`string`) — same names as the WPF original.

Consumes: `ShortcutChips.Build` from Task 2b.1.

WinUI 3's `DependencyProperty` API is the same shape as WPF's (`Register`/`RegisterAttached`/`RegisterReadOnly`, `GetValue`/`SetValue`), just under `Microsoft.UI.Xaml` instead of `System.Windows`, so this is close to a namespace-only port. Two real differences: WinUI 3 has no `[TemplatePart]`-adjacent `DefaultStyleKeyProperty.OverrideMetadata` call needed for controls with no code-generated default style — instead, `DefaultStyleKey = typeof(ShortcutPresenter);` is set directly in the constructor (WinUI 3 convention for custom controls, since there is no static-constructor metadata-override pattern for this in WinAppSDK). `Orientation` moves from `System.Windows.Controls.Orientation` to `Microsoft.UI.Xaml.Controls.Orientation` (same member names: `Horizontal`/`Vertical`).

- [ ] **Step 1: Port `ShortcutPresenter.cs`**

```csharp
// winui3/WinTabber.UI.Common/Controls/ShortcutPresenter.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinTabber.Events.Shortcuts;

namespace WinTabber.UI.Common.Controls;

/// <summary>
/// Read-only renderer for a <see cref="ShortcutTrigger" />. Chip rendering lives here and nowhere
/// else — <see cref="ShortcutCaptureBox" /> hosts this control rather than duplicating it.
/// </summary>
public class ShortcutPresenter : Control
{
    public ShortcutPresenter()
    {
        DefaultStyleKey = typeof(ShortcutPresenter);
    }

    public static readonly DependencyProperty TriggerProperty = DependencyProperty.Register(
        nameof(Trigger),
        typeof(ShortcutTrigger),
        typeof(ShortcutPresenter),
        new PropertyMetadata(null, OnVisualInputChanged)
    );

    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation),
        typeof(Orientation),
        typeof(ShortcutPresenter),
        new PropertyMetadata(Orientation.Horizontal)
    );

    public static readonly DependencyProperty ShowEdgeHintProperty = DependencyProperty.Register(
        nameof(ShowEdgeHint),
        typeof(bool),
        typeof(ShortcutPresenter),
        new PropertyMetadata(true, OnVisualInputChanged)
    );

    public static readonly DependencyProperty ChipsProperty = DependencyProperty.Register(
        nameof(Chips),
        typeof(IReadOnlyList<ShortcutChip>),
        typeof(ShortcutPresenter),
        new PropertyMetadata(Array.Empty<ShortcutChip>())
    );

    public static readonly DependencyProperty IsEmptyProperty = DependencyProperty.Register(
        nameof(IsEmpty),
        typeof(bool),
        typeof(ShortcutPresenter),
        new PropertyMetadata(true)
    );

    public static readonly DependencyProperty EmptyTextProperty = DependencyProperty.Register(
        nameof(EmptyText),
        typeof(string),
        typeof(ShortcutPresenter),
        new PropertyMetadata("Not set")
    );

    public ShortcutTrigger? Trigger
    {
        get => (ShortcutTrigger?)GetValue(TriggerProperty);
        set => SetValue(TriggerProperty, value);
    }

    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Renders a trailing "release" chip for a <see cref="TriggerEdge.Release" /> trigger.</summary>
    public bool ShowEdgeHint
    {
        get => (bool)GetValue(ShowEdgeHintProperty);
        set => SetValue(ShowEdgeHintProperty, value);
    }

    public IReadOnlyList<ShortcutChip> Chips => (IReadOnlyList<ShortcutChip>)GetValue(ChipsProperty);

    /// <summary>True when there is nothing to render, so the template can show <see cref="EmptyText" />.</summary>
    public bool IsEmpty => (bool)GetValue(IsEmptyProperty);

    public string EmptyText
    {
        get => (string)GetValue(EmptyTextProperty);
        set => SetValue(EmptyTextProperty, value);
    }

    private static void OnVisualInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((ShortcutPresenter)d).Rebuild();

    private void Rebuild()
    {
        var chips = ShortcutChips.Build(Trigger, ShowEdgeHint);
        SetValue(ChipsProperty, chips);
        SetValue(IsEmptyProperty, chips.Count == 0);
    }
}
```

Note: `Chips`/`IsEmpty` are plain (writable) `DependencyProperty` registrations here, not WPF's read-only `DependencyPropertyKey` pattern — WinUI 3's `DependencyProperty` has no `RegisterReadOnly`/`DependencyPropertyKey` API. External code can technically call `SetValue` on these, same risk profile as any other WinUI 3 control exposing computed state this way; this is a known WinUI 3 limitation, not a mistake in this port.

- [ ] **Step 2: Build**

Run: `dotnet build WinTabber.slnx`
Expected: builds clean.

- [ ] **Step 3: Commit**

```bash
git add winui3/WinTabber.UI.Common/Controls/ShortcutPresenter.cs
git commit -m "feat: port ShortcutPresenter to WinUI3

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

### Task 2b.3: Port `ShortcutCaptureBox`

**Files:**
- Create: `winui3/WinTabber.UI.Common/Controls/ShortcutCaptureBox.cs`

**Interfaces:**
- Produces: `WinTabber.UI.Common.Controls.ShortcutCaptureBox : Microsoft.UI.Xaml.Controls.Control`, same public surface as the WPF original (`Trigger`, `TriggerSource`, `AllowMouseButtons`, `IsCapturing`, `PendingChips`, `ValidationMessage`, `StartCaptureCommand`, `CancelCaptureCommand`, `Captured` event, `StartCapture()`, `CancelCapture()`).

Consumes: `ShortcutChip`/`ShortcutChips` from Task 2b.1, `ShortcutPresenter` from Task 2b.2 (referenced only via the `[TemplatePart]` attribute's type, not directly instantiated in code).

Three real API changes, not renames:
- `System.Windows.Threading.DispatcherTimer` → `Microsoft.UI.Dispatching.DispatcherQueueTimer`. Construction differs: WPF's constructor takes `(interval, priority, callback, dispatcher)`; WinUI 3's `DispatcherQueueTimer` is created via `DispatcherQueue.GetForCurrentThread().CreateTimer()`, then configured with `.Interval` and a `.Tick` event handler, then `.Start()`.
- `System.Windows.Input.Keyboard.Focus(this)` → `this.Focus(Microsoft.UI.Xaml.FocusState.Programmatic)`.
- The `RelayCommand` inner class's `CanExecuteChanged` used WPF's `CommandManager.RequerySuggested`, which has no WinUI 3 equivalent — same situation Task 2.2 already resolved for `MinimizeCommand`/`RestoreMaximizeCommand`: use the same no-op `add {} remove {}` pattern here for consistency (these two commands' `CanExecute` results only actually change when `TriggerSource`/`IsCapturing` change, and nothing in this control re-queries on a timer or external event today, so this matches the WPF original's actual behavior under `RequerySuggested`, which only fires on focus/keyboard/mouse events WPF itself generates — not a guaranteed re-evaluation on every state change either).

The debug-log `File.AppendAllText` calls in `StartCapture`/`CancelCapture`/`OnCapturedInput` are ported unchanged — removing them is out of scope for a structural port, and the original author may still want them for diagnosing the exact focus-timing bug the code comment already documents.

- [ ] **Step 1: Port `ShortcutCaptureBox.cs`**

```csharp
// winui3/WinTabber.UI.Common/Controls/ShortcutCaptureBox.cs
using System.Reactive.Linq;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinTabber.Events.Shortcuts;
using WinTabber.Events.Shortcuts.Detection;

namespace WinTabber.UI.Common.Controls;

/// <summary>
/// Captures a shortcut from live global input.
/// <para>
/// <b>Why not WinUI3 keyboard events:</b> WinUI3 cannot see the Win key reliably and cannot see
/// mouse buttons pressed outside the window, so capture goes through
/// <see cref="IShortcutTriggerSource.BeginCapture" /> (§3.2).
/// </para>
/// <para>
/// <b>The hook is never torn down to enter capture mode.</b> The gate lives inside the trigger
/// source: the hook stays alive, command dispatch is muted, and raw input is both suppressed and
/// forwarded here — so pressing Alt+Tab while capturing doesn't switch windows.
/// </para>
/// <para>
/// <b>CapsLock:</b> <c>HyperKeyState</c> honors the same gate and steps aside while capturing, so
/// CapsLock is captured as CapsLock rather than as its Ctrl+Alt+Shift+Win expansion (§3.4).
/// </para>
/// </summary>
[TemplatePart(Name = PartPresenter, Type = typeof(ShortcutPresenter))]
public class ShortcutCaptureBox : Control
{
    private const string PartPresenter = "PART_Presenter";

    /// <summary>
    /// Chords the OS intercepts before any hook sees them. Accepting one silently would produce a
    /// binding that never fires, so they get an inline message instead (§3.4).
    /// </summary>
    private static readonly (ShortcutModifiers Modifiers, ushort Key, string Name)[] ReservedByWindows =
    [
        (ShortcutModifiers.Win, 0x4C, "Win+L"),
        (ShortcutModifiers.Ctrl | ShortcutModifiers.Alt, VirtualKeys.Delete, "Ctrl+Alt+Del"),
    ];

    /// <summary>Backstop if the user walks away mid-capture (§3.3).</summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(10);

    private IDisposable? _session;
    private IDisposable? _rawSubscription;
    private DispatcherQueueTimer? _idleTimer;
    private ShortcutModifiers _pendingModifiers;

    public ShortcutCaptureBox()
    {
        DefaultStyleKey = typeof(ShortcutCaptureBox);

        StartCaptureCommand = new RelayCommand(_ => StartCapture(), _ => TriggerSource is not null && !IsCapturing);
        CancelCaptureCommand = new RelayCommand(_ => CancelCapture(), _ => IsCapturing);
        Unloaded += (_, _) => CancelCapture();
        LosingFocus += (_, _) => CancelCapture();

        // Nothing else invokes StartCaptureCommand: the host template (see ShortcutsSettingsPage.xaml)
        // just toggles this control's Visibility on when the row enters edit mode, it never fires the
        // command itself. Without this, becoming visible showed the idle presenter with no capture
        // session behind it, so keystrokes went nowhere.
        RegisterPropertyChangedCallback(
            VisibilityProperty,
            (_, _) =>
            {
                if (Visibility == Visibility.Visible)
                {
                    StartCapture();
                }
                else
                {
                    CancelCapture();
                }
            }
        );
    }

    public static readonly DependencyProperty TriggerProperty = DependencyProperty.Register(
        nameof(Trigger),
        typeof(ShortcutTrigger),
        typeof(ShortcutCaptureBox),
        new PropertyMetadata(null)
    );

    public static readonly DependencyProperty TriggerSourceProperty = DependencyProperty.Register(
        nameof(TriggerSource),
        typeof(IShortcutTriggerSource),
        typeof(ShortcutCaptureBox),
        new PropertyMetadata(null)
    );

    public static readonly DependencyProperty AllowMouseButtonsProperty = DependencyProperty.Register(
        nameof(AllowMouseButtons),
        typeof(bool),
        typeof(ShortcutCaptureBox),
        new PropertyMetadata(true)
    );

    public static readonly DependencyProperty IsCapturingProperty = DependencyProperty.Register(
        nameof(IsCapturing),
        typeof(bool),
        typeof(ShortcutCaptureBox),
        new PropertyMetadata(false)
    );

    public static readonly DependencyProperty PendingChipsProperty = DependencyProperty.Register(
        nameof(PendingChips),
        typeof(IReadOnlyList<ShortcutChip>),
        typeof(ShortcutCaptureBox),
        new PropertyMetadata(Array.Empty<ShortcutChip>())
    );

    public static readonly DependencyProperty ValidationMessageProperty = DependencyProperty.Register(
        nameof(ValidationMessage),
        typeof(string),
        typeof(ShortcutCaptureBox),
        new PropertyMetadata(null)
    );

    public ShortcutTrigger? Trigger
    {
        get => (ShortcutTrigger?)GetValue(TriggerProperty);
        set => SetValue(TriggerProperty, value);
    }

    /// <summary>Supplied by the hosting view model; capture is unavailable until this is set.</summary>
    public IShortcutTriggerSource? TriggerSource
    {
        get => (IShortcutTriggerSource?)GetValue(TriggerSourceProperty);
        set => SetValue(TriggerSourceProperty, value);
    }

    public bool AllowMouseButtons
    {
        get => (bool)GetValue(AllowMouseButtonsProperty);
        set => SetValue(AllowMouseButtonsProperty, value);
    }

    public bool IsCapturing => (bool)GetValue(IsCapturingProperty);

    /// <summary>Live modifier chips while capturing, rendered by the same presenter.</summary>
    public IReadOnlyList<ShortcutChip> PendingChips => (IReadOnlyList<ShortcutChip>)GetValue(PendingChipsProperty);

    public string? ValidationMessage => (string?)GetValue(ValidationMessageProperty);

    public ICommand StartCaptureCommand { get; }

    public ICommand CancelCaptureCommand { get; }

    public event EventHandler<ShortcutTrigger>? Captured;

    public void StartCapture()
    {
        if (IsCapturing || TriggerSource is not { } source)
        {
            return;
        }

        _pendingModifiers = ShortcutModifiers.None;
        SetValue(ValidationMessageProperty, null);
        SetValue(PendingChipsProperty, Array.Empty<ShortcutChip>());
        SetValue(IsCapturingProperty, true);

        _session = source.BeginCapture(out var raw);
        _rawSubscription = raw
            .ObserveOn(System.Reactive.Concurrency.SynchronizationContextScheduler.Instance ?? throw new InvalidOperationException("Not on a UI thread."))
            .Subscribe(OnCapturedInput, _ => CancelCapture());

        _idleTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _idleTimer.Interval = IdleTimeout;
        _idleTimer.Tick += (_, _) => CancelCapture();
        _idleTimer.Start();

        Focus(FocusState.Programmatic);
    }

    public void CancelCapture()
    {
        if (!IsCapturing)
        {
            return;
        }

        EndSession();
        SetValue(PendingChipsProperty, Array.Empty<ShortcutChip>());
    }

    private void EndSession()
    {
        _idleTimer?.Stop();
        _idleTimer = null;

        _rawSubscription?.Dispose();
        _rawSubscription = null;

        _session?.Dispose();
        _session = null;

        SetValue(IsCapturingProperty, false);
    }

    private void OnCapturedInput(CapturedInput input)
    {
        // Any activity resets the idle countdown.
        _idleTimer?.Stop();
        _idleTimer?.Start();

        switch (input.Kind)
        {
            case CapturedInputKind.ModifierDown:
                _pendingModifiers |= input.ModifierBit;
                UpdatePendingChips();
                return;

            case CapturedInputKind.ModifierUp:
                // No completion on modifier release — the user may be re-pressing.
                _pendingModifiers &= ~input.ModifierBit;
                UpdatePendingChips();
                return;

            case CapturedInputKind.KeyDown:
                OnKeyCaptured(input);
                return;

            case CapturedInputKind.MouseDown:
                OnMouseCaptured(input);
                return;
        }
    }

    private void OnKeyCaptured(CapturedInput input)
    {
        if (input.Key.VirtualKey == VirtualKeys.Escape && _pendingModifiers == ShortcutModifiers.None)
        {
            CancelCapture();
            return;
        }

        if (input.Key.VirtualKey == VirtualKeys.Back && _pendingModifiers != ShortcutModifiers.None)
        {
            _pendingModifiers = ShortcutModifiers.None;
            UpdatePendingChips();
            return;
        }

        if (input.Key.IsModifier)
        {
            // A modifier key that the mask did not classify; treat as a modifier, not a completion.
            return;
        }

        if (FindReserved(_pendingModifiers, input.Key.VirtualKey) is { } reserved)
        {
            SetValue(ValidationMessageProperty, $"{reserved} is reserved by Windows and cannot be captured.");
            return;
        }

        Complete(new ShortcutTrigger.Keyboard { Modifiers = _pendingModifiers, Key = input.Key });
    }

    private void OnMouseCaptured(CapturedInput input)
    {
        if (!AllowMouseButtons)
        {
            return;
        }

        if (_pendingModifiers == ShortcutModifiers.None)
        {
            // Binding a bare mouse button would swallow ordinary clicking.
            SetValue(
                ValidationMessageProperty,
                "A mouse shortcut needs at least one modifier. Hold Ctrl, Alt, Shift or Win first."
            );
            return;
        }

        Complete(new ShortcutTrigger.KeyMouse { Modifiers = _pendingModifiers, Button = input.Button });
    }

    private void Complete(ShortcutTrigger trigger)
    {
        EndSession();
        SetValue(PendingChipsProperty, Array.Empty<ShortcutChip>());
        SetValue(ValidationMessageProperty, null);

        Trigger = trigger;
        Captured?.Invoke(this, trigger);
    }

    private void UpdatePendingChips() =>
        SetValue(PendingChipsProperty, ShortcutChips.BuildInProgress(_pendingModifiers));

    private static string? FindReserved(ShortcutModifiers modifiers, ushort key)
    {
        foreach (var (reservedModifiers, reservedKey, name) in ReservedByWindows)
        {
            if (reservedKey == key && modifiers == reservedModifiers)
            {
                return name;
            }
        }

        return null;
    }

    private sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;

        public void Execute(object? parameter) => execute(parameter);
    }
}
```

Three notes on the port above, beyond the three API changes already called out:
1. WPF's `IsVisibleChanged` has no WinUI 3 equivalent event with that exact name; `RegisterPropertyChangedCallback(VisibilityProperty, ...)` is the WinUI 3 mechanism for observing a dependency property change without a dedicated CLR event, and `Visibility` is the closest WinUI 3 analog to WPF's computed `IsVisible` for this control's purpose (the host template toggles this control's `Visibility`, not some ancestor's, per the existing code comment).
2. WPF's `LostKeyboardFocus` becomes WinUI 3's `LosingFocus` (fires before focus moves, closest bubbling equivalent; WinUI 3 also has `LostFocus`, which fires after — `LosingFocus` was chosen to cancel before the next control gains focus, matching the WPF original's intent of canceling as focus leaves). **TODO(verify):** confirm `LosingFocus` exists on `Control` in the installed WinAppSDK version and fires in the scenario this control needs (tabbing/clicking away while capturing) before relying on it — if it does not behave as expected, `LostFocus` is the fallback.
3. **TODO(verify):** the raw-input observable's `ObserveOn` scheduler in the WPF original was `Dispatcher` (a `DispatcherScheduler`). WinUI 3 has no built-in `DispatcherQueueScheduler` in `System.Reactive` — confirm whether `WinTabber.Events`' `IShortcutTriggerSource.BeginCapture`'s raw observable already marshals onto some UI-safe context, or whether a custom `IScheduler` wrapping `DispatcherQueue.TryEnqueue` is needed here instead of the placeholder `SynchronizationContextScheduler.Instance` reference above (which will not compile as written — `SynchronizationContextScheduler` requires an explicit `SynchronizationContext` instance, not a static `.Instance`). This is the one piece of this task that could not be fully resolved without either running the actual WinUI 3 app to observe `SynchronizationContext.Current` inside a `Loaded` handler, or reading `WinTabber.Events`' capture-source implementation in full (out of scope for this pass — flagged rather than guessed). Resolve this before Step 2's build succeeds; a plausible correct fix is `raw.ObserveOn(new SynchronizationContextScheduler(SynchronizationContext.Current!))` captured once in the constructor while the control is guaranteed to be on the UI thread, or `DispatcherQueue.GetForCurrentThread()`-based marshaling inside `OnCapturedInput` itself instead of via `ObserveOn`.

- [ ] **Step 2: Build, resolving the two TODO(verify) items above against the actual compiler output**

Run: `dotnet build WinTabber.slnx`
Expected: does not build clean on the first attempt — the `ObserveOn` line above is deliberately left unresolved (see note 3). Fix it using the real compiler error and the actual shape of `IShortcutTriggerSource.BeginCapture`'s raw observable (read `WinTabber.Events/Shortcuts/Detection/IShortcutTriggerSource.cs` at this point, since this is now necessary to complete this specific step correctly), then re-build until clean.

- [ ] **Step 3: Commit**

```bash
git add winui3/WinTabber.UI.Common/Controls/ShortcutCaptureBox.cs
git commit -m "feat: port ShortcutCaptureBox to WinUI3

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

### Task 2b.4: Port `Themes/Generic.xaml` control templates

**Files:**
- Create: `winui3/WinTabber.UI.Common/Themes/Generic.xaml`

**Interfaces:**
- Produces: default styles for `ShortcutPresenter` and `ShortcutCaptureBox` (keyed `ShortcutPresenterLargeStyle`/`ShortcutCaptureBoxDialogStyle` plus the two implicit `TargetType`-only styles), matching the WPF original's four styles and two shared `DataTemplate`s.

This is the one piece of Phase 2b that is not a mechanical rename: the WPF original uses `Style.Triggers`/`ControlTemplate.Triggers`/`DataTrigger`, none of which exist in WinUI 3 (migration skill's XAML prohibitions). Every trigger becomes a `VisualStateManager` state, and the `DataTrigger Binding="{Binding Kind}" Value="Hint"` pattern (used to restyle a chip when its `Kind` is `Hint`) becomes an `x:Bind`-driven `Visibility`/property split via a converter, since WinUI 3's `VisualStateManager` operates on the control's own states, not a bound data value inside a `DataTemplate`.

- [ ] **Step 1: Port the two chip `DataTemplate`s, replacing the `Kind == Hint` `DataTrigger` with a converter**

Add a new converter (not in Task 2.1's list, since it is specific to this control): `winui3/WinTabber.UI.Common/ValueConverters/ChipKindToBoolConverter.cs`:

```csharp
// winui3/WinTabber.UI.Common/ValueConverters/ChipKindToBoolConverter.cs
using Microsoft.UI.Xaml.Data;
using WinTabber.UI.Common.Controls;

namespace WinTabber.UI.Common.ValueConverters;

/// <summary>True when the bound <see cref="ChipKind"/> is <see cref="ChipKind.Hint"/> — drives the
/// two `DataTemplate`s in Generic.xaml that render a hint chip differently from a normal one,
/// replacing the WPF original's `DataTrigger Binding="{Binding Kind}" Value="Hint"`.</summary>
public sealed class ChipKindToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is ChipKind.Hint;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
```

```xml
<!-- winui3/WinTabber.UI.Common/Themes/Generic.xaml -->
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:controls="using:WinTabber.UI.Common.Controls"
    xmlns:conv="using:WinTabber.UI.Common.ValueConverters"
>
    <conv:ChipKindToBoolConverter x:Key="ChipKindToBoolConverter" />

    <SolidColorBrush x:Key="ShortcutChipForegroundBrush" Color="#202020" />
    <SolidColorBrush x:Key="ShortcutCaptureBoxBorderBrush" Color="#6E7683" />
    <SolidColorBrush x:Key="ShortcutHintForegroundBrush" Color="#99FFFFFF" />
    <SolidColorBrush x:Key="ShortcutEmptyForegroundBrush" Color="#88808080" />
    <SolidColorBrush x:Key="ShortcutValidationForegroundBrush" Color="#FFC42B1C" />
    <SolidColorBrush x:Key="ShortcutGroupBackgroundBrush" Color="#3A3A3A" />
    <SolidColorBrush x:Key="ShortcutGroupHoverBackgroundBrush" Color="#454545" />
    <SolidColorBrush x:Key="ShortcutGroupBorderBrush" Color="#55FFFFFF" />
    <SolidColorBrush x:Key="ShortcutChipAccentBrush" Color="{ThemeResource SystemAccentColorLight2}" />

    <!-- Chip rendering exists in exactly one place. ShortcutCaptureBox hosts a ShortcutPresenter
         rather than duplicating this template. The Hint variant (transparent, italic, softer
         foreground) is selected via ChipKindToBoolConverter instead of WPF's DataTrigger, since
         WinUI 3 DataTemplates cannot host a Style with a data-bound trigger condition the way WPF's
         DataTrigger could. -->
    <DataTemplate x:Key="ShortcutChipTemplate">
        <Border
            Margin="0,0,6,0"
            Padding="10,5"
            BorderThickness="1"
            CornerRadius="6"
            Background="{Binding Kind, Converter={StaticResource ChipKindToBoolConverter}, ConverterParameter=Invert, FallbackValue={StaticResource ShortcutChipAccentBrush}}"
            BorderBrush="{StaticResource ShortcutChipAccentBrush}">
            <TextBlock
                FontSize="13"
                FontWeight="Bold"
                Foreground="{StaticResource ShortcutChipForegroundBrush}"
                Text="{Binding Text}" />
        </Border>
    </DataTemplate>

    <!-- Large-scale chip, shared by the capture dialog's idle and in-progress display. -->
    <DataTemplate x:Key="ShortcutChipTemplateLarge">
        <Border
            MinWidth="50"
            MinHeight="50"
            Margin="5,0,5,0"
            Padding="8"
            BorderThickness="1"
            CornerRadius="6"
            Background="{StaticResource ShortcutChipAccentBrush}"
            BorderBrush="{StaticResource ShortcutChipAccentBrush}">
            <TextBlock
                HorizontalAlignment="Center"
                VerticalAlignment="Center"
                FontSize="18"
                FontWeight="Bold"
                Foreground="{StaticResource ShortcutChipForegroundBrush}"
                Text="{Binding Text}" />
        </Border>
    </DataTemplate>
</ResourceDictionary>
```

**Open item, not resolved by this task:** the `ChipKindToBoolConverter`/`ConverterParameter=Invert` binding above for the Hint-variant background/border/foreground swap is sketched but not fully worked out — a single boolean converter cannot drive three different property swaps (background, border, foreground, font-style, font-weight) cleanly the way the WPF `Style.Triggers` block did across two nested `Style` elements. The faithful WinUI 3 equivalent is either (a) two `DataTemplate`s selected by a `DataTemplateSelector` keyed on `Kind == Hint`, or (b) a single `DataTemplate` whose visual states are driven by `VisualStateManager.GoToState` from code-behind on the generated container, set via `ItemsControl.ContainerContentChanging`. Given the complexity this adds and that it affects visual polish only (not the control's functional behavior — capture, validation, and completion all work regardless of how a Hint chip is styled), **this task stops here and defers the exact Hint-chip visual treatment to a follow-up**, rather than fabricate a converter binding shape not verified to actually work in WinUI 3's binding engine. Ship both templates with the Hint variant visually identical to a normal chip for now (drop the `Background`/`BorderBrush` swap entirely, accept a normal-looking chip for the "release" hint), and revisit polish once the control is live and can be visually checked.

Simplify Step 1's templates accordingly — replace the `Background`/`BorderBrush` bindings above with the flat `{StaticResource ShortcutChipAccentBrush}` value unconditionally (no converter), and do not create `ChipKindToBoolConverter` in this task. This keeps Task 2b.4 mechanical and honest about what it delivers; restyling the Hint chip is a two-line follow-up once someone can see the running control.

- [ ] **Step 2: Port `ShortcutPresenter`'s two styles (large and default), replacing `IsEmpty` triggers with `VisualStateManager`**

```xml
<!-- continuing winui3/WinTabber.UI.Common/Themes/Generic.xaml -->
    <Style x:Key="ShortcutPresenterLargeStyle" TargetType="controls:ShortcutPresenter">
        <Setter Property="IsTabStop" Value="False" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="controls:ShortcutPresenter">
                    <Grid>
                        <VisualStateManager.VisualStateGroups>
                            <VisualStateGroup x:Name="EmptyStates">
                                <VisualState x:Name="HasChips" />
                                <VisualState x:Name="Empty">
                                    <VisualState.Setters>
                                        <Setter Target="PART_Chips.Visibility" Value="Collapsed" />
                                        <Setter Target="PART_Empty.Visibility" Value="Visible" />
                                    </VisualState.Setters>
                                </VisualState>
                            </VisualStateGroup>
                        </VisualStateManager.VisualStateGroups>
                        <ItemsControl
                            x:Name="PART_Chips"
                            ItemTemplate="{StaticResource ShortcutChipTemplateLarge}"
                            ItemsSource="{TemplateBinding Chips}">
                            <ItemsControl.ItemsPanel>
                                <ItemsPanelTemplate>
                                    <StackPanel Orientation="{TemplateBinding Orientation}" />
                                </ItemsPanelTemplate>
                            </ItemsControl.ItemsPanel>
                        </ItemsControl>
                        <TextBlock
                            x:Name="PART_Empty"
                            VerticalAlignment="Center"
                            FontSize="14"
                            FontStyle="Italic"
                            Foreground="{StaticResource ShortcutEmptyForegroundBrush}"
                            Text="{TemplateBinding EmptyText}"
                            Visibility="Collapsed" />
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType="controls:ShortcutPresenter">
        <Setter Property="IsTabStop" Value="False" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="controls:ShortcutPresenter">
                    <Grid>
                        <VisualStateManager.VisualStateGroups>
                            <VisualStateGroup x:Name="EmptyStates">
                                <VisualState x:Name="HasChips" />
                                <VisualState x:Name="Empty">
                                    <VisualState.Setters>
                                        <Setter Target="PART_Chips.Visibility" Value="Collapsed" />
                                        <Setter Target="PART_Empty.Visibility" Value="Visible" />
                                    </VisualState.Setters>
                                </VisualState>
                            </VisualStateGroup>
                        </VisualStateManager.VisualStateGroups>
                        <ItemsControl
                            x:Name="PART_Chips"
                            ItemTemplate="{StaticResource ShortcutChipTemplate}"
                            ItemsSource="{TemplateBinding Chips}">
                            <ItemsControl.ItemsPanel>
                                <ItemsPanelTemplate>
                                    <StackPanel Orientation="{TemplateBinding Orientation}" />
                                </ItemsPanelTemplate>
                            </ItemsControl.ItemsPanel>
                        </ItemsControl>
                        <TextBlock
                            x:Name="PART_Empty"
                            VerticalAlignment="Center"
                            FontStyle="Italic"
                            Foreground="{StaticResource ShortcutEmptyForegroundBrush}"
                            Text="{TemplateBinding EmptyText}"
                            Visibility="Collapsed" />
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
```

**Open item, not resolved by this task:** the `IsEmpty` → `Empty`/`HasChips` `VisualState` transition above is declared but nothing calls `VisualStateManager.GoToState(this, "Empty", ...)` — WPF's `Trigger Property="IsEmpty"` fired automatically off the dependency property; WinUI 3's `VisualStateManager` requires an explicit `GoToState` call, typically from a property-changed callback in code-behind. This needs a small addition to `ShortcutPresenter.cs` (Task 2b.2) — `IsEmptyProperty`'s `PropertyMetadata` callback should call `VisualStateManager.GoToState(this, IsEmpty ? "Empty" : "HasChips", useTransitions: true)` — which was not included in Task 2b.2 above because Task 2b.2 was written before this XAML-side discovery. **Before running this task's Step 3 build, go back and add that callback to `ShortcutPresenter.cs`.** This is exactly the kind of cross-task discovery this plan's fix loop (or, if caught before implementation, a controller ruling) exists to handle — noting it here explicitly rather than silently leaving broken visual states.

- [ ] **Step 3: Port `ShortcutCaptureBox`'s two styles (dialog and default), replacing `IsCapturing` and `ValidationMessage` triggers with `VisualStateManager`**

```xml
<!-- continuing winui3/WinTabber.UI.Common/Themes/Generic.xaml -->
    <Style x:Key="ShortcutCaptureBoxDialogStyle" TargetType="controls:ShortcutCaptureBox">
        <Setter Property="IsTabStop" Value="True" />
        <Setter Property="MinHeight" Value="100" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="controls:ShortcutCaptureBox">
                    <StackPanel HorizontalAlignment="Center">
                        <VisualStateManager.VisualStateGroups>
                            <VisualStateGroup x:Name="CaptureStates">
                                <VisualState x:Name="Idle" />
                                <VisualState x:Name="Capturing">
                                    <VisualState.Setters>
                                        <Setter Target="PART_Presenter.Visibility" Value="Collapsed" />
                                        <Setter Target="PART_Capturing.Visibility" Value="Visible" />
                                        <Setter Target="PART_Hint.Visibility" Value="Visible" />
                                    </VisualState.Setters>
                                </VisualState>
                            </VisualStateGroup>
                            <VisualStateGroup x:Name="ValidationStates">
                                <VisualState x:Name="NoValidationMessage">
                                    <VisualState.Setters>
                                        <Setter Target="PART_Validation.Visibility" Value="Collapsed" />
                                    </VisualState.Setters>
                                </VisualState>
                                <VisualState x:Name="HasValidationMessage" />
                            </VisualStateGroup>
                        </VisualStateManager.VisualStateGroups>

                        <controls:ShortcutPresenter
                            x:Name="PART_Presenter"
                            HorizontalAlignment="Center"
                            EmptyText="Press a shortcut…"
                            Style="{StaticResource ShortcutPresenterLargeStyle}"
                            Trigger="{TemplateBinding Trigger}" />

                        <ItemsControl
                            x:Name="PART_Capturing"
                            HorizontalAlignment="Center"
                            ItemTemplate="{StaticResource ShortcutChipTemplateLarge}"
                            ItemsSource="{TemplateBinding PendingChips}"
                            Visibility="Collapsed">
                            <ItemsControl.ItemsPanel>
                                <ItemsPanelTemplate>
                                    <StackPanel Orientation="Horizontal" />
                                </ItemsPanelTemplate>
                            </ItemsControl.ItemsPanel>
                        </ItemsControl>

                        <TextBlock
                            x:Name="PART_Hint"
                            Margin="0,12,0,0"
                            HorizontalAlignment="Center"
                            FontStyle="Italic"
                            Foreground="{StaticResource ShortcutHintForegroundBrush}"
                            Text="Press a shortcut… (Esc to cancel)"
                            Visibility="Collapsed" />

                        <TextBlock
                            x:Name="PART_Validation"
                            Margin="0,8,0,0"
                            HorizontalAlignment="Center"
                            Foreground="{StaticResource ShortcutValidationForegroundBrush}"
                            Text="{TemplateBinding ValidationMessage}"
                            TextAlignment="Center"
                            TextWrapping="Wrap" />
                    </StackPanel>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style x:Key="ShortcutEditButtonStyle" TargetType="Button">
        <Setter Property="HorizontalAlignment" Value="Left" />
        <Setter Property="HorizontalContentAlignment" Value="Left" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Grid x:Name="RootGrid">
                        <VisualStateManager.VisualStateGroups>
                            <VisualStateGroup x:Name="CommonStates">
                                <VisualState x:Name="Normal" />
                                <VisualState x:Name="PointerOver">
                                    <VisualState.Setters>
                                        <Setter Target="Bg.Background" Value="{StaticResource ShortcutGroupHoverBackgroundBrush}" />
                                    </VisualState.Setters>
                                </VisualState>
                            </VisualStateGroup>
                        </VisualStateManager.VisualStateGroups>
                        <Border
                            x:Name="Bg"
                            Padding="8,5"
                            Background="{StaticResource ShortcutGroupBackgroundBrush}"
                            BorderBrush="{StaticResource ShortcutGroupBorderBrush}"
                            BorderThickness="1"
                            CornerRadius="6">
                            <ContentPresenter HorizontalAlignment="Left" VerticalAlignment="Center" />
                        </Border>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <Style TargetType="controls:ShortcutCaptureBox">
        <Setter Property="IsTabStop" Value="True" />
        <Setter Property="MinHeight" Value="28" />
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="controls:ShortcutCaptureBox">
                    <Border
                        Padding="6,4"
                        Background="Transparent"
                        BorderBrush="{StaticResource ShortcutCaptureBoxBorderBrush}"
                        BorderThickness="1"
                        CornerRadius="6">
                        <StackPanel>
                            <VisualStateManager.VisualStateGroups>
                                <VisualStateGroup x:Name="CaptureStates">
                                    <VisualState x:Name="Idle" />
                                    <VisualState x:Name="Capturing">
                                        <VisualState.Setters>
                                            <Setter Target="PART_Capturing.Visibility" Value="Visible" />
                                            <Setter Target="PART_Presenter.Visibility" Value="Collapsed" />
                                        </VisualState.Setters>
                                    </VisualState>
                                </VisualStateGroup>
                                <VisualStateGroup x:Name="ValidationStates">
                                    <VisualState x:Name="NoValidationMessage">
                                        <VisualState.Setters>
                                            <Setter Target="PART_Validation.Visibility" Value="Collapsed" />
                                        </VisualState.Setters>
                                    </VisualState>
                                    <VisualState x:Name="HasValidationMessage" />
                                </VisualStateGroup>
                            </VisualStateManager.VisualStateGroups>

                            <controls:ShortcutPresenter
                                x:Name="PART_Presenter"
                                Trigger="{TemplateBinding Trigger}" />

                            <StackPanel x:Name="PART_Capturing" Orientation="Horizontal" Visibility="Collapsed">
                                <ItemsControl
                                    ItemTemplate="{StaticResource ShortcutChipTemplate}"
                                    ItemsSource="{TemplateBinding PendingChips}">
                                    <ItemsControl.ItemsPanel>
                                        <ItemsPanelTemplate>
                                            <StackPanel Orientation="Horizontal" />
                                        </ItemsPanelTemplate>
                                    </ItemsControl.ItemsPanel>
                                </ItemsControl>
                                <TextBlock
                                    VerticalAlignment="Center"
                                    FontStyle="Italic"
                                    Foreground="{StaticResource ShortcutEmptyForegroundBrush}"
                                    Text="Press a shortcut… (Esc to cancel)" />
                            </StackPanel>

                            <TextBlock
                                x:Name="PART_Validation"
                                FontSize="11"
                                Foreground="{StaticResource ShortcutValidationForegroundBrush}"
                                Text="{TemplateBinding ValidationMessage}"
                                TextWrapping="Wrap" />
                        </StackPanel>
                    </Border>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
```

**Open item, not resolved by this task, same class of gap as Step 2's:** `CaptureStates`/`ValidationStates` are declared but nothing calls `GoToState` when `IsCapturing`/`ValidationMessage` change. `ShortcutCaptureBox.cs` (Task 2b.3) needs `IsCapturingProperty`'s and `ValidationMessageProperty`'s `PropertyMetadata` callbacks to call `VisualStateManager.GoToState(this, ..., useTransitions: true)` for their respective groups — not included in Task 2b.3 above for the same reason as Step 2's note. **Before this task's Step 4 build, go back and add both callbacks to `ShortcutCaptureBox.cs`.**

- [ ] **Step 4: Build**

Run: `dotnet build WinTabber.slnx`
Expected: does not build clean until the two `GoToState`-wiring gaps from Steps 2 and 3 are closed in `ShortcutPresenter.cs`/`ShortcutCaptureBox.cs`. Close them, then re-build until clean.

- [ ] **Step 5: Commit**

```bash
git add winui3/WinTabber.UI.Common/Themes/Generic.xaml \
  winui3/WinTabber.UI.Common/Controls/ShortcutPresenter.cs \
  winui3/WinTabber.UI.Common/Controls/ShortcutCaptureBox.cs
git commit -m "feat: port Generic.xaml templates for ShortcutPresenter/ShortcutCaptureBox to WinUI3

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

**What Phase 2b honestly does not fully deliver:** two visual/behavioral gaps are explicitly deferred rather than fabricated — the Hint-chip visual treatment (Task 2b.4 Step 1) and confirming `LosingFocus`'s exact behavior (Task 2b.3, note 2). Both are named, both are small, and both are safe to defer because they affect polish/edge-case behavior, not the control's core capture/validate/complete logic. A future task, written once the control can actually be seen running, should close both.

---

## Phase 3 — Convert `SettingsWindow`

All files below were read in full before writing this phase: `WinTabberUI/Views/SettingsWindow.xaml(.cs)`,
`GeneralSettingsPage.xaml(.cs)`, `AppearanceSettingsPage.xaml(.cs)`, `ShortcutsSettingsPage.xaml(.cs)`,
`ShortcutCaptureDialog.xaml` (its `.xaml.cs` was read earlier in this plan's history), the current
`winui3/WinTabberUI` shell (`App.xaml.cs`, `MainWindow.xaml`, `WinTabberUI.csproj`), `WinTabberUI/Bootstrapper.cs`,
`WinTabber.ViewModels/SettingsWindowViewModel.cs` (class name `SettingsViewModel`, despite the file name),
`WinTabber.ViewModels/Settings/GeneralSettingsViewModel.cs`, and `WinTabber.Interop/GsudoElevationLauncher.cs`.

**Scope correction:** `WinTabberUI/EditableTextBlock.xaml` is not used by any Settings page — it is the
window-tile title editor, consumed by `WindowSelectorWindow`. It belongs to Phase 4, not this phase; not
read further here.

**`SelectedView`'s WPF pattern doesn't port as-is.** WPF's `ui:Frame` here is not real page navigation —
`Frame.Content` is bound directly to `SelectedView` (a ViewModel instance), and `Frame.Resources` holds
`DataTemplate`s keyed by `DataType`, so WPF's implicit content-templating machinery picks the matching
template automatically, the same way a bare `ContentControl` would. WinUI 3's `Frame` is for real
`Frame.Navigate(Type)` page navigation and has no equivalent implicit-by-type template selection. The
native replacement is a plain `ContentControl` with an explicit `DataTemplateSelector` (Task 3.5) — a real,
supported WinUI 3 API (`Microsoft.UI.Xaml.Controls.DataTemplateSelector`), not a fabricated workaround.

**Icon glyphs stay deferred per Global Constraints.** `NavigationView`'s per-section icon (bound to
`SettingsViewModelBase.Icon`, an `IconKey`) and `ShortcutsSettingsPage`'s two `FluentSystemIcons.Add_32_Filled`
references all get the placeholder glyph + `TODO(icon)` comment pattern — Phase 6's job, not this one.

### Task 3.1: Minimal DI bootstrap for `winui3/WinTabberUI`, enough to construct `SettingsViewModel`

**Files:**
- Create: `winui3/WinTabberUI/Bootstrapper.cs`
- Modify: `winui3/WinTabberUI/App.xaml.cs`

**Interfaces:**
- Produces: `WinTabberUI.Bootstrapper.Init() : ServiceProvider`, following the same static-class/extension-method shape as `WinTabberUI/Bootstrapper.cs` (the WPF one) so the two stay easy to compare and eventually reconcile in Phase 5.
- Consumes: `WinTabber.ViewModels.SettingsViewModel`'s constructor — `WinTabberEventManager`, `ApplicationSettings`, `IShortcutMapProvider`, `GsudoElevationLauncher` — and each of those types' own transitive dependencies.

Full app bootstrap parity (tray icon, all coordinators, every window) is Phase 5's job. This task registers only what `SettingsViewModel`'s dependency graph actually needs, traced from its constructor down:

- `SettingsViewModel(WinTabberEventManager, ApplicationSettings, IShortcutMapProvider, GsudoElevationLauncher)`
- `WinTabberEventManager(IWindowInterop, InputListenerService, IShortcutMapProvider)` — confirmed by reading `WinTabber.Events/WinTabberEventManager.cs`. Constructing this for real starts the live global keyboard/mouse hook (`InputListenerService.Init()`), which is correct and unavoidable here: `ShortcutsSettingsViewModel`'s whole purpose is capturing a *live* shortcut via `winTabberEventManager.TriggerSource`, so a fake/no-op event manager would make the one page this phase most needs to prove out untestable.
- `IWindowInterop` → `InteropProxy` (per the WPF `Bootstrapper.cs` registration: `InteropProxy` registered once, exposed under `IProcessControl`/`IWindowPlacement`/`IWindowInterop`/`IWindowVisibility`).
- `IShortcutMapProvider` → `ShortcutMapProvider`, constructed from `ApplicationSettings.Shortcuts.ToMap()` (mirror the WPF registration exactly — `sp => new ShortcutMapProvider(sp.GetRequiredService<ApplicationSettings>().Shortcuts.ToMap())`).
- `ApplicationSettings` → `ApplicationSettings.Load()`, singleton (mirror the WPF registration's comment about why it must be a single shared instance).
- `GsudoElevationLauncher` — parameterless constructor, confirmed by reading the file; no further dependencies.
- `InputListenerService` — read `WinTabber.Events/InputListenerService.cs` (or wherever it actually lives — confirm the exact path via `find`/`grep` if it is not directly under `WinTabber.Events/`) to find its own constructor dependencies before registering it; this file was **not** read while writing this plan, so its exact dependency chain must be traced by whoever implements this task, following the same pattern as every other dependency listed above (register in the app project's Bootstrapper, following `WinTabberUI/Bootstrapper.cs`'s existing pattern for the same type one-for-one).

**Explicitly excluded from this task** (Phase 5's job, or simply not needed by `SettingsViewModel`): `AutoStartupService`, `BackgroundServiceContainer`, `WindowManager`, every audio/media/SMTC service, `IProcessSuspensionService`, `IWindowThumbnailService`, `AppCache`, every coordinator, every other `View`/`ViewModel` registration, `IElevationBackendProvider`/`BuiltInElevationLauncher`/`ElevationLauncherResolver` (nothing in `SettingsViewModel`'s graph resolves `IElevationLauncher` — `GeneralSettingsViewModel` takes the concrete `GsudoElevationLauncher` directly).

- [ ] **Step 1: Trace `InputListenerService`'s own dependencies**

Run: `grep -rn "class InputListenerService" WinTabber.Events` to confirm its file location, then read that file's constructor in full. Register whatever it needs, following the exact registration shape `WinTabberUI/Bootstrapper.cs` already uses for the same type (do not re-derive a different shape — copy the existing pattern one-for-one, adjusted only for this project's namespace).

- [ ] **Step 2: Write the bootstrapper**

```csharp
// winui3/WinTabberUI/Bootstrapper.cs
using Microsoft.Extensions.DependencyInjection;
using WinTabber.Events;
using WinTabber.Events.Shortcuts;
using WinTabber.Interop;
using WinTabberUI.Models.Settings;
using WinTabber.ViewModels;

namespace WinTabberUI;

public static class Bootstrapper
{
    public static ServiceProvider Init()
    {
        return new ServiceCollection()
            .AddCoreServices()
            .AddSettingsGraph()
            .BuildServiceProvider();
    }

    private static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        return services
            .AddSingleton<InteropProxy>()
            .AddSingleton<IProcessControl>(sp => sp.GetRequiredService<InteropProxy>())
            .AddSingleton<IWindowPlacement>(sp => sp.GetRequiredService<InteropProxy>())
            .AddSingleton<IWindowInterop>(sp => sp.GetRequiredService<InteropProxy>())
            .AddSingleton<IWindowVisibility>(sp => sp.GetRequiredService<InteropProxy>());
            // Step 1's InputListenerService registration goes here, following the exact shape
            // WinTabberUI/Bootstrapper.cs already uses for it.
    }

    private static IServiceCollection AddSettingsGraph(this IServiceCollection services)
    {
        return services
            .AddSingleton<ApplicationSettings>(_ => ApplicationSettings.Load())
            .AddSingleton<IShortcutMapProvider>(sp => new ShortcutMapProvider(
                sp.GetRequiredService<ApplicationSettings>().Shortcuts.ToMap()))
            .AddSingleton<WinTabberEventManager>()
            .AddSingleton<GsudoElevationLauncher>()
            .AddSingleton<SettingsViewModel>();
    }
}
```

Note: `WinTabberUI.Models.Settings.ApplicationSettings` is the namespace `ApplicationSettings` actually lives under today (physically in `WinTabber.Infrastructure`, namespace unchanged per this plan's Task 1.2 precedent) — confirm this `using` resolves; if the namespace has since moved, use whatever `grep -rn "class ApplicationSettings"` finds.

- [ ] **Step 3: Wire it into `App.xaml.cs`**

```csharp
// winui3/WinTabberUI/App.xaml.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using WinTabber.ViewModels;
using WinTabberUI.Views;

namespace WinTabberUI;

public partial class App : Application
{
    private Window? _window;
    public static ServiceProvider Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = Bootstrapper.Init();

        _window = new SettingsWindow(Services.GetRequiredService<SettingsViewModel>());
        _window.Activate();
    }
}
```

This replaces the Phase 1 placeholder `MainWindow` as the shell's entry point for the duration of Phase 3
(there is no tray icon or event-driven show/hide yet — Phase 5's job — so launching straight into
`SettingsWindow` is the only way to manually verify this phase's work at all). `MainWindow.xaml(.cs)` stays
in the project, unreferenced, rather than being deleted — Phase 5 will decide the real startup shape.

- [ ] **Step 4: Build**

Run: `dotnet build WinTabber.slnx`
Expected: does not build yet — `SettingsWindow` (Task 3.5) does not exist in the winui3 tree. This step's real purpose is confirming the DI graph above resolves with no missing-service exceptions once a temporary no-op `Window` stands in; if a full build isn't possible until Task 3.5 lands, the implementer may defer this task's own final build verification to the end of Task 3.5 instead — note that explicitly in this task's commit message if so, since it deviates from this plan's usual per-task build-and-commit rhythm.

- [ ] **Step 5: Commit**

```bash
git add winui3/WinTabberUI/Bootstrapper.cs winui3/WinTabberUI/App.xaml.cs
git commit -m "feat: add minimal DI bootstrap for winui3/WinTabberUI's SettingsViewModel graph

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

### Task 3.2: Port `GeneralSettingsPage`

**Files:**
- Create: `winui3/WinTabberUI/Views/GeneralSettingsPage.xaml`
- Create: `winui3/WinTabberUI/Views/GeneralSettingsPage.xaml.cs`

**Interfaces:**
- Consumes: `WinTabber.ViewModels.Settings.GeneralSettingsViewModel` (unchanged from Phase 0/1), the converters/commands ported in Phase 2 (`EnumValuesConverter`, `StringToEnumConverter`, `BoolToVisibilityConverter` — all already in `winui3/WinTabber.UI.Common/ValueConverters/ValueConverters.cs`, Task 2.1).

Control mapping for this page, per the design spec: `ui:SettingsCard`/`ui:SettingsExpander` →
`CommunityToolkit.WinUI.Controls.SettingsControls` (already referenced in `winui3/WinTabber.UI.Common.csproj`
since Task 1.4); `ui:ToggleSwitch`/`ui:FontIcon` → native `ToggleSwitch`/`FontIcon`; `rxwpf:ReactivePage` →
`ReactiveUI.WinUI`'s `ReactivePage`. `HeaderedContentControl` (from a Toolkit package the WPF app doesn't
even reference explicitly — it resolves through an implicit style) is replaced with a plain `StackPanel`
holding the header `TextBlock` followed by the content, rather than adding a new
`CommunityToolkit.WinUI.Controls.HeaderedControls` package dependency for what is, in this file, a purely
cosmetic header-above-content wrapper with no other behavior.

`this.Bind(...)` with an explicit `signalViewUpdate` (used twice in the WPF code-behind, for
`StartupList`/`ThumbnailResizeModeList`) is a ReactiveUI-idiomatic two-way binding that should port
unchanged under `ReactiveUI.WinUI` — `ComboBox.SelectionChanged` exists identically in WinUI 3.
`ui:TextBoxHelper.IsDeleteButtonVisible="False"` doesn't appear on this page (only on Appearance's — see
Task 3.3); nothing to remove here.

- [ ] **Step 1: Port the XAML**

```xml
<!-- winui3/WinTabberUI/Views/GeneralSettingsPage.xaml -->
<rxwpf:ReactivePage
    x:Class="WinTabberUI.Views.GeneralSettingsPage"
    x:TypeArguments="settingsvm:GeneralSettingsViewModel"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:settingsvm="using:WinTabber.ViewModels.Settings"
    xmlns:c="using:WinTabber.UI.Common.ValueConverters"
    xmlns:rxwpf="using:ReactiveUI"
>
    <Page.Resources>
        <c:EnumValuesConverter x:Key="EnumValuesConverter" />
        <c:StringToEnumConverter x:Key="StringToEnumConverter" />
        <c:BoolToVisibilityConverter x:Key="BoolToVisibilityConverter" />
    </Page.Resources>
    <Grid>
        <StackPanel Margin="20" VerticalAlignment="Top">
            <TextBlock FontSize="24" Foreground="White" Text="General" />
            <StackPanel Margin="0,12,0,0">
                <controls:SettingsCard xmlns:controls="using:CommunityToolkit.WinUI.Controls" Header="Startup mode">
                    <controls:SettingsCard.HeaderIcon>
                        <FontIcon Glyph="&#xe7f4;" />
                    </controls:SettingsCard.HeaderIcon>
                    <ComboBox
                        x:Name="StartupList"
                        MinWidth="220"
                        FontSize="14"
                        VerticalAlignment="Center"
                        ItemsSource="{x:Bind ViewModel.StartupModes, Mode=OneWay}" />
                </controls:SettingsCard>
                <controls:SettingsCard
                    xmlns:controls="using:CommunityToolkit.WinUI.Controls"
                    Margin="0,8,0,0"
                    Header="Thumbnail resize mode"
                    Description="What resizing a floating window thumbnail does">
                    <controls:SettingsCard.HeaderIcon>
                        <FontIcon Glyph="&#xe740;" />
                    </controls:SettingsCard.HeaderIcon>
                    <ComboBox
                        x:Name="ThumbnailResizeModeList"
                        MinWidth="220"
                        FontSize="14"
                        VerticalAlignment="Center"
                        ItemsSource="{x:Bind ViewModel.ThumbnailResizeModes, Mode=OneWay}" />
                </controls:SettingsCard>
            </StackPanel>
            <TextBlock Margin="0,20,0,0" FontSize="24" Foreground="White" Text="Features" />
            <StackPanel Margin="0,12,0,0">
                <controls:SettingsCard
                    xmlns:controls="using:CommunityToolkit.WinUI.Controls"
                    Header="Window suspend"
                    Description="The sleep button on a window tile, its shortcut, the suspended-windows shortcut, and the suspended-windows bar">
                    <ToggleSwitch IsOn="{x:Bind ViewModel.EnableWindowSuspension, Mode=TwoWay}" />
                </controls:SettingsCard>
                <controls:SettingsCard
                    xmlns:controls="using:CommunityToolkit.WinUI.Controls"
                    Margin="0,8,0,0"
                    Header="Media controls"
                    Description="The media controls shortcut and window, and the preload of installed apps and audio devices it uses">
                    <ToggleSwitch IsOn="{x:Bind ViewModel.EnableMediaControls, Mode=TwoWay}" />
                </controls:SettingsCard>
                <controls:SettingsCard
                    xmlns:controls="using:CommunityToolkit.WinUI.Controls"
                    Margin="0,8,0,0"
                    Header="Close application windows"
                    Description="The close button on the window selector and the close-application-windows shortcut">
                    <ToggleSwitch IsOn="{x:Bind ViewModel.EnableCloseApplicationWindows, Mode=TwoWay}" />
                </controls:SettingsCard>
                <controls:SettingsExpander
                    xmlns:controls="using:CommunityToolkit.WinUI.Controls"
                    Margin="0,8,0,0"
                    Header="Focus select"
                    Description="Hold the modifier below while selecting a window to minimize the others"
                    IsExpanded="False"
                    IsEnabled="{x:Bind ViewModel.EnableFocusSelect, Mode=OneWay}">
                    <ToggleSwitch IsOn="{x:Bind ViewModel.EnableFocusSelect, Mode=TwoWay}" />
                    <controls:SettingsExpander.Items>
                        <controls:SettingsCard Header="Modifier" Description="Which modifier, held at selection, triggers Focus Select">
                            <ComboBox
                                x:Name="FocusSelectModifierList"
                                MinWidth="220"
                                FontSize="14"
                                VerticalAlignment="Center"
                                ItemsSource="{x:Bind ViewModel.FocusSelectModifiers, Mode=OneWay}" />
                        </controls:SettingsCard>
                        <controls:SettingsCard Header="Scope" Description="Which windows get minimized: only the ones the switcher showed, or every other window">
                            <ComboBox
                                x:Name="FocusSelectScopeList"
                                MinWidth="220"
                                FontSize="14"
                                VerticalAlignment="Center"
                                ItemsSource="{x:Bind ViewModel.FocusSelectScopes, Mode=OneWay}" />
                        </controls:SettingsCard>
                    </controls:SettingsExpander.Items>
                </controls:SettingsExpander>
                <controls:SettingsCard
                    xmlns:controls="using:CommunityToolkit.WinUI.Controls"
                    Margin="0,8,0,0"
                    Header="Elevation backend"
                    Description="How WinTabber closes or minimizes windows belonging to an elevated (admin) process">
                    <ComboBox
                        x:Name="ElevationBackendList"
                        MinWidth="220"
                        FontSize="14"
                        VerticalAlignment="Center"
                        ItemsSource="{x:Bind ViewModel.ElevationBackends, Mode=OneWay}" />
                </controls:SettingsCard>
                <controls:SettingsCard
                    xmlns:controls="using:CommunityToolkit.WinUI.Controls"
                    Margin="0,8,0,0"
                    Header="gsudo not found"
                    Description="Install gsudo via winget to use it as the elevation backend"
                    Visibility="{x:Bind ViewModel.ShowGsudoInstallPrompt, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}">
                    <Button Command="{x:Bind ViewModel.InstallGsudoCommand}" Content="Install" />
                </controls:SettingsCard>
            </StackPanel>
        </StackPanel>
    </Grid>
</rxwpf:ReactivePage>
```

Note: repeating `xmlns:controls="using:CommunityToolkit.WinUI.Controls"` on every `SettingsCard`/
`SettingsExpander` element above is verbose but deliberately explicit for this port — once the page
compiles, collapse it to a single `xmlns:controls="using:CommunityToolkit.WinUI.Controls"` declaration on
the root `rxwpf:ReactivePage` element (the normal XAML pattern) rather than leaving it repeated; it is
written per-element here only so each control's exact required namespace is unambiguous while porting.

`StartupList`'s `SelectedValue` binding from WPF (`SelectedValue="{Binding Path=StarupMode, ...}"`,
note the original's misspelling of "Startup") is deliberately not carried into the XAML above — the
code-behind's `this.Bind(...)` call (Step 2) is the one place the two-way `SelectedValue`↔`StartupMode`
sync actually happens, matching how the WPF code-behind did it (the WPF XAML's own `SelectedValue`
binding was in addition to, not instead of, the code-behind bind — reproduce only the code-behind path
here, since duplicating both would create two competing write paths).

- [ ] **Step 2: Port the code-behind**

```csharp
// winui3/WinTabberUI/Views/GeneralSettingsPage.xaml.cs
using ReactiveUI;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using WinTabber.ViewModels.Settings;

namespace WinTabberUI.Views;

public sealed partial class GeneralSettingsPage : ReactivePage<GeneralSettingsViewModel>, IViewFor<GeneralSettingsViewModel>
{
    public GeneralSettingsPage()
    {
        InitializeComponent();
        this.WhenActivated((dispose) =>
        {
            this.Bind(
                ViewModel,
                vm => vm.StartupMode,
                view => view.StartupList.SelectedValue,
                signalViewUpdate: Observable.FromEventPattern(StartupList, nameof(StartupList.SelectionChanged))
            ).DisposeWith(dispose);

            this.Bind(
                ViewModel,
                vm => vm.ThumbnailResizeMode,
                view => view.ThumbnailResizeModeList.SelectedValue,
                signalViewUpdate: Observable.FromEventPattern(ThumbnailResizeModeList, nameof(ThumbnailResizeModeList.SelectionChanged))
            ).DisposeWith(dispose);
        });
    }
}
```

`DataContextChanged`/`ViewModel = e.NewValue as ...` from the WPF code-behind is dropped: WinUI 3's
`ReactivePage<T>` (from `ReactiveUI.WinUI`) sets `ViewModel` from `DataContext` itself the same way its WPF
counterpart does — **TODO(verify):** confirm this against the actual `ReactiveUI.WinUI` package source or
a real compiler error before assuming it, since this plan was written without the ability to browse that
package's source directly; if `ReactivePage<T>` does NOT wire `ViewModel` automatically in the installed
version, restore an explicit `DataContextChanged`/`OnDataContextChanged` handler exactly like the WPF
original's (WinUI 3's `FrameworkElement.DataContextChanged` event exists with the same name and a
compatible signature, just a different event-args type).

`StartupList_LostFocus` is dropped — it was already fully commented-out dead code in the WPF original
(confirmed by reading the file); nothing to port.

- [ ] **Step 3: Build**

Run: `dotnet build WinTabber.slnx`
Expected: builds clean, or fails specifically on the `ReactivePage<T>`/`ViewModel` question flagged above — resolve per that note's guidance if so.

- [ ] **Step 4: Commit**

```bash
git add winui3/WinTabberUI/Views/GeneralSettingsPage.xaml winui3/WinTabberUI/Views/GeneralSettingsPage.xaml.cs
git commit -m "feat: port GeneralSettingsPage to WinUI3

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

### Task 3.3: Port `AppearanceSettingsPage`

**Files:**
- Create: `winui3/WinTabberUI/Views/AppearanceSettingsPage.xaml`
- Create: `winui3/WinTabberUI/Views/AppearanceSettingsPage.xaml.cs`

**Interfaces:**
- Consumes: `WinTabber.ViewModels.Settings.AppearanceSettingsViewModel` (unchanged from Phase 0/1).

Simpler than Task 3.2 — no code-behind bindings, no converters beyond what `x:Bind` handles directly.
`ui:TextBoxHelper.IsDeleteButtonVisible="False"` (two sites) is removed outright per the design spec's
control-mapping rule — native `TextBox` has no delete-button chrome to suppress.

- [ ] **Step 1: Port the XAML**

```xml
<!-- winui3/WinTabberUI/Views/AppearanceSettingsPage.xaml -->
<rxui:ReactivePage
    x:Class="WinTabberUI.Views.AppearanceSettingsPage"
    x:TypeArguments="settingsvm:AppearanceSettingsViewModel"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:settingsvm="using:WinTabber.ViewModels.Settings"
    xmlns:rxui="using:ReactiveUI"
    xmlns:controls="using:CommunityToolkit.WinUI.Controls"
>
    <Grid>
        <StackPanel Margin="20" VerticalAlignment="Top">
            <TextBlock FontSize="24" Foreground="White" Text="Scaling" />
            <StackPanel Margin="0,12,0,0">
                <controls:SettingsCard Header="Scale with display density" Description="Scale Window Selector tiles with screen DPI">
                    <ToggleSwitch IsOn="{x:Bind ViewModel.ScaleToDpi, Mode=TwoWay}" />
                </controls:SettingsCard>
                <controls:SettingsCard Header="UI Scale" Description="Scale of certain UI elements, on top of any other scaling" ContentAlignment="Vertical">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <Slider
                            Minimum="0.5"
                            Maximum="3.0"
                            VerticalAlignment="Center"
                            IsThumbToolTipEnabled="True"
                            StepFrequency="0.1"
                            Value="{x:Bind ViewModel.ScaleFactor, Mode=TwoWay}" />
                        <TextBox
                            Grid.Column="1"
                            Width="40"
                            Margin="10,0,0,0"
                            HorizontalContentAlignment="Center"
                            VerticalContentAlignment="Center"
                            Text="{x:Bind ViewModel.ScaleFactor, Mode=TwoWay}" />
                    </Grid>
                </controls:SettingsCard>
                <controls:SettingsCard Description="Width of Window Selector tiles in DIP" Header="Window Selector Tile Width" ContentAlignment="Vertical">
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*" />
                            <ColumnDefinition Width="Auto" />
                        </Grid.ColumnDefinitions>
                        <Slider
                            Minimum="250"
                            Maximum="500"
                            VerticalAlignment="Center"
                            IsThumbToolTipEnabled="True"
                            StepFrequency="10"
                            Value="{x:Bind ViewModel.WindowTileWidth, Mode=TwoWay}" />
                        <TextBox
                            Grid.Column="1"
                            Width="40"
                            Margin="10,0,0,0"
                            HorizontalContentAlignment="Center"
                            VerticalContentAlignment="Center"
                            Text="{x:Bind ViewModel.WindowTileWidth, Mode=TwoWay}" />
                    </Grid>
                </controls:SettingsCard>
            </StackPanel>
        </StackPanel>
    </Grid>
</rxui:ReactivePage>
```

WPF's `Slider` used `TickFrequency`/`IsSnapToTickEnabled`/`TickPlacement` for the tick marks; WinUI 3's
`Slider` has no `TickPlacement`/tick-mark rendering at all — `StepFrequency` is the closest equivalent
(it snaps the value the same way `IsSnapToTickEnabled` did, just without drawing tick marks).
`IsThumbToolTipEnabled="True"` is added as a reasonable substitute for the visual feedback WPF's tick
marks gave, not a literal requirement — this is the plan author's judgment call, not a spec mandate;
drop it if it doesn't read well once the page is actually visible. `TextBox`'s `Width="10"` from the WPF
original is widened to `40` here since a single-digit-wide box was almost certainly already relying on
WPF's more forgiving text clipping — a `TODO(verify)`-free, low-risk cosmetic judgment call, adjust once
visible.

- [ ] **Step 2: Port the code-behind**

```csharp
// winui3/WinTabberUI/Views/AppearanceSettingsPage.xaml.cs
using WinTabber.ViewModels.Settings;

namespace WinTabberUI.Views;

public sealed partial class AppearanceSettingsPage : ReactiveUI.ReactivePage<AppearanceSettingsViewModel>
{
    public AppearanceSettingsPage()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build WinTabber.slnx`
Expected: builds clean.

- [ ] **Step 4: Commit**

```bash
git add winui3/WinTabberUI/Views/AppearanceSettingsPage.xaml winui3/WinTabberUI/Views/AppearanceSettingsPage.xaml.cs
git commit -m "feat: port AppearanceSettingsPage to WinUI3

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

### Task 3.4: Port `ShortcutsSettingsPage` and `ShortcutCaptureDialog`

**Files:**
- Create: `winui3/WinTabberUI/Views/ShortcutsSettingsPage.xaml`
- Create: `winui3/WinTabberUI/Views/ShortcutsSettingsPage.xaml.cs`
- Create: `winui3/WinTabberUI/Views/ShortcutCaptureDialog.xaml`
- Create: `winui3/WinTabberUI/Views/ShortcutCaptureDialog.xaml.cs`

**Interfaces:**
- Consumes: `WinTabber.ViewModels.Settings.ShortcutsSettingsViewModel`/`ShortcutBindingViewModel`/`ShortcutCommandViewModel`/`ShortcutGroupViewModel` (unchanged), and `WinTabber.UI.Common.Controls.ShortcutPresenter`/`ShortcutCaptureBox` plus `Themes/Generic.xaml`'s styles (Phase 2b, already reviewed clean).

This is the page that most directly exercises Phase 2b's work — `ShortcutPresenter` inside the binding
row's button, `ShortcutCaptureBox` inside the dialog. Two real conversions beyond mechanical control
mapping:

1. **`DataTemplate.Triggers`/`DataTrigger` (forbidden in WinUI 3) → direct `x:Bind` with a converter.**
   The WPF original shows `ConflictIcon` when `HasConflict` is true via a `DataTrigger`. Replace with a
   direct `Visibility="{x:Bind HasConflict, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}"`
   on the `FontIcon` itself — no `VisualStateManager` needed here since this is a plain `DataTemplate`
   binding, not a `Style`/`ControlTemplate` trigger (the `OnApplyTemplate` gotcha from Phase 2b's final
   review does not apply to this case).
2. **`ShortcutPresenter.Trigger="{Binding Trigger}"` needs `Mode=OneWay`, not the Phase 2b hazard's
   `TwoWay`.** This page only *displays* the trigger inside the row button — nothing here writes back to
   `ShortcutPresenter.Trigger`, so the lost `BindsTwoWayByDefault` (documented as a Phase 3 hazard at the
   end of Phase 2's scope note) does not bite: `Mode=OneWay` is correct and sufficient, not a workaround.

`{x:Static ui:FluentSystemIcons.Add_32_Filled}` (the "add shortcut" button icon) gets the deferred-icon
placeholder pattern per Global Constraints — this is exactly the kind of site that rule anticipated.
`ConflictIcon`'s hardcoded `Foreground="#FFC42B1C"` and `Glyph="&#xE7BA;"` are literal values, not an
iNKORE key — they port unchanged, not deferred.

`ShortcutCaptureDialog`'s WPF `Style="{DynamicResource ContentDialog}"` and its several
`{DynamicResource ...}` brush references need to become `{ThemeResource}` (`DynamicResource` is
prohibited in WinUI 3 per the migration skill) — WinUI 3's `ContentDialog` already ships a default
`ContentDialogStyle` via `XamlControlsResources`, so the explicit `Style="{DynamicResource ContentDialog}"`
line is dropped entirely rather than translated; the dialog gets the native default look, only overriding
`ContentDialogPadding`/`ContentDialogTitleMargin` as the WPF original already did (those two keys are
real WinUI 3 `ContentDialog` template resource keys too, so the override translates as-is).

- [ ] **Step 1: Port `ShortcutsSettingsPage.xaml`**

```xml
<!-- winui3/WinTabberUI/Views/ShortcutsSettingsPage.xaml -->
<rxwpf:ReactivePage
    x:Class="WinTabberUI.Views.ShortcutsSettingsPage"
    x:TypeArguments="settingsvm:ShortcutsSettingsViewModel"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:settingsvm="using:WinTabber.ViewModels.Settings"
    xmlns:controls="using:WinTabber.UI.Common.Controls"
    xmlns:c="using:WinTabber.UI.Common.ValueConverters"
    xmlns:ui="using:CommunityToolkit.WinUI.Controls"
    xmlns:rxwpf="using:ReactiveUI"
>
    <Page.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <!-- ShortcutEditButtonStyle and its brushes live here, not auto-merged like the
                     implicit ShortcutPresenter/ShortcutCaptureBox default styles WinUI 3 resolves by
                     TargetType lookup — this is a plain-Button keyed style, so it needs an explicit
                     merge, same reasoning as the WPF original's comment. -->
                <ResourceDictionary Source="ms-appx:///WinTabber.UI.Common/Themes/Generic.xaml" />
            </ResourceDictionary.MergedDictionaries>

            <c:BoolToVisibilityConverter x:Key="BoolToVisibilityConverter" />

            <!-- Editing and deleting both happen in ShortcutCaptureDialog (opened from code-behind).
                 The whole combination is one clickable frame — there is no separate pencil icon. -->
            <DataTemplate x:Key="BindingRowTemplate" x:DataType="settingsvm:ShortcutBindingViewModel">
                <StackPanel Margin="0,2" HorizontalAlignment="Right" Orientation="Horizontal">
                    <Button Click="OnEditShortcutClick" Style="{StaticResource ShortcutEditButtonStyle}" ToolTipService.ToolTip="Click to change">
                        <controls:ShortcutPresenter EmptyText="Click to set shortcut" Trigger="{x:Bind Trigger, Mode=OneWay}" />
                    </Button>

                    <!-- Conflicts warn, they never block saving (§6.1). -->
                    <FontIcon
                        Margin="8,0"
                        VerticalAlignment="Center"
                        Foreground="#FFC42B1C"
                        Glyph="&#xE7BA;"
                        ToolTipService.ToolTip="{x:Bind ConflictMessage, Mode=OneWay}"
                        Visibility="{x:Bind HasConflict, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}" />
                </StackPanel>
            </DataTemplate>

            <DataTemplate x:Key="CommandRowTemplate" x:DataType="settingsvm:ShortcutCommandViewModel">
                <ui:SettingsCard Margin="0,4,0,0" Description="{x:Bind Desscription, Mode=OneWay}" Header="{x:Bind DisplayName, Mode=OneWay}">
                    <ui:SettingsCard.HeaderIcon>
                        <!-- TODO(icon): originally iNKORE FluentSystemIcons via IconKeyToFontIconDataConverter; real mapping is Phase 6 -->
                        <FontIcon Glyph="&#xE897;" />
                    </ui:SettingsCard.HeaderIcon>
                    <StackPanel MinWidth="320">
                        <ItemsControl HorizontalAlignment="Right" ItemTemplate="{StaticResource BindingRowTemplate}" ItemsSource="{x:Bind Bindings, Mode=OneWay}" />
                        <StackPanel Margin="0,4,0,0" HorizontalAlignment="Right" Orientation="Horizontal">
                            <Button Width="40" Height="28" Click="OnAddShortcutClick" ToolTipService.ToolTip="Add shortcut">
                                <!-- TODO(icon): originally iNKORE FluentSystemIcons.Add_32_Filled; real mapping is Phase 6 -->
                                <FontIcon Glyph="&#xE897;" />
                            </Button>
                        </StackPanel>
                    </StackPanel>
                </ui:SettingsCard>
            </DataTemplate>
        </ResourceDictionary>
    </Page.Resources>

    <ScrollViewer VerticalScrollBarVisibility="Auto">
        <StackPanel Margin="20" VerticalAlignment="Top">
            <TextBlock FontSize="24" Foreground="White" Text="Shortcuts" />
            <StackPanel>
                <ItemsControl ItemsSource="{x:Bind ViewModel.Groups, Mode=OneWay}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate x:DataType="settingsvm:ShortcutGroupViewModel">
                            <StackPanel Margin="0,12,0,0">
                                <TextBlock Margin="0,0,0,4" FontSize="16" FontWeight="SemiBold" Text="{x:Bind Name, Mode=OneWay}" />
                                <ItemsControl ItemTemplate="{StaticResource CommandRowTemplate}" ItemsSource="{x:Bind Commands, Mode=OneWay}" />
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>

                <Button
                    Margin="0,20,0,0"
                    HorizontalAlignment="Left"
                    Command="{x:Bind ViewModel.ResetAllCommand}"
                    Content="Reset all shortcuts to defaults" />
            </StackPanel>
        </StackPanel>
    </ScrollViewer>
</rxwpf:ReactivePage>
```

**TODO(verify):** the `ResourceDictionary Source="ms-appx:///WinTabber.UI.Common/Themes/Generic.xaml"` URI
above assumes WinUI 3's `ms-appx:///<AssemblyName>/<Path>` cross-assembly resource URI scheme resolves the
same way WPF's `pack://`-style `/WinTabber.UI.Common;component/...` did — confirm this against a real build
(a missing-resource exception at runtime, not a compile error, is the likely failure mode if the URI scheme
is wrong) before trusting it; if it does not resolve, the fallback is merging `Generic.xaml`'s content
directly into this page's own `ResourceDictionary.MergedDictionaries` via a `ResourceDictionary` with no
`Source` and instead an in-app-relative path, or (more idiomatically for WinUI 3) confirming whether
`Themes/Generic.xaml` in a class library is already picked up automatically as that library's own default
style dictionary without needing an explicit merge from a consumer at all — this is a real, common WinUI 3
pattern (automatic per-assembly `Generic.xaml` lookup, the same mechanism that makes `ShortcutPresenter`'s
own default style resolve today without WinUI 3 asset any explicit `Generic.xaml` merge in that project's
own test/host app) that may make this whole `MergedDictionaries` entry unnecessary for the *default*
styles, while `ShortcutEditButtonStyle` (an ordinary keyed `Button` style, not a default `TargetType`
style) still needs it. Resolve this against real build/runtime behavior, not this plan's guess.

- [ ] **Step 2: Port `ShortcutsSettingsPage.xaml.cs`**

```csharp
// winui3/WinTabberUI/Views/ShortcutsSettingsPage.xaml.cs
using Microsoft.UI.Xaml;
using ReactiveUI;
using WinTabber.ViewModels.Settings;

namespace WinTabberUI.Views;

public sealed partial class ShortcutsSettingsPage : ReactivePage<ShortcutsSettingsViewModel>, IViewFor<ShortcutsSettingsViewModel>
{
    public ShortcutsSettingsPage()
    {
        InitializeComponent();
    }

    private async void OnEditShortcutClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ShortcutBindingViewModel binding } || ViewModel is null)
        {
            return;
        }

        var dialog = new ShortcutCaptureDialog(
            binding.CommandDisplayName,
            binding.Trigger,
            ViewModel.TriggerSource,
            canDelete: true
        )
        {
            XamlRoot = XamlRoot,
        };
        dialog.ShowConflict(binding.ConflictMessage);
        dialog.TriggerCaptured += (_, trigger) =>
            dialog.ShowConflict(ViewModel.DescribeConflict(binding.Command, trigger, binding));

        await dialog.ShowAsync();

        switch (dialog.Result)
        {
            case ShortcutCaptureDialogResult.Saved when dialog.ResultTrigger is { } trigger:
                binding.Trigger = trigger;
                break;
            case ShortcutCaptureDialogResult.Deleted:
                binding.RemoveCommand.Execute().Subscribe();
                break;
            case ShortcutCaptureDialogResult.ResetToDefault:
                binding.ResetOwnerToDefault();
                break;
        }
    }

    private async void OnAddShortcutClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ShortcutCommandViewModel command } || ViewModel is null)
        {
            return;
        }

        var dialog = new ShortcutCaptureDialog(command.DisplayName, null, ViewModel.TriggerSource, canDelete: false)
        {
            XamlRoot = XamlRoot,
        };
        dialog.TriggerCaptured += (_, trigger) =>
            dialog.ShowConflict(ViewModel.DescribeConflict(command.Command, trigger, excluding: null));

        await dialog.ShowAsync();

        if (dialog.Result == ShortcutCaptureDialogResult.Saved && dialog.ResultTrigger is { } trigger)
        {
            command.AddFromDialog(trigger);
        }
    }
}
```

The `DataContextChanged`/`ViewModel = ...` wiring is dropped for the same reason as Task 3.2 —
`ReactivePage<T>` is expected to handle it; same `TODO(verify)` applies if it doesn't. The one WinUI
3-specific addition beyond a mechanical port: `dialog.XamlRoot = XamlRoot` — a `ContentDialog` in WinUI 3
throws at `ShowAsync()` if its `XamlRoot` isn't set (this repo's own migration skill flags this exact
pitfall), unlike WPF's `ContentDialog`-equivalent, which needed no such wiring.

- [ ] **Step 3: Port `ShortcutCaptureDialog.xaml`**

```xml
<!-- winui3/WinTabberUI/Views/ShortcutCaptureDialog.xaml -->
<ContentDialog
    x:Class="WinTabberUI.Views.ShortcutCaptureDialog"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:controls="using:WinTabber.UI.Common.Controls"
    CornerRadius="8"
>
    <ContentDialog.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <!-- Same ms-appx:/// resolution question as Task 3.1's Step 1 — see that step's
                     TODO(verify) note; ShortcutChipTemplateLarge/ShortcutCaptureBoxDialogStyle are
                     keyed resources, so at minimum this explicit merge is needed for those two,
                     whatever the answer turns out to be for the default styles. -->
                <ResourceDictionary Source="ms-appx:///WinTabber.UI.Common/Themes/Generic.xaml" />
            </ResourceDictionary.MergedDictionaries>
            <Thickness x:Key="ContentDialogPadding">0</Thickness>
            <Thickness x:Key="ContentDialogTitleMargin">0</Thickness>
        </ResourceDictionary>
    </ContentDialog.Resources>

    <Grid MinWidth="440">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <!-- Title, instructions, and the keys being pressed. -->
        <StackPanel Grid.Row="0" Margin="26,20,26,20">
            <TextBlock x:Name="TitleText" FontSize="20" FontWeight="SemiBold" />
            <TextBlock
                Margin="0,4,0,0"
                FontSize="13"
                Foreground="{ThemeResource ShortcutHintForegroundBrush}"
                Text="Press the keys for this shortcut." />

            <Border MinHeight="90" Margin="0,20,0,0" HorizontalAlignment="Center" VerticalAlignment="Center">
                <controls:ShortcutCaptureBox x:Name="CaptureBox" Style="{StaticResource ShortcutCaptureBoxDialogStyle}" />
            </Border>
        </StackPanel>

        <!-- Conflict warning — informational only; a conflicting shortcut can still be saved. -->
        <Border Grid.Row="1" Margin="26,0,26,16" HorizontalAlignment="Stretch">
            <Border
                x:Name="ConflictBanner"
                Padding="14,10"
                Background="{ThemeResource ShortcutValidationBannerBackgroundBrush}"
                BorderBrush="{ThemeResource ShortcutValidationForegroundBrush}"
                BorderThickness="1"
                CornerRadius="5"
                Visibility="Collapsed">
                <TextBlock x:Name="ConflictText" Foreground="{ThemeResource ShortcutValidationForegroundBrush}" TextWrapping="Wrap" />
            </Border>
        </Border>

        <!-- Save / Reset / Delete / Cancel — the footer Flow.Launcher's hotkey dialog uses, minus
             its "Overwrite" button: WinTabber never blocks a conflicting save. -->
        <Border Grid.Row="2" BorderBrush="{ThemeResource ShortcutCaptureBoxBorderBrush}" BorderThickness="0,1,0,0" CornerRadius="0,0,8,8">
            <StackPanel Margin="10,9" HorizontalAlignment="Center" Orientation="Horizontal">
                <Button x:Name="SaveButton" MinWidth="100" MinHeight="36" Margin="0,0,4,0" Click="OnSaveClick" Content="Save" Style="{StaticResource AccentButtonStyle}" />
                <Button x:Name="ResetButton" MinWidth="100" MinHeight="36" Margin="4,0" Click="OnResetClick" Content="Reset to default" />
                <Button x:Name="DeleteButton" MinWidth="100" MinHeight="36" Margin="4,0" Click="OnDeleteClick" Content="Delete" />
                <Button MinWidth="100" MinHeight="36" Margin="4,0,0,0" Click="OnCancelClick" Content="Cancel" />
            </StackPanel>
        </Border>
    </Grid>
</ContentDialog>
```

**TODO(verify):** `ShortcutValidationBannerBackgroundBrush` is referenced here but was not confirmed to
exist among the brushes Task 2b.4 actually defined in `winui3/WinTabber.UI.Common/Themes/Generic.xaml`
(that task's brush list was: `ShortcutChipForegroundBrush`, `ShortcutCaptureBoxBorderBrush`,
`ShortcutHintForegroundBrush`, `ShortcutEmptyForegroundBrush`, `ShortcutValidationForegroundBrush`,
`ShortcutGroupBackgroundBrush`, `ShortcutGroupHoverBackgroundBrush`, `ShortcutGroupBorderBrush`,
`ShortcutChipAccentBrush` — no `...BannerBackgroundBrush`). Check `Generic.xaml` directly before this
compiles; if the brush is missing, add it there (a semi-transparent variant of
`ShortcutValidationForegroundBrush`'s color is the WPF original's apparent intent) rather than inventing a
hardcoded color inline.

`AccentButtonStyle` is a native WinUI 3 style key (ships via `XamlControlsResources`, already the first
merged dictionary in `winui3/WinTabberUI/App.xaml`) — no port needed, it already exists.

- [ ] **Step 4: Port `ShortcutCaptureDialog.xaml.cs`**

```csharp
// winui3/WinTabberUI/Views/ShortcutCaptureDialog.xaml.cs
using Microsoft.UI.Xaml;
using WinTabber.Events.Shortcuts;
using WinTabber.Events.Shortcuts.Detection;

namespace WinTabberUI.Views;

public enum ShortcutCaptureDialogResult
{
    Cancelled,
    Saved,
    Deleted,
    ResetToDefault,
}

public sealed partial class ShortcutCaptureDialog : ContentDialog
{
    public ShortcutCaptureDialog(
        string title,
        ShortcutTrigger? initialTrigger,
        IShortcutTriggerSource triggerSource,
        bool canDelete
    )
    {
        InitializeComponent();

        TitleText.Text = title;
        ResultTrigger = initialTrigger;
        DeleteButton.Visibility = canDelete ? Visibility.Visible : Visibility.Collapsed;
        ResetButton.Visibility = canDelete ? Visibility.Visible : Visibility.Collapsed;
        SaveButton.IsEnabled = initialTrigger is not null;

        CaptureBox.TriggerSource = triggerSource;
        CaptureBox.Trigger = initialTrigger;
        CaptureBox.Captured += (_, trigger) =>
        {
            ResultTrigger = trigger;
            SaveButton.IsEnabled = true;
            TriggerCaptured?.Invoke(this, trigger);
        };

        // ShortcutCaptureBox normally starts capturing off its own Visibility toggle (see
        // ShortcutCaptureBox.cs's RegisterPropertyChangedCallback on VisibilityProperty) — inside a
        // ContentDialog that never fires reliably, since the control can already report Visible
        // before the dialog itself actually opens. Start explicitly once the dialog has opened, and
        // stop once it closes regardless of how it closed, same as the WPF original's Opened/Closed
        // wiring, just under WinUI 3's own ContentDialog event names.
        Opened += (_, _) => CaptureBox.StartCapture();
        Closed += (_, _) => CaptureBox.CancelCapture();
    }

    public event EventHandler<ShortcutTrigger>? TriggerCaptured;

    public ShortcutCaptureDialogResult Result { get; private set; } = ShortcutCaptureDialogResult.Cancelled;

    public ShortcutTrigger? ResultTrigger { get; private set; }

    public void ShowConflict(string? message)
    {
        ConflictBanner.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
        ConflictText.Text = message;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (ResultTrigger is null)
        {
            return;
        }

        Result = ShortcutCaptureDialogResult.Saved;
        Hide();
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        Result = ShortcutCaptureDialogResult.ResetToDefault;
        Hide();
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        Result = ShortcutCaptureDialogResult.Deleted;
        Hide();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        Result = ShortcutCaptureDialogResult.Cancelled;
        CaptureBox.CancelCapture();
        Hide();
    }
}
```

The WPF original's `Opened`/`Closed` handlers wrote a debug line to
`%TEMP%\shortcut-capture-debug.log` (a leftover diagnostic, per that file's own code comment about a
focus-timing bug). Not ported: Phase 2b's final review already removed the equivalent debug logging from
`ShortcutCaptureBox.cs` itself as dead scaffolding, and carrying the same pattern into this new file would
reintroduce exactly what that fix wave removed.

- [ ] **Step 5: Build**

Run: `dotnet build WinTabber.slnx`
Expected: does not build clean until this task's `TODO(verify)` items (the `ms-appx:///` resource URI, the
missing `ShortcutValidationBannerBackgroundBrush`, and whatever `ReactivePage<T>`/`ViewModel` question Task
3.2 already surfaced) are resolved against real compiler/runtime behavior. Iterate until clean.

- [ ] **Step 6: Commit**

```bash
git add winui3/WinTabberUI/Views/ShortcutsSettingsPage.xaml winui3/WinTabberUI/Views/ShortcutsSettingsPage.xaml.cs \
  winui3/WinTabberUI/Views/ShortcutCaptureDialog.xaml winui3/WinTabberUI/Views/ShortcutCaptureDialog.xaml.cs
git commit -m "feat: port ShortcutsSettingsPage and ShortcutCaptureDialog to WinUI3

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

### Task 3.5: Port `SettingsWindow` and wire it as the winui3 shell's entry point

**Files:**
- Create: `winui3/WinTabberUI/Views/SettingsWindow.xaml`
- Create: `winui3/WinTabberUI/Views/SettingsWindow.xaml.cs`
- Create: `winui3/WinTabberUI/Views/SettingsPageTemplateSelector.cs`
- Create: `winui3/WinTabberUI/ValueConverters/IconKeyToGlyphConverter.cs` (winui3-side placeholder mapping, per Global Constraints — every `IconKey` maps to the same placeholder glyph until Phase 6)
- Modify: `winui3/WinTabberUI/App.xaml` (add `WinUIEx` package's needs, if any beyond what's already referenced; add `XamlControlsResources` as first merged dictionary if not already present — confirm against the current file before assuming)
- Modify: `winui3/WinTabberUI/App.xaml.cs` (Task 3.1 already changed this to construct `SettingsWindow` directly; no further change expected here unless Step 1 below reveals otherwise)
- Modify: `winui3/WinTabberUI/WinTabberUI.csproj` (add `<ProjectReference>` to `winui3/WinTabber.UI.Common` — confirm it is not already there from Task 1.3's original scaffold, since that csproj's `ItemGroup` already listed one)

**Interfaces:**
- Consumes: `WinTabber.ViewModels.SettingsViewModel` (Task 3.1's DI-constructed instance), `GeneralSettingsPage`/`AppearanceSettingsPage`/`ShortcutsSettingsPage` (Tasks 3.2-3.4).
- Produces: `WinTabberUI.Views.SettingsPageTemplateSelector : Microsoft.UI.Xaml.Controls.DataTemplateSelector`, the native WinUI 3 replacement for WPF's implicit `DataTemplate`-by-`DataType` selection inside a `Frame`/`ContentControl` (see this phase's opening note).

`WindowEx` (WinUIEx) replaces the bare `Window`/`ui:WindowHelper.UseModernWindowStyle`/`CornerStyle`/
`SystemBackdropType="Mica"` combination — per the design spec's per-window backdrop table, `SettingsWindow`
gets `MicaBackdrop`. `ui:NavigationView` → native `NavigationView`; `ui:FontIcon` → native `FontIcon`.
`SegoeFluentIcons.Home`/`.OEM`/`.Game` (the three static top-level nav items — Home/Apps/Games, which this
plan's earlier XAML read confirms are unrelated to the dynamic `Sections`-driven items and were explicitly
called "untouched" in the design spec's icon note) get the same deferred-glyph placeholder treatment as
every other iNKORE icon key in this phase.

- [ ] **Step 1: Confirm current `App.xaml`'s resource setup and `WinTabberUI.csproj`'s `WinTabber.UI.Common` reference**

Run: `cat winui3/WinTabberUI/App.xaml` and `cat winui3/WinTabberUI/WinTabberUI.csproj`. Task 1.3's original
scaffold already added `<XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls" />` as the first
merged dictionary and a `ProjectReference` to `winui3/WinTabber.UI.Common` — if both are already present
(expected), no changes are needed to either file in this step; if either is missing (e.g. removed or never
landed as the plan originally specified), add it before proceeding.

- [ ] **Step 2: Write the `DataTemplateSelector`**

```csharp
// winui3/WinTabberUI/Views/SettingsPageTemplateSelector.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinTabber.ViewModels.Settings;

namespace WinTabberUI.Views;

/// <summary>
/// The native WinUI 3 replacement for WPF's implicit Frame/ContentControl DataTemplate-by-DataType
/// selection: WinUI 3's Frame is real page navigation with no equivalent, so SettingsWindow uses a
/// plain ContentControl with this selector instead.
/// </summary>
public sealed class SettingsPageTemplateSelector : DataTemplateSelector
{
    public required DataTemplate AppearanceTemplate { get; set; }
    public required DataTemplate GeneralTemplate { get; set; }
    public required DataTemplate ShortcutsTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(object item)
    {
        return item switch
        {
            AppearanceSettingsViewModel => AppearanceTemplate,
            GeneralSettingsViewModel => GeneralTemplate,
            ShortcutsSettingsViewModel => ShortcutsTemplate,
            _ => throw new ArgumentOutOfRangeException(nameof(item), item, "No template registered for this settings section."),
        };
    }
}
```

**TODO(verify):** `DataTemplateSelector.SelectTemplateCore(object)` (single-parameter overload) is the
signature this plan assumes based on WPF's own `DataTemplateSelector.SelectTemplate(object, DependencyObject)`
precedent adapted to WinUI 3's simpler API surface — confirm the exact virtual method WinUI 3's
`Microsoft.UI.Xaml.Controls.DataTemplateSelector` actually requires overriding (it may be a two-parameter
`SelectTemplateCore(object, DependencyObject)` instead, mirroring WPF more closely) against real compiler
output before trusting this signature.

- [ ] **Step 3: Write the placeholder icon converter**

```csharp
// winui3/WinTabberUI/ValueConverters/IconKeyToGlyphConverter.cs
using Microsoft.UI.Xaml.Data;
using WinTabber.Infrastructure;

namespace WinTabberUI.ValueConverters;

/// <summary>
/// Placeholder per this plan's Global Constraints: every IconKey maps to the same "Help" glyph until
/// Phase 6 does the real IconKey-to-WinUI-glyph mapping. Do not add real per-key glyphs here before
/// Phase 6 — that is the one phase this plan allows to replace this file's placeholder behavior.
/// </summary>
public sealed class IconKeyToGlyphConverter : IValueConverter
{
    // TODO(icon): placeholder for every IconKey; Phase 6 replaces this with real per-key glyphs.
    private const string PlaceholderGlyph = "";

    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is IconKey ? PlaceholderGlyph : PlaceholderGlyph;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
```

- [ ] **Step 4: Port `SettingsWindow.xaml`**

```xml
<!-- winui3/WinTabberUI/Views/SettingsWindow.xaml -->
<winuiex:WindowEx
    x:Class="WinTabberUI.Views.SettingsWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:winuiex="using:WinUIEx"
    xmlns:settingsvm="using:WinTabber.ViewModels.Settings"
    xmlns:views="using:WinTabberUI.Views"
    xmlns:conv="using:WinTabberUI.ValueConverters"
    Title="Settings"
    Width="1200"
    Height="1000"
    SystemBackdrop="{winuiex:MicaBackdrop}"
>
    <winuiex:WindowEx.Resources>
        <conv:IconKeyToGlyphConverter x:Key="IconKeyToGlyphConverter" />
        <DataTemplate x:Key="GeneralTemplate" x:DataType="settingsvm:GeneralSettingsViewModel">
            <views:GeneralSettingsPage />
        </DataTemplate>
        <DataTemplate x:Key="AppearanceTemplate" x:DataType="settingsvm:AppearanceSettingsViewModel">
            <views:AppearanceSettingsPage />
        </DataTemplate>
        <DataTemplate x:Key="ShortcutsTemplate" x:DataType="settingsvm:ShortcutsSettingsViewModel">
            <views:ShortcutsSettingsPage />
        </DataTemplate>
        <views:SettingsPageTemplateSelector
            x:Key="SettingsPageTemplateSelector"
            AppearanceTemplate="{StaticResource AppearanceTemplate}"
            GeneralTemplate="{StaticResource GeneralTemplate}"
            ShortcutsTemplate="{StaticResource ShortcutsTemplate}" />
    </winuiex:WindowEx.Resources>

    <NavigationView
        x:Name="SettingsNavigationView"
        IsBackButtonVisible="Collapsed"
        MenuItemsSource="{x:Bind ViewModel.Sections}"
        OpenPaneLength="240"
        PaneDisplayMode="Left"
        SelectionChanged="OnNavigationSelectionChanged">
        <NavigationView.MenuItemTemplate>
            <DataTemplate x:DataType="settingsvm:SettingsViewModelBase">
                <NavigationViewItem Content="{x:Bind Name}">
                    <NavigationViewItem.Icon>
                        <FontIcon Glyph="{x:Bind Icon, Converter={StaticResource IconKeyToGlyphConverter}}" />
                    </NavigationViewItem.Icon>
                </NavigationViewItem>
            </DataTemplate>
        </NavigationView.MenuItemTemplate>
        <NavigationView.MenuItems>
            <NavigationViewItem Content="Home">
                <NavigationViewItem.Icon>
                    <!-- TODO(icon): originally iNKORE SegoeFluentIcons.Home -->
                    <FontIcon Glyph="&#xE897;" />
                </NavigationViewItem.Icon>
            </NavigationViewItem>
            <NavigationViewItem Content="Apps">
                <NavigationViewItem.Icon>
                    <!-- TODO(icon): originally iNKORE SegoeFluentIcons.OEM -->
                    <FontIcon Glyph="&#xE897;" />
                </NavigationViewItem.Icon>
            </NavigationViewItem>
            <NavigationViewItem Content="Games">
                <NavigationViewItem.Icon>
                    <!-- TODO(icon): originally iNKORE SegoeFluentIcons.Game -->
                    <FontIcon Glyph="&#xE897;" />
                </NavigationViewItem.Icon>
            </NavigationViewItem>
        </NavigationView.MenuItems>

        <ContentControl
            x:Name="SettingsContent"
            HorizontalAlignment="Stretch"
            VerticalAlignment="Stretch"
            HorizontalContentAlignment="Stretch"
            VerticalContentAlignment="Stretch"
            Content="{x:Bind ViewModel.SelectedView, Mode=OneWay}"
            ContentTemplateSelector="{StaticResource SettingsPageTemplateSelector}" />
    </NavigationView>
</winuiex:WindowEx>
```

**TODO(verify):** `SystemBackdrop="{winuiex:MicaBackdrop}"` assumes `WinUIEx.WindowEx` exposes a XAML
markup extension for its `SystemBackdrop` property with this exact name — this plan was written without
access to browse the installed `WinUIEx` package's exact markup-extension surface. If `{winuiex:MicaBackdrop}`
does not resolve, the fallback is setting it in code-behind instead: `SystemBackdrop = new MicaBackdrop();`
(from `Microsoft.UI.Xaml.Media`) inside the constructor, which is guaranteed to work regardless of what
`WindowEx` does or doesn't expose as a markup extension, since `SystemBackdrop` is a plain settable
property either way.

`NavigationView`'s `SelectedItem` two-way binding from the WPF original (`SelectedItem="{Binding
Mode=TwoWay, Path=SelectedView}"`) is replaced with a `SelectionChanged` event handler (Step 5) rather
than attempted as `x:Bind Mode=TwoWay` — `NavigationView.SelectedItem`'s change-notification behavior
under `x:Bind` two-way was not confirmed against real compiler/runtime behavior while writing this plan,
and the event-handler approach is the standard, unambiguous WinUI 3 `NavigationView` pattern regardless,
so it is used here rather than risking a binding that silently doesn't sync.

- [ ] **Step 5: Port `SettingsWindow.xaml.cs`**

```csharp
// winui3/WinTabberUI/Views/SettingsWindow.xaml.cs
using Microsoft.UI.Xaml.Controls;
using WinTabber.ViewModels;
using WinTabber.ViewModels.Settings;
using WinUIEx;

namespace WinTabberUI.Views;

public sealed partial class SettingsWindow : WindowEx
{
    public SettingsViewModel ViewModel { get; }

    public SettingsWindow(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is SettingsViewModelBase section)
        {
            ViewModel.SelectedView = section;
        }
    }
}
```

The three static `NavigationViewItem`s (Home/Apps/Games) select `null` via this handler today (they are
not `SettingsViewModelBase` instances), which matches the WPF original's behavior exactly: those three
items were never wired to anything beyond being visible, decorative placeholders in the WPF app either —
confirmed by re-reading `SettingsWindow.xaml.cs`, which has no logic branching on them at all.

`ViewModel` here is a plain constructor-injected property, not `ReactiveWindow<T>`'s `ViewModel` — WinUI 3
`Window` (unlike `Page`/`UserControl`) is genuinely not a `DependencyObject` at all (per the migration
skill's own troubleshooting table), so `ReactiveWindow<T>`'s WPF-style `DependencyProperty`-backed
`ViewModel` pattern does not carry over the same way `ReactivePage<T>` does — a plain CLR property is the
correct, deliberate substitute here, not a shortcut.

- [ ] **Step 6: Update Task 3.1's `App.xaml.cs` reference**

`Task 3.1`'s `App.xaml.cs` already constructs `new SettingsWindow(Services.GetRequiredService<SettingsViewModel>())`
— confirm the `using WinTabberUI.Views;` in that file resolves now that `SettingsWindow` actually exists at
that namespace/path (it should, no change expected, but this step exists to catch it if Task 3.1's file
needs a namespace adjustment once this task's real `SettingsWindow.xaml.cs` supersedes the assumption that
task made about where it would live).

- [ ] **Step 7: Build and run**

Run: `dotnet build WinTabber.slnx` then `dotnet run --project winui3/WinTabberUI/WinTabberUI.csproj`
Expected: does not build clean on the first attempt — resolve every `TODO(verify)` flagged across all five
tasks in this phase against real compiler output, in whatever order the compiler surfaces them, then
re-build until clean. Once clean, the app should launch directly into `SettingsWindow` (per Task 3.1's
`App.xaml.cs`), showing the General page by default with Mica backdrop, and clicking between
General/Appearance/Shortcuts in the nav pane should swap the content pane. Manually verify: toggling a
`ToggleSwitch` persists (re-launch and confirm it stuck — `SettingsViewModel` saves on any section's
`Changed` observable firing); clicking a shortcut's edit button opens `ShortcutCaptureDialog` and pressing
a real key combination is captured and displayed live via `ShortcutPresenter`/`ShortcutCaptureBox` (this is
the actual, meaningful end-to-end proof that Phase 2b's controls work, not just that they compile).

- [ ] **Step 8: Commit**

```bash
git add winui3/WinTabberUI/Views/SettingsWindow.xaml winui3/WinTabberUI/Views/SettingsWindow.xaml.cs \
  winui3/WinTabberUI/Views/SettingsPageTemplateSelector.cs winui3/WinTabberUI/ValueConverters/IconKeyToGlyphConverter.cs \
  winui3/WinTabberUI/App.xaml winui3/WinTabberUI/App.xaml.cs winui3/WinTabberUI/WinTabberUI.csproj
git commit -m "feat: port SettingsWindow to WinUI3, launch it as the winui3 shell's entry point

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

**What Phase 3 honestly does not fully deliver:** six distinct `TODO(verify)` items are left for the
implementer to resolve against real compiler/runtime output rather than fabricated (the `ReactivePage<T>`/
`ViewModel` auto-wiring question, twice; the `ms-appx:///` cross-assembly resource URI scheme; the missing
`ShortcutValidationBannerBackgroundBrush`; `DataTemplateSelector`'s exact override signature; and
`WinUIEx`'s `{winuiex:MicaBackdrop}` markup extension, with a guaranteed-correct code-behind fallback
already given). All six are named, scoped narrowly, and have either a concrete fallback already written or
a clear "check `Generic.xaml`, add the brush" resolution path — none require new design work the way
Phase 2c's hint-overlay system does.

---

## Phase 2c onward — scope note

Phase 3 (`SettingsWindow`) is now written above and consumes the two "Phase 3
hazards" this note originally flagged (`ShortcutPresenter.Trigger`'s lost
`BindsTwoWayByDefault`, `RelayCommand.CanExecuteChanged`'s no-op) only
partially — Task 3.4's `ShortcutPresenter` usage is `Mode=OneWay` and never
hits the `TwoWay` hazard, and nothing in Phase 3 binds `StartCaptureCommand`/
`CancelCaptureCommand` directly (they're driven by `ShortcutCaptureBox`
internally, not from XAML), so both hazards remain live for whatever future
phase or feature first does bind them. This note's remaining content (the
hint-overlay deferral, the `OnApplyTemplate` rule, and the corrected
`VirtualKey` fallback scope) is unaffected by Phase 3 and stays as written.

**Deferred from Phase 2, needs design work before a task-by-task plan can be
written:**

- **The hint-overlay system** — `WinTabber.UI.Common/Behaviors/HintBehavior.cs`
  (755 lines) plus `IHintBehaviorKernel.cs`, `DefaultHintBehaviorKernel.cs`,
  `ItemsControlHintBehaviorKernel.cs`, `HintActivationScope.cs`,
  `HintAdorner.cs`, `HintPosition.cs`, and everything under `Hints/**` (7
  files, read enough to know they are the hint-text data model and were not
  read in full for this plan). This is not a mechanical port: `HintBehavior`
  derives from `Microsoft.Xaml.Behaviors.Behavior<FrameworkElement>` (needs
  `Microsoft.Xaml.Behaviors.WinUI.Managed`, per the migration skill's NuGet
  table), renders hints through WPF's `AdornerLayer`/`AdornerDecorator`
  (no WinUI 3 equivalent — needs a `Popup`- or `Canvas`-based overlay
  redesign), listens on `PreviewKeyDown` (a tunneling event WinUI 3 does not
  have — needs the bubbling `KeyDown` + `Handled`/`AddHandler` pattern), and
  activates elements through WPF UI Automation peer interfaces
  (`IInvokeProvider`, `IExpandCollapseProvider`, `IToggleProvider` via
  `FrameworkElementAutomationPeer`) whose WinUI 3 equivalents were not
  researched while writing this plan. `WinTabber.UI.Common.Tests/Behaviors/HintBehaviorTests.cs`
  and `HintBehaviorDesktopTests.cs` were read in full and confirm the same
  dependency (`AdornerDecorator`, `Window.Show()`) at the test level.
- **The shortcut-capture custom controls** are now planned as Phase 2b
  (above), not deferred — see that section for the full research and the
  `KeyInterop`/`VirtualKey` resolution.

Before writing Phase 2c, read `Hints/**` (all 7 files),
`WinTabber.UI.Common/HintAdorner.cs`, and
`WinTabber.UI.Common/Themes/Generic.xaml` in full (note: `Themes/Generic.xaml`
was read for Phase 2b's port of the shortcut-capture controls' templates —
re-reading it for the hint-overlay's own template needs, if any live in the
same file, should reuse that read rather than redo it), and research the
actual WinUI 3 replacement for `AdornerLayer`-based overlays and the UI
Automation invoke pattern before writing any task — the same "No
Placeholders" discipline that limited this pass to Phase 2b's mechanical
subset applies there too.

`WinTabberUI/Controls/SpatialNavigationListView.cs` and
`WindowThumbnail.cs` (corrected location, see the note at the top of Phase
2) belong to the window-conversion phases (3–4), since they live in the app
project, not `WinTabber.UI.Common` — read them when writing Phase 3/4, not
Phase 2c.

**Ported in Phase 2 but dead by design in the winui3 tree:**
`WindowStateToVisibilityConverter` (Task 2.1) and `WindowCommands`/
`MinimizeCommand`/`RestoreMaximizeCommand` (Task 2.2) were ported for
structural parity, following this plan's Task 0.2 precedent for
`WindowRenameViewModel`. Unlike that precedent, though, these have no live
consumer in the winui3 tree by design: the converter's only WPF consumer is
`WinTabber.UI.Common/Chrome/CaptionButtons.xaml` (its `RestoreButtonVisibilityConverter`/
`MaximizeButtonVisibilityConverter` resources), and the design spec deletes
the entire `Chrome/` folder — `CaptionButtons` named explicitly — because
`SystemBackdrop` and the default WinUI 3 window frame replace it; the
`WindowCommands` set was already dead code in WPF per Task 2.2's own grep,
and is doubly obsolete once the default window frame provides minimize/
maximize natively.

Controller's ruling: keep this ported code as-is for now rather than
deleting it or adding speculative tests — `WindowRenameViewModel` is
plausibly reusable, but these artifacts' only consumer is permanently gone
by design, so there is nothing to test against. Flag them for removal
alongside `Chrome/CaptionButtons` when that deletion actually happens in
the window-conversion phases (3–4).

Also note for any future consumer: `OverlappedPresenterState` (the WinUI 3
analog these artifacts use for window state) has no direct binding source
the way WPF's `Window.WindowState` `DependencyProperty` did —
`AppWindow.Presenter.State` is a plain property with no change notification
a XAML binding could observe directly. A future consumer of
`WindowStateToVisibilityConverter` needs a ViewModel-level property that
mirrors this state (e.g. by hooking `AppWindow.Changed`'s
`DidSizeChange`), not a direct binding to the presenter.

Phases 3 through 7 (converting all six windows, tray icon and bootstrap
parity, the icon-mapping pass, and final verification) remain **not detailed
task-by-task in this document**, for the same reason as before: producing
real, verified code for them requires reading files not yet read. The design
spec (`docs/superpowers/specs/2026-09-12-wpf-to-winui3-migration-design.md`)
already fixes the phase boundaries, the backdrop-per-window table, and the
control-mapping rules — that scoping work does not need to be redone, only
the file-level task breakdown for Phase 2b and Phases 3–7.

**Architecture gap found during final review, unaddressed by Phases 0–2:**
`WinTabber.Api.Media` and `WinTabber.Infrastructure` are not actually fully
UI-framework-agnostic, despite this plan's Architecture section describing
them that way. `WinTabber.Api.Media/ShellApplications/Models/InstalledApplicationInfo.cs:11`
declares `public required IObservable<ImageSource> Icon { get; init; }`
using WPF's `System.Windows.Media.ImageSource`, and
`WinTabber.Infrastructure/AppCache.cs` uses `System.Windows.Media.Imaging`
types directly, which is why `WinTabber.Infrastructure.csproj` still carries
`<UseWPF>true</UseWPF>`. Because `AggregateSession` (moved into
`WinTabber.ViewModels` by Task 1.2) exposes a public
`InstalledApplicationInfo App` property, a WPF type is reachable through
`WinTabber.ViewModels`'s public surface today — it only compiles clean
because `winui3/WinTabberUI` has no real code yet exercising that path.
Phase 3 (SettingsWindow) does not touch `WinTabber.Api.Media`, `AggregateSession`,
or `AppCache` at all — none of the Settings pages have any media dependency — so
this gap is confirmed still not applicable to close out there. **Phase 4 (the
media/overlay windows) should open with a task to de-WPF
`InstalledApplicationInfo.Icon`** (e.g. to `IObservable<Stream>` or a new
per-UI-framework `IIconSource` abstraction) and `AppCache`'s imaging code,
before any winui3 media-related conversion work begins. Still unaddressed as
of Phase 3.

**Findings from Phase 2b's final-review fix wave, relevant to Phase 3 onward:**

- **The `OnApplyTemplate` rule.** WinUI 3's `VisualStateManager` callbacks
  wired to `PropertyMetadata` change handlers only fire on a property
  *change*, not at template application — unlike WPF's declarative
  `Style.Triggers`/`DataTrigger`, which also matched at the property's
  default value. A control ported this way (as `ShortcutPresenter` and
  `ShortcutCaptureBox` were in Task 2b.4) needs an `OnApplyTemplate`
  override that calls `GoToState` once per `VisualStateGroup` at current
  property values, or a state that should be active by default (e.g. an
  "empty"/"idle" state whose driving property never changes from its
  default) never activates and the corresponding template part never shows.
  Found and fixed in Phase 2b; apply the same pattern to every future
  ported control that converts a WPF trigger to `VisualStateManager`. Also
  double-check, when porting such a template, that
  `VisualStateManager.VisualStateGroups` is attached to the control's
  actual template root (the top-level element returned by the
  `ControlTemplate`) — Phase 2b's own default `ShortcutCaptureBox` style
  initially had the groups attached to an inner `StackPanel` instead of the
  root `Border`, which silently no-ops every `GoToState` call regardless of
  whether `OnApplyTemplate` calls it correctly.
- **Two Phase 3 hazards, left unfixed for now since there's no live consumer
  to test against:** (a) `ShortcutPresenter.Trigger`'s WPF
  `BindsTwoWayByDefault` was lost in the port — WinUI 3's `{x:Bind}`
  defaults to `OneTime`, so a future consumer binding `Trigger` must use an
  explicit `Mode=TwoWay` or it will silently lose captured shortcuts; (b)
  `ShortcutCaptureBox`'s `RelayCommand`s (`StartCaptureCommand`/
  `CancelCaptureCommand`) have a no-op `CanExecuteChanged` (consistent with
  this plan's established precedent for WPF's `CommandManager.RequerySuggested`
  having no WinUI 3 equivalent), which means a future consumer that binds a
  button to either command will see it permanently disabled/enabled based
  only on its state at construction time — a real `RaiseCanExecuteChanged`
  wired to the relevant property-changed callbacks will be needed before
  either command is safely bindable.
- **The `VirtualKey` fallback's scope was corrected.** It was originally
  documented as covering "media, volume, browser keys" outside
  `ShortcutDisplayNames`'s canonical table; this is false for media and
  volume keys (VK 0xAD-0xB3), which are not defined members of
  `Windows.System.VirtualKey` at all (that enum stops at `GoHome = 0xAC`),
  so they fell through to a raw hex fallback (e.g. "0xB3"). `ShortcutChip.cs`
  now has an explicit lookup table for those seven keys, checked before the
  `VirtualKey` fallback; the doc comment and the `VirtualKey` fallback
  itself (still valid for keys like browser navigation, which genuinely are
  defined `VirtualKey` members) were corrected accordingly.

---

## Phase 4 — scope note (research only; no tasks written yet)

Phase 4 converts the five remaining windows: `WindowSelectorWindow`,
`ThumbnailWindow`, `DockWindow`, `SuspendedWindowsWindow`,
`MediaControlsWindow`. This pass read `WindowSelectorWindow.xaml(.cs)`,
`ThumbnailWindow.xaml(.cs)`, and `WinTabberUI/Controls/WindowThumbnail.cs` in
full, in that order, before stopping — deliberately, not from running out of
effort partway through a file. What was found in just those three files
means writing real, verified tasks for any of the five windows right now
would violate this plan's own "No Placeholders" discipline. This note
records the findings so the next pass does not have to re-derive them, and
explains exactly why the split happened where it did.

**The one question this whole migration hinged on — DWM thumbnail
compositing inside a WinUI 3 window — checks out.** `WindowThumbnail.cs`'s
actual registration code
(`PInvoke.DwmRegisterThumbnail(new HWND(_target.Handle), new HWND(source),
out _thumb)`) is plain `Windows.Win32.PInvoke` — the same CsWin32-generated
binding the WPF app already uses, with no WPF-specific type in the call
itself. The one WPF-specific piece is obtaining `_target.Handle`, done today
via `HwndSource.FromVisual(this)`; the direct WinUI 3 replacement is
`WinRT.Interop.WindowNative.GetWindowHandle(window)`, a real, standard API
for exactly this. The per-frame destination-rect recompute
(`Thumbnail_LayoutUpdated`) uses `TransformToAncestor` (WPF) and
`VisualTreeHelper.GetDpi` (WPF) — both have direct WinUI 3 equivalents
(`TransformToVisual`, `XamlRoot.RasterizationScale`), and neither touches the
DWM call itself, only the rectangle fed into it. Nothing found in this file
threatens the premise this migration was approved on. Confirming this in
code, not just by the earlier verbal validation, is the most important
outcome of this pass.

**What is NOT yet resolved, and why full tasks were not written around it:**

- `ThumbnailWindow.xaml.cs` hooks `WM_NCHITTEST`, `WM_SIZING`, and
  `WM_EXITSIZEMOVE` via WPF's `HwndSource.AddHook(WndProc)` to implement a
  hand-tuned resize-grab hit-test region (`HitTestResizeBorder`), aspect-lock
  during drag (`LockAspectRatio`), and a post-drag real-window resize
  (`ApplyZoomFactor`). WinUI 3 has no direct equivalent of `HwndSource.AddHook`
  — the standard replacement is subclassing the window's `WndProc` via
  `SetWindowSubclass`/`SetWindowLongPtr(GWLP_WNDPROC)`, which is a real,
  documented pattern but was not researched in this pass, and CsWin32's
  metadata coverage for it was not checked. Writing `WM_NCHITTEST`
  hit-testing code against an unconfirmed subclassing mechanism would be
  exactly the kind of fabrication this plan's discipline exists to prevent.
- `WindowSelectorWindow.xaml.cs` derives from `ReactiveWindow<T>` (the same
  generic-XAML-root shape that broke `ReactivePage<T>` in Phase 3 — untested
  for `Window`, and likely moot anyway, since Task 3.5 already established
  that `Microsoft.UI.Xaml.Window` is not a `DependencyObject` at all, so
  `SettingsWindow`'s plain-`WindowEx`-with-a-CLR-`ViewModel`-property pattern
  is almost certainly the right target here too — but this needs confirming
  against `ReactiveUI.WinUI`'s actual `ReactiveWindow<T>` (if it exists) the
  same way Task 3.2 confirmed `ReactivePage<T>`'s real behavior, not assumed).
  It also has WPF-specific frame-composition timing logic
  (`CompositionTarget.Rendering`-gated `ArmReveal`/`RevealWhenComposed`) to
  avoid a stale-frame flicker on reuse, and DPI-aware screen-bounds
  resolution (`ToLogicalBounds`, an extension method not yet located) — both
  need their WinUI 3/`AppWindow` equivalents identified before real code can
  be written.
- Neither `DockWindow`, `SuspendedWindowsWindow`, nor `MediaControlsWindow`
  has been read at all yet. `MediaControlsWindow.xaml` was flagged elsewhere
  in this repo's history as large and complex; it should not be assumed
  simpler than `ThumbnailWindow` just because it hasn't been opened.

**Recommendation for whoever picks this up next:** research and plan one
window at a time, the same way Phase 2 split into 2 (mechanical) / 2b
(shortcut-capture, needed real design work) / 2c (deferred, hint-overlay).
A plausible order, easiest-to-hardest: `DockWindow` and
`SuspendedWindowsWindow` first (unread, but by their names and role likely
closer to `SettingsWindow`'s shape than to `ThumbnailWindow`'s), then
`WindowSelectorWindow` (complex but no undiscovered Win32 subclassing
question), then `ThumbnailWindow` last (blocked on resolving the
`WM_NCHITTEST` subclassing mechanism), with `MediaControlsWindow` read and
sized up before deciding where it lands in that order.

**Verification bar for Phase 4, raised by what Phase 3's final review
found:** a build succeeding and a process staying alive proved nothing about
whether Phase 3's settings pages actually rendered — the real defect
(`DataTemplateSelector`'s wrong override never being called) was silent at
both compile and runtime until someone actually walked the live UI
Automation tree. Every Phase 4 task's own verification step must specify
concrete, checkable evidence that the window's real controls render and its
real interactive behavior works (a UI Automation tree walk confirming actual
control types, `SelectionItemPattern`/`InvokePattern` exercised against
real controls, or equivalent), not process liveness or a window title alone.
This applies with extra force to `ThumbnailWindow` and `WindowSelectorWindow`
specifically, since both have custom hit-testing whose only failure mode
(clicks landing in the wrong place, or not registering at all) is invisible
to both a build and a bare process-alive check.

**Update: Phase 4a (below) is now planned**, covering `DockWindow` and
`SuspendedWindowsWindow` per this note's own recommended order. The
remaining three windows (`WindowSelectorWindow`, `ThumbnailWindow`,
`MediaControlsWindow`) are still only scoped, not planned — the next
research pass should pick up `WindowSelectorWindow` next, per the ordering
above.

---

## Phase 4a — Convert `DockWindow` and `SuspendedWindowsWindow`

All files below were read in full before writing this phase:
`WinTabberUI/Views/DockWindow.xaml(.cs)`, `SuspendedWindowsWindow.xaml(.cs)`,
`WinTabberUI/Controls/WindowThumbnail.cs`, `WinTabberUI/Windowing/DesktopHelper.cs`,
`WinTabber.ViewModels/DockWindowViewModel.cs`, `SuspendedWindowsViewModel.cs`,
`SuspendedWindowItemViewModel.cs`, `WindowItem.cs` (all already WPF-free, no
changes needed), and `WinTabberUI/Bootstrapper.cs`'s registrations for
`WindowManager`/`IProcessSuspensionService`/`IWindowThumbnailService` and
their own transitive dependencies.

**Both windows are plain `Window`, not `ReactiveWindow<T>`.** Neither hits
the generic-XAML-root bug — `SettingsWindow`'s plain-`WindowEx`-with-a-CLR-
`ViewModel`-property pattern (Task 3.5) is the template for both, not the
`*PageBase` workaround.

**`DockWindow` embeds `WindowThumbnail` directly** (`<local:WindowThumbnail
Source="{Binding Path=Handle}" />` inside its `ItemTemplate`), so this
phase cannot deliver a working `DockWindow` without also porting
`WindowThumbnail` — even though `WindowThumbnail` was flagged in the Phase 4
scope note above as reserved for later, that flag was about `ThumbnailWindow`
needing it for `WM_NCHITTEST` hit-testing, a separate, harder problem. The
control itself (DWM registration, per-frame rect updates) has no dependency
on that hit-testing code and is fully portable now.

**`WindowThumbnail`'s two real design decisions, not mechanical renames:**

1. WPF's `HwndSource.FromVisual(this)` finds the owning window by walking up
   from any element in the visual tree — WinUI 3 has no equivalent (a
   `FrameworkElement` cannot discover its owning `Window` from the visual
   tree alone). Fix: add a settable `TargetWindow` property that the hosting
   window (`DockWindow`, later `ThumbnailWindow`) sets once, obtaining the
   HWND via `WinRT.Interop.WindowNative.GetWindowHandle(window)` itself,
   rather than trying to rediscover it per-instance the way WPF did.
2. WPF's `_target.RootVisual.IsAncestorOf(this)` (checked every
   `LayoutUpdated` tick, to detect the element leaving the visual tree) has
   no WinUI 3 equivalent — there is no ancestor-walk API on `UIElement`. Fix:
   track connection state via the element's own `Loaded`/`Unloaded` events
   instead of an ancestry check — functionally equivalent (both exist to
   answer "is this element still live"), simpler, and doesn't require an
   ancestor-walk API that doesn't exist. **TODO(verify) at runtime, not at
   compile time:** confirm `Unloaded` fires promptly enough when a
   `DockWindow` list item's container is virtualized away or removed — if
   there is a lag, thumbnails could briefly render at a stale position
   before `DwmUnregisterThumbnail` is called. Flag this in manual
   verification (Task 4a.4's Step 4), not something to block on now.

**`DesktopHelper.ToLogicalBounds` currently depends on
`iNKORE.UI.WPF.DragDrop.Utilities.DpiHelper`** — an iNKORE dependency this
whole migration exists to remove, confirming the plan's assumption that this
file was WPF-package-clean was wrong (same class of gap as
`WinTabber.Api.Media`/`Infrastructure`'s WPF-imaging leak, flagged in Phase 2's
scope note). Port drops the iNKORE call entirely in favor of
`PInvoke.GetDpiForWindow` (already a CsWin32 binding available via
`WinTabber.Interop`'s `NativeMethods.txt` coverage, or addable if not — a
plain, well-documented Win32 API, not iNKORE-specific).

### Task 4a.1: Extend `winui3/WinTabberUI`'s DI graph for the window/suspension/thumbnail services

**Files:**
- Modify: `winui3/WinTabberUI/Bootstrapper.cs`

**Interfaces:**
- Produces: `WindowManager`, `IProcessSuspensionService`, `IWindowThumbnailService` all resolvable from `App.Services`.
- Consumes: `WinTabber.Api.Windowing.WindowManager`, `WinTabber.Api.Windowing.Suspension.{IProcessSuspensionService, ProcessSuspensionService, ISuspensionStrategy, NtProcessSuspensionStrategy, ThreadSuspensionStrategy, ISuspendedWindowStore, SuspendedWindowFileStore}`, `WinTabber.Api.Windowing.Thumbnails.{IWindowThumbnailService, WindowThumbnailService}`, `WinTabber.Interop.IProcessRepository`/`ProcessRepository` — all already used by the WPF `Bootstrapper.cs`, mirror its registrations one-for-one, not `AppCache` or anything audio-related (neither `DockWindowViewModel` nor `SuspendedWindowsViewModel` needs them).

- [ ] **Step 1: Add the registrations**

```csharp
// winui3/WinTabberUI/Bootstrapper.cs — inside AddCoreServices, after the existing
// IWindowVisibility/InputListenerService registrations, mirroring WinTabberUI/Bootstrapper.cs
// (the WPF one) lines 94-101 exactly:
.AddSingleton<IProcessRepository, ProcessRepository>()
.AddSingleton<WindowManager>()
.AddSingleton<ISuspensionStrategy, NtProcessSuspensionStrategy>()
.AddSingleton<ISuspensionStrategy, ThreadSuspensionStrategy>()
.AddSingleton<ISuspendedWindowStore>(_ => new SuspendedWindowFileStore(Paths.SuspensionDirectory))
.AddSingleton<IProcessSuspensionService, ProcessSuspensionService>()
.AddSingleton<IWindowThumbnailService, WindowThumbnailService>()
```

Add the corresponding `using WinTabber.Api.Windowing;`, `using WinTabber.Api.Windowing.Suspension;`, `using WinTabber.Api.Windowing.Thumbnails;` to the top of the file — confirm `Paths.SuspensionDirectory`'s actual namespace via `grep -rn "class Paths" WinTabber.Interop WinTabber.Infrastructure` (not confirmed in this pass) and add that `using` too.

- [ ] **Step 2: Register the two ViewModels and add the DI-graph extension methods**

```csharp
// winui3/WinTabberUI/Bootstrapper.cs — new method, called from Init()
private static IServiceCollection AddDockAndSuspendedWindowsGraph(this IServiceCollection services)
{
    return services
        .AddSingleton<DockWindowViewModel>()
        .AddSingleton<SuspendedWindowsViewModel>();
}
```

```csharp
// Init() — add the new call:
public static ServiceProvider Init()
{
    return new ServiceCollection()
        .AddCoreServices()
        .AddSettingsGraph()
        .AddDockAndSuspendedWindowsGraph()
        .BuildServiceProvider();
}
```

Note: `DockWindowViewModel` is registered `AddSingleton`, not
`AddTransient`, deliberately diverging from the WPF app's registration
(unconfirmed in this pass — check `WinTabberUI/Bootstrapper.cs` for
`DockWindowViewModel`'s actual WPF lifetime before implementing this step,
and match it exactly rather than assuming singleton; `DockWindow` is shown
once per triggering application in the WPF app based on `ApplicationName`
being settable post-construction, which is more consistent with a
transient-per-show lifetime than a singleton — verify, don't guess).

- [ ] **Step 3: Build**

Run: `dotnet build WinTabber.slnx`
Expected: builds clean. This task adds no new UI, so no runtime/UI-Automation verification applies here — Task 4a.4/4a.5 will exercise this DI graph for real.

- [ ] **Step 4: Commit**

```bash
git add winui3/WinTabberUI/Bootstrapper.cs
git commit -m "feat: extend winui3 DI graph for window/suspension/thumbnail services

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

### Task 4a.2: Port `DesktopHelper`, dropping its iNKORE dependency

**Files:**
- Create: `winui3/WinTabberUI/Windowing/DesktopHelper.cs`

**Interfaces:**
- Produces: `WinTabberUI.Windowing.DesktopHelper` with `ToLogicalBounds(nint hwnd, System.Drawing.Rectangle deviceRect) : Windows.Foundation.Rect`, `GetDesktopArea() : Windows.Foundation.Rect`, `SetDesktopArea(Windows.Foundation.Rect rect)`.

Signature change from the WPF original: `ToLogicalBounds` becomes an
ordinary static method taking an `nint hwnd` instead of a `this Visual`
extension method — WinUI 3 has no `VisualTreeHelper.GetDpi(Visual)`
equivalent tied to an arbitrary element; DPI is queried per-window via
`GetDpiForWindow(HWND)`. Callers (Task 4a.5) pass their own HWND
(`WinRT.Interop.WindowNative.GetWindowHandle(this)`), obtained once, instead
of chaining off `this` as a `Visual`.

- [ ] **Step 1: Port, replacing the iNKORE DPI call**

```csharp
// winui3/WinTabberUI/Windowing/DesktopHelper.cs
using Windows.Foundation;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WinTabberUI.Windowing;

internal static class DesktopHelper
{
    /// <summary>
    /// Converts a device-pixel screen rectangle to WinUI 3 logical (effective-pixel) units using
    /// the DPI in effect for the window at <paramref name="hwnd"/> right now. Queried live rather
    /// than cached, so centering is always correct even if the window's own DPI bookkeeping is stale.
    /// </summary>
    public static Rect ToLogicalBounds(nint hwnd, System.Drawing.Rectangle deviceRect)
    {
        var dpi = PInvoke.GetDpiForWindow(new HWND(hwnd));
        var scale = dpi / 96.0;
        return new Rect(
            deviceRect.Left / scale,
            deviceRect.Top / scale,
            deviceRect.Width / scale,
            deviceRect.Height / scale);
    }

    public static unsafe Rect GetDesktopArea()
    {
        RECT area = new RECT();
        PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_GETWORKAREA, 0, &area, 0);
        return new Rect(area.X, area.Y, area.Width, area.Height);
    }

    public static unsafe void SetDesktopArea(Rect rect)
    {
        RECT area = new RECT((int)rect.Left, (int)rect.Top, (int)(rect.Left + rect.Width), (int)(rect.Top + rect.Height));
        PInvoke.SystemParametersInfo(SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETWORKAREA, 0, &area, 0);
    }
}
```

`GetDesktopArea`/`SetDesktopArea` are unchanged in substance from the WPF
original (plain `SPI_GETWORKAREA`/`SPI_SETWORKAREA` calls, never WPF-typed) —
only `ToLogicalBounds` actually needed a rewrite. **TODO(verify):** confirm
`PInvoke.GetDpiForWindow` is already covered by this project's CsWin32
`NativeMethods.txt` (it should be, transitively, given other DPI-aware calls
elsewhere in the interop layer — not confirmed in this pass); if the build
reports it missing, add `GetDpiForWindow` to
`winui3/WinTabberUI/NativeMethods.txt` (create the file if it doesn't exist
yet, following the pattern in `WinTabber.Interop/NativeMethods.txt`).

- [ ] **Step 2: Build**

Run: `dotnet build WinTabber.slnx`
Expected: builds clean.

- [ ] **Step 3: Commit**

```bash
git add winui3/WinTabberUI/Windowing/DesktopHelper.cs
git commit -m "feat: port DesktopHelper to WinUI3, dropping its iNKORE DPI dependency

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

### Task 4a.3: Port `WindowThumbnail`

**Files:**
- Create: `winui3/WinTabberUI/Controls/WindowThumbnail.cs`

**Interfaces:**
- Produces: `WinTabberUI.Controls.WindowThumbnail : Microsoft.UI.Xaml.FrameworkElement` with `Source` (`nint`, was `IntPtr` — same type, WinUI 3 convention prefers `nint`), `ClientAreaOnly` (`bool`), `Stretch` (`bool`), `TargetWindow` (`Microsoft.UI.Xaml.Window`, NEW — replaces WPF's auto-discovered `HwndSource`).

- [ ] **Step 1: Port the control**

```csharp
// winui3/WinTabberUI/Controls/WindowThumbnail.cs
using Microsoft.UI.Xaml;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using WinRT.Interop;

namespace WinTabberUI.Controls;

public class WindowThumbnail : FrameworkElement
{
    public WindowThumbnail()
    {
        if (!IsDwmEnabled)
            throw new NotSupportedException("Creating a window thumbnail is not supported when DWM is not enabled.");

        LayoutUpdated += Thumbnail_LayoutUpdated;
        Loaded += (_, _) => _isLoaded = true;
        Unloaded += (_, _) =>
        {
            _isLoaded = false;
            ReleaseThumbnail();
        };
    }

    private static bool IsDwmEnabled
    {
        get
        {
            PInvoke.DwmIsCompositionEnabled(out var enabled);
            return enabled;
        }
    }

    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(nint), typeof(WindowThumbnail),
        new PropertyMetadata((nint)0, (d, e) => ((WindowThumbnail)d).InitialiseThumbnail((nint)e.NewValue)));

    public static readonly DependencyProperty ClientAreaOnlyProperty = DependencyProperty.Register(
        nameof(ClientAreaOnly), typeof(bool), typeof(WindowThumbnail),
        new PropertyMetadata(false, (d, _) => ((WindowThumbnail)d).UpdateThumbnail()));

    // When true, always fills exactly the space it's given (DWM stretches the bitmap to match,
    // non-uniformly if the aspect ratio doesn't line up) instead of computing an aspect-preserving,
    // letterboxed size. Off by default so existing consumers (e.g. the selector tiles) keep their
    // current letterboxed behavior.
    public static readonly DependencyProperty StretchProperty = DependencyProperty.Register(
        nameof(Stretch), typeof(bool), typeof(WindowThumbnail), new PropertyMetadata(false));

    // Replaces WPF's HwndSource.FromVisual(this) auto-discovery, which has no WinUI 3 equivalent —
    // a FrameworkElement cannot discover its owning Window from the visual tree alone. The hosting
    // window (DockWindow, ThumbnailWindow) sets this once after it obtains its own HWND.
    public static readonly DependencyProperty TargetWindowProperty = DependencyProperty.Register(
        nameof(TargetWindow), typeof(Window), typeof(WindowThumbnail),
        new PropertyMetadata(null, (d, e) => ((WindowThumbnail)d).InitialiseThumbnail(((WindowThumbnail)d).Source)));

    public nint Source
    {
        get => (nint)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public bool ClientAreaOnly
    {
        get => (bool)GetValue(ClientAreaOnlyProperty);
        set => SetValue(ClientAreaOnlyProperty, value);
    }

    public bool Stretch
    {
        get => (bool)GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public Window? TargetWindow
    {
        get => (Window?)GetValue(TargetWindowProperty);
        set => SetValue(TargetWindowProperty, value);
    }

    private nint _targetHwnd;
    private nint _thumb;
    private bool _isLoaded;

    private void InitialiseThumbnail(nint source)
    {
        if (_thumb != 0)
        {
            ReleaseThumbnail();
        }

        if (source != 0 && TargetWindow is { } window)
        {
            _targetHwnd = WindowNative.GetWindowHandle(window);

            if (_targetHwnd != 0 && 0 == PInvoke.DwmRegisterThumbnail(new HWND(_targetHwnd), new HWND(source), out var thumb))
            {
                _thumb = thumb;
                var props = new DWM_THUMBNAIL_PROPERTIES
                {
                    fVisible = false,
                    fSourceClientAreaOnly = ClientAreaOnly,
                    opacity = 255,
                    dwFlags = PInvoke.DWM_TNP_VISIBLE | PInvoke.DWM_TNP_SOURCECLIENTAREAONLY | PInvoke.DWM_TNP_OPACITY,
                };
                PInvoke.DwmUpdateThumbnailProperties(_thumb, props);
            }
        }
    }

    private void ReleaseThumbnail()
    {
        if (_thumb != 0)
        {
            PInvoke.DwmUnregisterThumbnail(_thumb);
        }
        _thumb = 0;
        _targetHwnd = 0;
    }

    private void UpdateThumbnail()
    {
        if (_thumb != 0)
        {
            var props = new DWM_THUMBNAIL_PROPERTIES
            {
                fSourceClientAreaOnly = ClientAreaOnly,
                opacity = 255,
                dwFlags = PInvoke.DWM_TNP_SOURCECLIENTAREAONLY | PInvoke.DWM_TNP_OPACITY,
            };
            PInvoke.DwmUpdateThumbnailProperties(_thumb, props);
        }
    }

    // this is where the magic happens
    private void Thumbnail_LayoutUpdated(object? sender, object e)
    {
        if (_thumb == 0)
        {
            InitialiseThumbnail(Source);
        }

        if (_thumb != 0)
        {
            if (!_isLoaded || TargetWindow is not { } window)
            {
                ReleaseThumbnail();
                return;
            }

            var root = window.Content;
            if (root is null)
            {
                InvalidateArrange();
                return;
            }

            var transform = TransformToVisual(root);
            var a = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
            if (double.IsNaN(a.X))
            {
                InvalidateArrange();
            }
            else
            {
                var b = transform.TransformPoint(new Windows.Foundation.Point(ActualSize.X, ActualSize.Y));
                var scale = XamlRoot?.RasterizationScale ?? 1.0;

                var props = new DWM_THUMBNAIL_PROPERTIES
                {
                    fVisible = true,
                    rcDestination = new RECT
                    {
                        left = (int)Math.Ceiling(a.X * scale),
                        top = (int)Math.Ceiling(a.Y * scale),
                        right = (int)Math.Ceiling(b.X * scale),
                        bottom = (int)Math.Ceiling(b.Y * scale),
                    },
                    dwFlags = PInvoke.DWM_TNP_VISIBLE | PInvoke.DWM_TNP_RECTDESTINATION,
                };
                PInvoke.DwmUpdateThumbnailProperties(_thumb, props);
            }
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Stretch)
        {
            return new Size(
                double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
                double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
        }

        if (_thumb == 0)
        {
            return new Size(0, 0);
        }

        PInvoke.DwmQueryThumbnailSourceSize(_thumb, out var size);
        double scale = 1;
        if (size.Width > availableSize.Width) scale = availableSize.Width / size.Width;
        if (size.Height > availableSize.Height) scale = Math.Min(scale, availableSize.Height / size.Height);
        return new Size(size.Width * scale, size.Height * scale);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Stretch || _thumb == 0)
        {
            return finalSize;
        }

        PInvoke.DwmQueryThumbnailSourceSize(_thumb, out var size);
        double scale = finalSize.Width / size.Width;
        scale = Math.Min(scale, finalSize.Height / size.Height);
        return new Size(size.Width * scale, size.Height * scale);
    }
}
```

Two API-shape notes, both applied above, neither a guess: WinUI 3's
`FrameworkElement.LayoutUpdated` handler signature is
`(object? sender, object e)`, not WPF's `(object? sender, EventArgs e)` —
confirm this compiles as written; if the compiler reports a signature
mismatch, adjust the handler's second parameter type to whatever the real
event's delegate expects. `TransformToVisual(UIElement)` (not
`TransformToAncestor`) is WinUI 3's equivalent, returning a
`GeneralTransform` with `TransformPoint`, not `Transform`.

The opacity property (`WindowThumbnail.Opacity`, overridden in the WPF
original to trigger `UpdateThumbnail()` on change) is dropped in this port
— nothing in `DockWindow.xaml`'s usage sets it, and `FrameworkElement.Opacity`
already exists as a real WinUI 3 property with its own rendering behavior;
re-overriding it to also drive `DWM_TNP_OPACITY` is a nice-to-have this task
does not need. `opacity = 255` above is a hardcoded full-opacity default
matching the WPF original's actual runtime value for every current caller.

- [ ] **Step 2: Build**

Run: `dotnet build WinTabber.slnx`
Expected: builds clean, or names a specific unresolved member — resolve
against real compiler output per the two notes above.

- [ ] **Step 3: Commit**

```bash
git add winui3/WinTabberUI/Controls/WindowThumbnail.cs
git commit -m "feat: port WindowThumbnail to WinUI3

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

### Task 4a.4: Port `DockWindow`

**Files:**
- Create: `winui3/WinTabberUI/Views/DockWindow.xaml`
- Create: `winui3/WinTabberUI/Views/DockWindow.xaml.cs`
- Create: `winui3/WinTabberUI/Resources/DockWindowResources.xaml` (mirrors the WPF original's merged resource dictionary — read `WinTabberUI/Resources/DockWindowResources.xaml` before writing this file; not read while writing this plan)

**Interfaces:**
- Consumes: `WinTabber.ViewModels.DockWindowViewModel` (Task 4a.1), `WinTabberUI.Controls.WindowThumbnail` (Task 4a.3), `WinTabberUI.Windowing.DesktopHelper` (Task 4a.2), `WinTabber.Api.Windowing.WindowManager`.

`AcrylicChrome` (`ACCENT_ENABLE_BLURBEHIND`, `DWMWCP_ROUNDSMALL`) → `WindowEx`
+ `DesktopAcrylicBackdrop`, per the design spec's backdrop table and the
established `SettingsWindow`/Phase 2b pattern. `UnderStratumColor="#55ff0000"`
(a translucent red tint, likely a debug leftover given the color) is dropped —
`DesktopAcrylicBackdrop` has no tint-color property matching this; note the
drop in the commit message rather than silently losing it.

- [ ] **Step 1: Read the WPF resource dictionary**

Run: read `WinTabberUI/Resources/DockWindowResources.xaml` in full before writing Step 2 — its content was not read while writing this plan.

- [ ] **Step 2: Port `DockWindow.xaml`**

```xml
<!-- winui3/WinTabberUI/Views/DockWindow.xaml -->
<winuiex:WindowEx
    x:Class="WinTabberUI.DockWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:winuiex="using:WinUIEx"
    xmlns:local="using:WinTabberUI.Controls"
    Title="DockWindow"
    Width="200"
    IsTitleBarVisible="False"
    IsShownInSwitchers="False"
    IsResizable="False"
>
    <Grid Padding="10" Margin="10">
        <Grid.Resources>
            <ResourceDictionary>
                <ResourceDictionary.MergedDictionaries>
                    <!-- Port DockWindowResources.xaml's content here per Step 1's read, following
                         the migration skill's XAML syntax table for any DynamicResource/Style
                         conversions its content needs. -->
                    <ResourceDictionary Source="/Resources/DockWindowResources.xaml" />
                </ResourceDictionary.MergedDictionaries>
            </ResourceDictionary>
        </Grid.Resources>
        <ListView
            x:Name="WindowsList"
            ItemsSource="{x:Bind ViewModel.Windows, Mode=OneWay}"
            Background="Transparent"
            ScrollViewer.HorizontalScrollBarVisibility="Disabled"
            ScrollViewer.VerticalScrollBarVisibility="Hidden"
        >
            <ListView.ItemsPanel>
                <ItemsPanelTemplate>
                    <StackPanel Orientation="Vertical" />
                </ItemsPanelTemplate>
            </ListView.ItemsPanel>
            <ListView.ItemContainerStyle>
                <Style TargetType="ListViewItem">
                    <Setter Property="Margin" Value="0" />
                </Style>
            </ListView.ItemContainerStyle>
            <ListView.ItemTemplate>
                <DataTemplate x:DataType="vm:WindowItem">
                    <Grid Margin="0">
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="*" />
                        </Grid.RowDefinitions>
                        <TextBlock
                            Grid.Row="0"
                            Margin="0,0,0,10"
                            VerticalAlignment="Center"
                            Text="{x:Bind Title, Mode=OneWay}"
                            TextTrimming="CharacterEllipsis"
                            TextWrapping="NoWrap" />
                        <Viewbox Grid.Row="1" HorizontalAlignment="Center" VerticalAlignment="Top" Stretch="Uniform">
                            <local:WindowThumbnail x:Name="PART_Thumbnail" Source="{x:Bind Handle, Mode=OneWay}" />
                        </Viewbox>
                    </Grid>
                </DataTemplate>
            </ListView.ItemTemplate>
        </ListView>
    </Grid>
</winuiex:WindowEx>
```

Add `xmlns:vm="using:WinTabber.ViewModels"` alongside the other `xmlns`
declarations. Background changed from WPF's near-transparent
`#01000000` (a WPF-specific hit-testing trick — a fully transparent
background in WPF does not receive hits, so the original used 1/255 alpha
to keep the `ListView` clickable while looking invisible) to WinUI 3's
`Transparent`, which — unlike WPF — does receive hits by default; the
`#01000000` trick is unneeded and would just be a wrong color if carried
over literally.

**Open item, consistent with this plan's Hint-chip precedent (Phase 2b):**
`WindowThumbnail.TargetWindow` (Task 4a.3) is not wired in this XAML — a
`{x:Bind}`/`{Binding}` to the window itself is not idiomatic in either XAML
dialect. Wire it in code-behind instead (Step 3).

- [ ] **Step 3: Port `DockWindow.xaml.cs`**

> **Corrected after a final-review finding (post-Task-4a.5 whole-phase review).** The draft below
> as originally written had two bugs, both fixed in the code block that follows:
> 1. `OnClosed` restored from a freshly-read `DesktopHelper.GetDesktopArea()`, substituting only
>    `Height` from `_reservedArea`. Once `MakeSpace` has shrunk the work area, `GetDesktopArea()`
>    only ever returns the already-shrunk value — the "restore" was an identity write that
>    restored nothing, and the desktop work area ratcheted narrower on every open/close cycle with
>    no recovery. Fixed by capturing the work area BEFORE the shrink, into a separate field
>    (`_originalDesktopArea`), and restoring from that saved value directly in `OnClosed`.
> 2. The scale calculation (`AppWindow.Size.Width / (double)Bounds.Width`) mixed physical pixels
>    for the whole window frame with DIPs for the client area — not a clean DPI scale factor — and
>    dropped the WPF original's `> 0` guard, so a zero `Bounds.Width` (possible at `Activated` time
>    before layout has run) produced `scale = Infinity`, which propagated into a reservation rect
>    with `Infinity`/`-Infinity` components that got cast to `int` and written to a global system
>    display setting via `SetDesktopArea`. Fixed by using `DesktopHelper.GetScaleForWindow` (the
>    helper Task 4a.5 added for exactly this DIP-to-physical-pixel conversion) and restoring a
>    `Bounds.Width > 0` guard.
>
> Both bugs were provable purely from reading the code (an identity write, and an unguarded
> division producing `Infinity`) and were caught only in final-phase review, not in Step 4's
> original verification — see the phase 4a fix report for the corrected verification method
> (`SPI_GETWORKAREA` readings across shrink and a graceful close).

```csharp
// winui3/WinTabberUI/Views/DockWindow.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinRT.Interop;
using WinTabber.Api.Windowing;
using WinTabber.ViewModels;
using WinTabberUI.Controls;
using WinTabberUI.Windowing;

namespace WinTabberUI;

public sealed partial class DockWindow : WinUIEx.WindowEx
{
    private readonly WindowManager _windowManager;
    public DockWindowViewModel ViewModel { get; }

    private readonly nint _hwnd;

    // The reservation MakeSpace computed and applied — used to reposition windows against.
    private Windows.Foundation.Rect? _reservedArea;

    // The work area as it was BEFORE MakeSpace shrank it, captured once, up front. OnClosed
    // restores from this saved value directly rather than re-reading GetDesktopArea() (which,
    // once MakeSpace has run, always returns the already-shrunk area — restoring from that is an
    // identity write that leaves the desktop permanently narrower after every open/close cycle).
    private Windows.Foundation.Rect? _originalDesktopArea;

    public DockWindow(WindowManager windowManager, DockWindowViewModel viewModel)
    {
        _windowManager = windowManager;
        ViewModel = viewModel;

        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);

        // Wire WindowThumbnail.TargetWindow for every container the ListView generates — there is
        // no XAML-level way to bind a control property to "the window that hosts me" in WinUI 3.
        WindowsList.ContainerContentChanging += (_, args) =>
        {
            if (args.ItemContainer.ContentTemplateRoot is FrameworkElement root)
            {
                var thumbnail = root.FindName("PART_Thumbnail") as WindowThumbnail;
                // TODO(verify): confirm FindName walks into a DataTemplate's realized visual tree in
                // WinUI 3 the way it did in WPF (the x:Name="PART_Thumbnail" registration itself is
                // already in Step 2's XAML — this only needs confirming that FindName resolves it at
                // runtime). If it returns null, the fallback is VisualTreeHelper-walking root's
                // descendants for the first WindowThumbnail instead.
                if (thumbnail is not null)
                {
                    thumbnail.TargetWindow = this;
                }
            }
        };

        Activated += (_, _) => MakeSpace();
        Closed += OnClosed;
    }

    private void MakeSpace()
    {
        // Bounds.Width can still be 0 at Activated time, before layout has run — guard the same
        // way the WPF original did (`_rect is null && ActualWidth > 0`), otherwise scale becomes
        // Infinity and propagates into a reservation rect with Infinity/-Infinity components,
        // which then gets cast to int when written via SetDesktopArea — garbage written to a
        // global system display setting. Leaving _reservedArea null here means the next
        // Activated firing (once layout has run) retries.
        if (_reservedArea is not null || !(Bounds.Width > 0))
        {
            return;
        }

        var screenArea = DesktopHelper.GetDesktopArea();
        _originalDesktopArea = screenArea;

        // AppWindow.Size is physical pixels for the whole window (frame included); Bounds is DIPs
        // for the client area only — their ratio is inflated by the non-client border, not a
        // clean DPI scale factor. Use the established DIP-to-physical-pixel helper instead (added
        // in Task 4a.5 for this exact conversion problem).
        var scale = DesktopHelper.GetScaleForWindow(_hwnd);

        _reservedArea = new Windows.Foundation.Rect(
            screenArea.X + Width * scale,
            screenArea.Y,
            screenArea.Width - Width * scale,
            screenArea.Height);
        DesktopHelper.SetDesktopArea(_reservedArea.Value);

        foreach (var window in _windowManager.GetWindows()
            .Where(w => w.State != WindowPlacement.WindowState.Minimized
                && w.State != WindowPlacement.WindowState.Hidden
                && w.Bounds.X < _reservedArea.Value.X
                && w.Bounds.Width > 0))
        {
            if (!window.Process.IsProcessElevated)
            {
                window.MoveTo(new System.Drawing.Point((int)_reservedArea.Value.X, window.Bounds.Y));
            }
        }
    }

    // WinUI 3's Window has no overridable OnClosed (unlike WPF's Window.OnClosing) — Closed is a
    // plain event, wired up in the constructor above.
    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_originalDesktopArea is not null)
        {
            // Restore from the pre-shrink value captured in MakeSpace, not a freshly-read
            // GetDesktopArea() — by this point that call would only ever return the already-
            // shrunk area, making the restore an identity write (see field comment above).
            DesktopHelper.SetDesktopArea(_originalDesktopArea.Value);
            _originalDesktopArea = null;
        }

        _reservedArea = null;
    }
}
```

Three deliberate simplifications from the WPF original, all disclosed:
- The commented-out `//MakeSpace()` calls in `DockWindow_LayoutUpdated`/
  `DockWindow_IsVisibleChanged` (dead code in the WPF original — already
  commented out there) are dropped rather than ported as dead code, since
  this plan's precedent (Task 0.2, etc.) is to port dead *reachable* code
  for structural parity, not resurrect already-disabled debug scaffolding.
- `ApplicationName`'s WPF `DependencyProperty` (used only so XAML could set
  it declaratively — no XAML in this repo actually does) is dropped; the
  constructor-injected `ViewModel.ApplicationName` is set directly by
  whoever constructs this window (the coordinator, ported in a later phase),
  the same simplification `SettingsWindow` (Task 3.5) already established
  for plain constructor-injected properties over `DependencyProperty`
  ceremony that has no real XAML consumer.
- `OnActivated` in the WPF original only calls `base.OnActivated(e)` with no
  added logic — dropped as a no-op override.

- [ ] **Step 4: Build and manually verify via UI Automation**

Run: `dotnet build WinTabber.slnx`

Then launch `winui3/WinTabberUI.exe` (via whatever entry-point wiring exists
at the time this task runs — `App.xaml.cs` may need a temporary direct
`new DockWindow(...)` launch if no coordinator wires it up yet in this
phase; note this explicitly in the report if so, since full app-wiring is
Phase 5's job) and walk its UI Automation tree (`System.Windows.Automation`
or `UIAutomationClient`/`UIAutomationTypes`, per the pattern Phase 3's fix
round established) to confirm: the `ListView` contains one item per open
window for the target application, each item shows a real `TextBlock` with
the window's title (not a bare type name), and a `WindowThumbnail` element
is present in the tree for each item (a UI Automation walk cannot directly
verify DWM's own compositing, but can confirm the control itself is
instantiated and sized). Also confirm — by checking the desktop's actual
work area via `SystemParametersInfo(SPI_GETWORKAREA)` before and after
showing/closing the window — that `MakeSpace`/`OnClosed` actually
resize and restore the desktop work area, not just that no exception is
thrown.

- [ ] **Step 5: Commit**

```bash
git add winui3/WinTabberUI/Views/DockWindow.xaml winui3/WinTabberUI/Views/DockWindow.xaml.cs \
  winui3/WinTabberUI/Resources/DockWindowResources.xaml
git commit -m "feat: port DockWindow to WinUI3

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

### Task 4a.5: Port `SuspendedWindowsWindow`

**Files:**
- Create: `winui3/WinTabberUI/Views/SuspendedWindowsWindow.xaml`
- Create: `winui3/WinTabberUI/Views/SuspendedWindowsWindow.xaml.cs`

**Interfaces:**
- Consumes: `WinTabber.ViewModels.SuspendedWindowsViewModel` (Task 4a.1), `WinTabberUI.Windowing.DesktopHelper` (Task 4a.2), `WinTabber.Interop.IWindowInterop`.

WPF's `fa:IconBlock Icon="Sun"` (fontawesome.sharp — a WPF-only icon font
package) gets the deferred-icon placeholder pattern per Global Constraints,
same as every other undecided icon site in this plan: `Glyph="&#xE897;"`
with a `TODO(icon)` comment naming the original (`fontawesome.sharp
FontAwesomeIcon.Sun`). The `DataTrigger`-driven "No windows are sleeping"
empty-state text becomes a direct `x:Bind` + converter on the `TextBlock`
itself, the same pattern Task 3.4 used for `ConflictIcon` — not
`VisualStateManager`, since this is a plain element property, not a custom
control's own visual states. `AcrylicChrome` (`ACCENT_ENABLE_BLURBEHIND`,
`DWMWCP_ROUND`) → `WindowEx` + `DesktopAcrylicBackdrop`.

- [ ] **Step 1: Port `SuspendedWindowsWindow.xaml`**

```xml
<!-- winui3/WinTabberUI/Views/SuspendedWindowsWindow.xaml -->
<winuiex:WindowEx
    x:Class="WinTabberUI.SuspendedWindowsWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:winuiex="using:WinUIEx"
    xmlns:vm="using:WinTabber.ViewModels"
    xmlns:c="using:WinTabber.UI.Common.ValueConverters"
    Title="SuspendedWindowsWindow"
    IsTitleBarVisible="False"
    IsShownInSwitchers="False"
    IsResizable="False"
    IsMinimizable="False"
    IsMaximizable="False"
    IsAlwaysOnTop="True"
    MinWidth="200"
    MinHeight="96"
>
    <Grid Margin="10" Background="#30000000">
        <Grid.Resources>
            <ResourceDictionary>
                <c:BoolToVisibilityConverter x:Key="BoolToVisibilityConverter" />
                <Style x:Key="ResumeTileButton" TargetType="Button">
                    <Setter Property="Background" Value="Transparent" />
                    <Setter Property="Foreground" Value="White" />
                    <Setter Property="Margin" Value="6" />
                    <Setter Property="Padding" Value="12,8" />
                    <Setter Property="Template">
                        <Setter.Value>
                            <ControlTemplate TargetType="Button">
                                <Border
                                    Background="{TemplateBinding Background}"
                                    Padding="{TemplateBinding Padding}"
                                    CornerRadius="4"
                                >
                                    <VisualStateManager.VisualStateGroups>
                                        <VisualStateGroup x:Name="CommonStates">
                                            <VisualState x:Name="Normal" />
                                            <VisualState x:Name="PointerOver">
                                                <VisualState.Setters>
                                                    <Setter Target="Bg.Background" Value="#22666666" />
                                                </VisualState.Setters>
                                            </VisualState>
                                            <VisualState x:Name="Pressed">
                                                <VisualState.Setters>
                                                    <Setter Target="Bg.Background" Value="#dd666666" />
                                                </VisualState.Setters>
                                            </VisualState>
                                        </VisualStateGroup>
                                    </VisualStateManager.VisualStateGroups>
                                    <ContentPresenter x:Name="Bg" Content="{TemplateBinding Content}" />
                                </Border>
                            </ControlTemplate>
                        </Setter.Value>
                    </Setter>
                </Style>
            </ResourceDictionary>
        </Grid.Resources>

        <TextBlock
            HorizontalAlignment="Center"
            VerticalAlignment="Center"
            FontStyle="Italic"
            Foreground="White"
            Opacity="0.7"
            Text="No windows are sleeping"
            Visibility="{x:Bind ViewModel.Items.Count, Mode=OneWay, Converter={StaticResource EmptyCountToVisibilityConverter}}" />

        <ItemsControl HorizontalAlignment="Center" ItemsSource="{x:Bind ViewModel.Items, Mode=OneWay}">
            <ItemsControl.ItemsPanel>
                <ItemsPanelTemplate>
                    <StackPanel Orientation="Horizontal" />
                </ItemsPanelTemplate>
            </ItemsControl.ItemsPanel>
            <ItemsControl.ItemTemplate>
                <DataTemplate x:DataType="vm:SuspendedWindowItemViewModel">
                    <Button Style="{StaticResource ResumeTileButton}" Command="{x:Bind ResumeCommand}" ToolTipService.ToolTip="Click to resume">
                        <StackPanel Orientation="Horizontal">
                            <!-- TODO(icon): originally fontawesome.sharp FontAwesomeIcon.Sun -->
                            <FontIcon Margin="0,0,8,0" VerticalAlignment="Center" Glyph="&#xE897;" />
                            <StackPanel Orientation="Vertical">
                                <TextBlock FontWeight="Bold" Text="{x:Bind ProcessName}" />
                                <TextBlock MaxWidth="220" Opacity="0.8" Text="{x:Bind Title}" TextTrimming="CharacterEllipsis" />
                            </StackPanel>
                        </StackPanel>
                    </Button>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </Grid>
</winuiex:WindowEx>
```

**Open item, not resolved by this task:** `EmptyCountToVisibilityConverter`
does not exist yet in `winui3/WinTabber.UI.Common/ValueConverters/ValueConverters.cs`
— the WPF original relied on a `DataTrigger Binding="{Binding Items.Count}"
Value="0"` comparing an `int` to a literal, which `x:Bind` cannot replicate
directly (no `DataTrigger` in WinUI 3, and `x:Bind`'s converter takes only
the bound value, not a comparison target). Add a small new converter —
`public sealed class EmptyCountToVisibilityConverter : IValueConverter`
returning `Visibility.Visible` when `(int)value == 0` else `Collapsed` — to
`winui3/WinTabber.UI.Common/ValueConverters/ValueConverters.cs`, registered
in `winui3/WinTabber.UI.Common/Resources/ValueConvertersResources.xaml`
(both files already exist, from Task 2.1/2.3 — this is an addition, not a
new file), before this task's build can succeed. This mirrors the class of
gap Task 3.4 found in Phase 2b's `Generic.xaml` (a genuinely missing
resource, added as part of the consuming task rather than deferred).

- [ ] **Step 2: Port `SuspendedWindowsWindow.xaml.cs`**

```csharp
// winui3/WinTabberUI/Views/SuspendedWindowsWindow.xaml.cs
using Microsoft.UI.Windowing;
using WinRT.Interop;
using WinTabber.Interop;
using WinTabber.ViewModels;
using WinTabberUI.Windowing;

namespace WinTabberUI;

public sealed partial class SuspendedWindowsWindow : WinUIEx.WindowEx
{
    private const double BottomMargin = 24;

    private readonly IWindowInterop _windowInterop;
    public SuspendedWindowsViewModel ViewModel { get; }

    public SuspendedWindowsWindow(SuspendedWindowsViewModel viewModel, IWindowInterop windowInterop)
    {
        ViewModel = viewModel;
        _windowInterop = windowInterop;

        InitializeComponent();

        var hwnd = WindowNative.GetWindowHandle(this);

        // Never let this window take focus/activation, even from a mouse click on one of its
        // buttons — that keeps focus on WindowSelectorWindow regardless of show ordering between
        // the two coordinators. Same call the WPF original made via IWindowInterop, just with a
        // WinUI3-obtained handle instead of WindowInteropHelper's.
        _windowInterop.MakeWindowNonActivating(hwnd);

        SizeChanged += (_, _) => PositionWindow(hwnd);
        Activated += (_, _) => PositionWindow(hwnd);

        PositionWindow(hwnd);
    }

    private void PositionWindow(nint hwnd)
    {
        // TODO(verify): WPF's original found the screen under the cursor via
        // System.Windows.Forms.Screen.FromPoint(Control.MousePosition) — that WinForms API still
        // works unchanged in a WinUI3 app (confirmed elsewhere in this plan, WindowSelectorViewModel
        // already does the same). The WinUI3-native alternative is
        // Microsoft.UI.Windowing.DisplayArea.GetFromPoint / GetFromWindowId — either is valid; this
        // draft uses the already-proven WinForms path for consistency with WindowSelectorViewModel's
        // CursorScreen, confirm this doesn't diverge from whatever WindowSelectorWindow's own port
        // (a later phase) settles on for the same concept.
        var workingArea = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Control.MousePosition).WorkingArea;
        var bounds = DesktopHelper.ToLogicalBounds(hwnd, workingArea);

        AppWindow.Move(new Windows.Graphics.PointInt32(
            (int)(bounds.Left + (bounds.Width - Bounds.Width) / 2),
            (int)(bounds.Bottom - Bounds.Height - BottomMargin)));
    }
}
```

**Open item:** the WPF original repositions on `IsVisibleChanged` (true) and
`SizeChanged`; this port repositions on `Activated` and `SizeChanged`
instead, since WinUI 3's `Window` has no direct `IsVisibleChanged`-equivalent
event the way a `FrameworkElement`'s `Visibility` DP change does (a `Window`
itself isn't a `FrameworkElement`, per Task 3.5's finding). **TODO(verify)
at runtime:** confirm this window is actually repositioned correctly every
time it is shown by whatever later shows it (a coordinator, not built in
this phase) — if `Activated` doesn't fire reliably on every show (e.g. if
the window is shown without stealing focus, which is the whole point of
`MakeWindowNonActivating`), a different hook is needed; flag this
explicitly in Step 3's manual verification rather than assume `Activated`
is sufficient.

- [ ] **Step 3: Build and manually verify via UI Automation**

Run: `dotnet build WinTabber.slnx`

Then, similar to Task 4a.4 Step 4: launch the window (via a temporary direct
construction if no coordinator exists yet at this point in the plan — note
this explicitly) with the suspension service holding at least one suspended
entry, and walk its UI Automation tree to confirm: a `Button` exists per
suspended window with real child `TextBlock`s showing the process name and
title (not a bare type name), and that invoking the button
(`InvokePattern.Invoke()`) actually triggers `ResumeCommand` (observable via
the entry disappearing from the suspension service's list, or the window
itself closing per `SuspendedWindowItemViewModel.ResumeCommand`'s
`eventManager.SendEvent(EventType.WindowSelected)` call). Also confirm the
empty-state text (`EmptyCountToVisibilityConverter`) actually shows/hides
correctly by toggling between zero and one suspended entries.

- [ ] **Step 4: Commit**

```bash
git add winui3/WinTabberUI/Views/SuspendedWindowsWindow.xaml winui3/WinTabberUI/Views/SuspendedWindowsWindow.xaml.cs \
  winui3/WinTabber.UI.Common/ValueConverters/ValueConverters.cs \
  winui3/WinTabber.UI.Common/Resources/ValueConvertersResources.xaml
git commit -m "feat: port SuspendedWindowsWindow to WinUI3

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Phase 4b — Convert `WindowSelectorWindow`

This phase re-read `WindowSelectorWindow.xaml(.cs)`, `SpatialNavigationListView.cs`,
`HoverSelect.cs`, `EditableTextBlock.xaml(.cs)`, `WindowSelectorViewModel.cs`,
`WindowItem.cs`, `ApplicationStateViewModel.cs`, `ApplicationStateViewModelFactory.cs`,
`ActiveWindowStateService.cs`, `MediaControlsStateService.cs`, `DesktopHelper.cs`,
`SuspendedWindowsWindow.xaml.cs` (for its established `ResizeToContent`/`PositionWindow`
pattern), and `WindowSelectorResources.xaml` in full, and resolved every open question
the prior scope note (commit 6c79a9a) left blocking, each against real compiler output
via a throwaway probe file (created, tested, deleted — never committed):

1. **`ReactiveWindow<T>` confirmed a non-question** — `WindowSelectorWindow : WindowEx`
   with a constructor-injected `ViewModel` property, matching `SettingsWindow`/
   `DockWindow`/`SuspendedWindowsWindow`.
2. **`Microsoft.UI.Xaml.Media.CompositionTarget.Rendering` exists**, confirmed by a
   real compile (`Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += (s, e) => { };`
   built clean against the installed SDK) — the exact same type/member shape as WPF's
   `System.Windows.Media.CompositionTarget.Rendering`. The whole `ArmReveal`/
   `RevealWhenComposed`/`_parked` hack ports almost verbatim, only the namespace
   changes. This was the single highest-value unknown from the prior pass and it
   resolves in the best possible way: no redesign needed, only a rename. (The API's
   *existence* is compiler-verified; its exact runtime timing semantics — whether it
   fires mid-composition the same way WPF's does — is not, and Task 4b.4 below calls
   this out as something its own implementer must verify against real execution, the
   same way Task 4a.3/4a.4 verified DWM thumbnail timing.)
3. **`ListView.ContainerFromIndex(int)` exists directly** (inherited from
   `Microsoft.UI.Xaml.Controls.ListViewBase`), returning `Microsoft.UI.Xaml.DependencyObject`
   — confirmed by a real compile forcing a type-mismatch error to print the return
   type. `ContainerContentChanging` (with `.Item`/`.ItemContainer`/`.ItemIndex`) also
   confirmed to exist and compile. `SpatialNavigationListView`'s container-access
   pattern ports as a near-rename: `ItemContainerGenerator.ContainerFromIndex(i)` →
   `ContainerFromIndex(i)`.
4. **`HoverSelect`'s redesign is simpler in WinUI 3 than the WPF original, not just
   different.** Reading `WindowSelectorResources.xaml`'s actual `MultiTrigger` (the
   thing `HoverSelect.IsEnabled`'s inheritance existed to feed) shows it does exactly
   one thing: `IsMouseOver=True AND HoverSelect.IsEnabled=True → ListViewItem.IsSelected=True`.
   WinUI 3 has no attached-property inheritance, but it also doesn't need it here —
   `SpatialNavigationListView` can simply expose `bool HoverSelectionEnabled` as a
   plain property on itself (no attached property, no inheritance), and each
   container's `PointerEntered` handler (wired via `ContainerContentChanging`, the
   same pattern Phase 2b's `ShortcutCaptureBox`/Phase 4a's `DockWindow` established)
   reads that one property directly off its owning list, then sets `SelectedItem`
   on the `ListView` itself. No per-container attached state needed at all.
5. **`WrapPanel`** — confirmed real, known answer from the prior pass:
   `CommunityToolkit.WinUI.Controls.Primitives` (XAML namespace
   `using:CommunityToolkit.WinUI.Controls`), not yet referenced by
   `winui3/WinTabberUI` — added in Task 4b.3.
6. **`EditableTextBlock` needs the `*Base` intermediate-class workaround**, same as
   Tasks 3.2-3.4's `ReactivePage<T>` pages — `ReactiveUserControl<WindowItem>` hits the
   identical generic-XAML-root `x:TypeArguments` propagation bug (`UserControl`, unlike
   `Window`, genuinely is a `DependencyObject`/`FrameworkElement`, so the bug applies
   here the same way it did to the settings pages, unlike `WindowSelectorWindow` itself).
7. **A genuine, previously-uncaught DI gap found by reading the graph, not by a crash
   this time.** `WindowSelectorViewModel`'s constructor needs `ApplicationStateViewModel`
   (via `ApplicationStateViewModelFactory`), which transitively needs
   `IMediaControlsStateService` → `MediaControlsStateService` — a concrete type that
   lives in the WPF-dependent `WinTabber.UI.Media` project, which `winui3/WinTabberUI`
   does not and should not reference (that's the whole point of the parallel-app
   structure). `winui3/WinTabber.UI.Media` (the WinUI3 copy, scaffolded in Phase 1) has
   no media-controls service of its own yet — that's `MediaControlsWindow`'s own future
   phase's job, not this one's. Task 4b.1 registers a minimal, explicitly-temporary stub
   (`IMediaControlsStateService.IsMediaControlsVisibleChanges => Observable.Empty<bool>()`,
   `HideView()` a no-op) scoped only to unblock `WindowSelectorViewModel`'s DI
   resolution — not a real implementation, and named/commented as such so it is not
   mistaken for one when `MediaControlsWindow`'s phase lands the real service.

**On the three Phase 4a final-review carry-forward items** (I3 show-path timing, M7
shared `SizeToContent`/resize-to-content helper, M8 `Bootstrapper` grouping naming):
M7 *is* directly relevant here — `WindowSelectorWindow` uses WPF's
`SizeToContent="WidthAndHeight"` plus `MaxWidth`/`MaxHeight` constraints, and
`WindowEx` (confirmed via compile: `MinWidth`/`MinHeight`/`MaxWidth`/`MaxHeight`/
`IsResizable`/`IsAlwaysOnTop`/`IsShownInSwitchers`/`IsTitleBarVisible` all exist and
compile as real `WindowEx` properties) has no automatic size-to-content behavior,
exactly the gap Task 4a.5 hand-rolled `ResizeToContent`/`PositionWindow` around for
`SuspendedWindowsWindow`. Task 4b.4 below reuses that established pattern (measure the
root content, scale via `DesktopHelper.GetScaleForWindow`, call
`AppWindow.ResizeClient`) rather than re-deriving a third variant — closing M7 by
reuse, not by extracting a shared helper (the two windows' sizing triggers differ
enough — content-driven with `MaxWidth`/`MaxHeight` clamping here, vs. a simple content
measure there — that forcing them through one shared method now would be premature
abstraction; a real `DesktopHelper.ResizeWindowToContent` extraction is worth
doing once a third window needs the identical shape, not before). I3 and M8 remain
genuinely not exercised by this window's port and stay open for whichever future task
touches them.

### Task 4b.1: Extend `winui3/WinTabberUI`'s DI graph for `WindowSelectorViewModel`

**Files:**
- Modify: `winui3/WinTabberUI/Bootstrapper.cs`
- Create: `winui3/WinTabberUI/Services/StubMediaControlsStateService.cs`

**Interfaces:**
- Produces: `WinTabberUI.Services.StubMediaControlsStateService : WinTabber.ViewModels.Services.IMediaControlsStateService`, registered singleton.
- Consumes/registers: `WinTabber.ViewModels.ApplicationStateViewModel` (via `ApplicationStateViewModelFactory.CreateApplicationStateViewModel()`), `WinTabber.ViewModels.ApplicationStateViewModelFactory`, `WinTabber.ViewModels.WindowSelectorViewModel`.

`WindowSelectorViewModel`'s full dependency chain, traced from its constructor:
`WindowSelectorViewModel(ApplicationStateViewModel, WinTabberEventManager, WindowManager,
IProcessSuspensionService, IWindowThumbnailService, ApplicationSettings)`. Every one of
these except `ApplicationStateViewModel` is already registered (Tasks 3.1/4a.1).
`ApplicationStateViewModel` itself is not a DI-constructed type — it is built by
`ApplicationStateViewModelFactory.CreateApplicationStateViewModel()`, which needs
`IMediaControlsStateService` and `IActiveWindowStateService`.
`IActiveWindowStateService` → `WinTabberUI.Services.ActiveWindowStateService(WinTabberEventManager, WindowManager)`
— both already registered, straightforward `AddSingleton`.
`IMediaControlsStateService` has no real implementation available in `winui3/WinTabberUI`
yet (see the phase-opening note above) — register the stub instead.

- [ ] **Step 1: Write the stub `IMediaControlsStateService`**

```csharp
// winui3/WinTabberUI/Services/StubMediaControlsStateService.cs
using System.Reactive.Linq;
using WinTabber.ViewModels.Services;

namespace WinTabberUI.Services;

/// <summary>
/// Placeholder implementation, registered only to satisfy <see cref="WinTabber.ViewModels.ApplicationStateViewModelFactory"/>'s
/// dependency graph so <see cref="WinTabber.ViewModels.WindowSelectorViewModel"/> can be
/// constructed in this phase. The real media-controls state service (WinTabber.UI.Media's
/// MediaControlsStateService, WPF-dependent, not referenced by this project) is ported when
/// MediaControlsWindow itself is — that phase should replace this registration with the real
/// one, not build alongside it.
/// </summary>
public sealed class StubMediaControlsStateService : IMediaControlsStateService
{
    public IObservable<bool> IsMediaControlsVisibleChanges { get; } = Observable.Empty<bool>();

    public void HideView() { }
}
```

- [ ] **Step 2: Register the graph**

```csharp
// winui3/WinTabberUI/Bootstrapper.cs — new method, called from Init()
private static IServiceCollection AddWindowSelectorGraph(this IServiceCollection services)
{
    return services
        .AddSingleton<IActiveWindowStateService, ActiveWindowStateService>()
        .AddSingleton<IMediaControlsStateService, StubMediaControlsStateService>()
        .AddSingleton<ApplicationStateViewModelFactory>()
        .AddSingleton(sp => sp.GetRequiredService<ApplicationStateViewModelFactory>().CreateApplicationStateViewModel())
        .AddSingleton<WindowSelectorViewModel>();
}
```

Add `.AddWindowSelectorGraph()` to the fluent chain in `Init()`, and the two new
`using` directives (`WinTabber.ViewModels.Services`, `WinTabberUI.Services` is already
present) this needs.

- [ ] **Step 3: Build and verify DI resolution**

Run: `dotnet build WinTabber.slnx` — must be clean. Run the `winui3/WinTabberUI.Tests`
DI smoke test (`BootstrapperDiResolutionTests`, added in Phase 4a's final-review fix
wave) — it only walks `Window`/`WindowEx` types, so it will not exercise
`WindowSelectorViewModel` directly until Task 4b.4 registers `WindowSelectorWindow`
itself; note this explicitly rather than treating a green smoke-test run as proof this
task's graph resolves. Actually resolve `WindowSelectorViewModel` from a real
`Bootstrapper.Init()` container in a throwaway test/harness (temporary, reverted before
commit, same discipline as Task 4a.4/4a.5) to prove the whole chain constructs without
exception before moving on.

- [ ] **Step 4: Commit**

Document any registration gap found beyond what's listed above (there may be one this
plan's static trace missed) directly in the commit message, not only in the task
report — this exact documentation gap recurred three times in Phase 4a despite being
called out each time.

```bash
git add winui3/WinTabberUI/Bootstrapper.cs winui3/WinTabberUI/Services/StubMediaControlsStateService.cs
git commit -m "feat: extend winui3 DI graph for WindowSelectorViewModel

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

### Task 4b.2: Port `EditableTextBlock`

**Files:**
- Create: `winui3/WinTabberUI/Views/EditableTextBlockBase.cs`
- Create: `winui3/WinTabberUI/Views/EditableTextBlock.xaml`
- Create: `winui3/WinTabberUI/Views/EditableTextBlock.xaml.cs`

**Interfaces:**
- Produces: `WinTabberUI.Views.EditableTextBlockBase : ReactiveUI.ReactiveUserControl<WindowItem>` (the generic-root workaround); `WinTabberUI.Views.EditableTextBlock : EditableTextBlockBase`.
- Consumes: `WinTabber.ViewModels.WindowItem`'s `Title`/`IsEditing`/`CanEdit`/`IsSuspendButtonVisible` properties and `StartEditCommand`/`SaveTitleCommand`/`CancelEditTitleCommand`/`SuspendCommand`/`ThumbnailCommand` commands (all already WPF-free, confirmed by reading `WindowItem.cs`).

Real conversions beyond the established `*Base` pattern:
1. **`IsMouseOver`** (used for `BorderContainer`'s background) has no WinUI 3
   `FrameworkElement`-level equivalent — WPF's is a built-in bindable property; WinUI 3
   only has `PointerEntered`/`PointerExited` events. Add a plain `bool IsPointerOver`
   property on the code-behind, toggled by those two events on the root, and bind
   `Border.Background` to it via `x:Bind` (not the WPF file's `ElementName=root`
   trick, which has no direct WinUI 3 equivalent either — bind directly to the
   code-behind property instead).
2. **`TextBox.InputBindings`/`KeyBinding`** (Enter→`SaveTitleCommand`, Escape→
   `CancelEditTitleCommand`) has no WinUI 3 equivalent — `TextBox` has no
   `InputBindings` collection. Replace with a `KeyDown` handler on the `TextBox`
   checking `args.Key == VirtualKey.Enter` / `VirtualKey.Escape` and invoking the
   corresponding `ICommand` directly.
3. **`fa:IconBlock`** (FontAwesome.Sharp: `Moon`, `ExternalLinkAlt`, `Check`, `Ban`)
   is not an iNKORE icon key, so it doesn't go through `IconKeyToGlyphConverter` —
   it's a different icon library entirely, used directly. Apply the same
   deferred-icon placeholder pattern established for iNKORE icons anyway, for
   consistency and to keep this task mechanical: `<FontIcon Glyph="&#xE897;" />`
   plus `<!-- TODO(icon): originally FontAwesome.Sharp <IconName> -->` at each of
   the four sites. Real glyph selection is Phase 6's job either way.
4. **`ui:TextBoxHelper.IsDeleteButtonVisible="False"`** — drop outright, per this
   plan's established no-op-removal pattern (Task 3.3's precedent).

- [ ] **Step 1: Write `EditableTextBlockBase`**

```csharp
// winui3/WinTabberUI/Views/EditableTextBlockBase.cs
using ReactiveUI;
using WinTabber.ViewModels;

namespace WinTabberUI.Views;

/// <summary>
/// See GeneralSettingsPageBase.cs for the full rationale: WinUI 3's XAML compiler does not
/// propagate x:TypeArguments from a generic base class on a XAML root (CS0305); this
/// non-generic intermediate class is the documented ReactiveUI.WinUI workaround. Unlike the
/// settings pages, EditableTextBlock's ViewModel is bound directly (x:Bind ViewModel="{Binding}"
/// from the hosting ItemTemplate), not via DataContext, so this base class does not need the
/// DataContextChanged wiring GeneralSettingsPageBase has — ViewModel is set explicitly per
/// instance instead. See Task 4b.4's item template for how.
/// </summary>
public class EditableTextBlockBase : ReactiveUserControl<WindowItem>
{
}
```

- [ ] **Step 2: Port `EditableTextBlock.xaml`**

```xml
<!-- winui3/WinTabberUI/Views/EditableTextBlock.xaml -->
<local:EditableTextBlockBase
    x:Class="WinTabberUI.Views.EditableTextBlock"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:local="using:WinTabberUI.Views"
    xmlns:c="using:WinTabber.UI.Common.ValueConverters"
    Focusable="False"
>
    <Border
        x:Name="BorderContainer"
        CornerRadius="4"
        BorderBrush="Gray"
        PointerEntered="OnPointerEnteredRoot"
        PointerExited="OnPointerExitedRoot"
    >
        <Grid Margin="2">
            <Grid.ColumnDefinitions>
                <ColumnDefinition />
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="Auto" />
            </Grid.ColumnDefinitions>

            <TextBox
                x:Name="TitleTextBox"
                Text="{x:Bind ViewModel.Title, Mode=TwoWay}"
                BorderThickness="0"
                Foreground="White"
                Background="Transparent"
                IsReadOnly="{x:Bind ViewModel.IsEditing, Mode=OneWay, Converter={StaticResource InverseBoolConverter}}"
                VerticalAlignment="Center"
                VerticalContentAlignment="Center"
                MinHeight="0"
                Padding="2"
                KeyDown="OnTitleTextBoxKeyDown"
            />

            <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center">
                <Button
                    x:Name="SuspendButton"
                    Command="{x:Bind ViewModel.SuspendCommand, Mode=OneWay}"
                    ToolTipService.ToolTip="Sleep this process"
                    Visibility="{x:Bind ViewModel.IsSuspendButtonVisible, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}">
                    <!-- TODO(icon): originally FontAwesome.Sharp Moon -->
                    <FontIcon Glyph="&#xE897;" />
                </Button>
                <Button
                    x:Name="ThumbnailButton"
                    Command="{x:Bind ViewModel.ThumbnailCommand, Mode=OneWay}"
                    ToolTipService.ToolTip="Show as floating thumbnail"
                    Visibility="{x:Bind ViewModel.IsEditing, Mode=OneWay, Converter={StaticResource InverseBoolToVisibilityConverter}}">
                    <!-- TODO(icon): originally FontAwesome.Sharp ExternalLinkAlt -->
                    <FontIcon Glyph="&#xE897;" />
                </Button>
                <Button
                    x:Name="AcceptButton"
                    Command="{x:Bind ViewModel.SaveTitleCommand, Mode=OneWay}"
                    CommandParameter="{x:Bind ViewModel.Title, Mode=OneWay}"
                    Visibility="{x:Bind ViewModel.IsEditing, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}">
                    <!-- TODO(icon): originally FontAwesome.Sharp Check -->
                    <FontIcon Glyph="&#xE897;" />
                </Button>
                <Button
                    x:Name="CancelButton"
                    Command="{x:Bind ViewModel.CancelEditTitleCommand, Mode=OneWay}"
                    Visibility="{x:Bind ViewModel.IsEditing, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}">
                    <!-- TODO(icon): originally FontAwesome.Sharp Ban -->
                    <FontIcon Glyph="&#xE897;" />
                </Button>
            </StackPanel>
        </Grid>
    </Border>
</local:EditableTextBlockBase>
```

`InverseBoolConverter`/`BoolToVisibilityConverter`/`InverseBoolToVisibilityConverter`
are already ported (Task 2.1) — confirm this file's resource scope actually has them
merged (per `SettingsWindow.xaml`'s precedent, converters need explicit merging; check
whether `winui3/WinTabberUI`'s `App.xaml` already merges `WinTabber.UI.Common`'s
`ValueConvertersResources.xaml` app-wide, or whether this file needs its own
`UserControl.Resources` merge — this file's root is `EditableTextBlockBase`, a real
`UserControl`/`FrameworkElement`, so unlike `Window` it CAN legally host `.Resources`
directly if a local merge turns out to be needed).

**TODO(verify):** confirm `TextBox.KeyDown`'s event-arg shape
(`Windows.System.VirtualKeyEventArgs` vs `Microsoft.UI.Xaml.Input.KeyRoutedEventArgs`)
against real compiler output before writing Step 3 — this plan has not yet had a
`TextBox`-level `KeyDown` handler to confirm the exact type against.

- [ ] **Step 3: Port `EditableTextBlock.xaml.cs`**

```csharp
// winui3/WinTabberUI/Views/EditableTextBlock.xaml.cs
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace WinTabberUI.Views;

public sealed partial class EditableTextBlock : EditableTextBlockBase
{
    public EditableTextBlock()
    {
        InitializeComponent();
    }

    private void OnPointerEnteredRoot(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) { }

    private void OnPointerExitedRoot(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) { }

    private void OnTitleTextBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Enter:
                e.Handled = true;
                if (ViewModel.SaveTitleCommand.CanExecute.FirstOrDefaultAsync().Wait())
                {
                    ViewModel.SaveTitleCommand.Execute(ViewModel.Title).Subscribe();
                }
                return;
            case VirtualKey.Escape:
                e.Handled = true;
                if (ViewModel.CancelEditTitleCommand.CanExecute.FirstOrDefaultAsync().Wait())
                {
                    ViewModel.CancelEditTitleCommand.Execute().Subscribe();
                }
                return;
        }
    }
}
```

The WPF original's `BorderContainer_MouseDown`/`IsUnderButton` click-to-edit handling
is deliberately NOT ported here — Task 4b.4's item template wires the equivalent
`PointerPressed` handling at the tile (`Grid`) level, where the WPF original's own
`Grid_MouseUp` already lived, since both need the same "did this click land on a
button inside the tile" walk and duplicating it in two places would drift.

**TODO(verify):** the `SaveTitleCommand.CanExecute.FirstOrDefaultAsync().Wait()` guard
above is a synchronous wait on an `IObservable<bool>`, mirroring how a
`ReactiveCommand`'s enablement is normally read — confirm this compiles and behaves
correctly against the real `ReactiveCommand<string, string>` type before relying on
it; a synchronous `.Wait()` inside a UI-thread event handler is a plausible deadlock
risk if the observable ever needs the same thread to produce a value, so verify this
doesn't hang in practice (a real capture-and-check pattern outside the handler, set
once and read as a field, is a safer fallback if `.Wait()` proves risky).

- [ ] **Step 4: Build**

Run: `dotnet build WinTabber.slnx` — expect it to fail only for reasons downstream of
`WindowSelectorWindow.xaml` not yet existing (Task 4b.4's job) if this file is wired
into anything before then; in isolation, this file alone should build clean once the
`KeyDown` event-arg TODO(verify) above is resolved.

- [ ] **Step 5: Commit**

```bash
git add winui3/WinTabberUI/Views/EditableTextBlockBase.cs winui3/WinTabberUI/Views/EditableTextBlock.xaml winui3/WinTabberUI/Views/EditableTextBlock.xaml.cs
git commit -m "feat: port EditableTextBlock to WinUI3

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

### Task 4b.3: Port `HoverSelect` and `SpatialNavigationListView`

**Files:**
- Create: `winui3/WinTabberUI/Controls/SpatialNavigationListView.cs`
- Modify: `winui3/WinTabberUI/WinTabberUI.csproj` (add `CommunityToolkit.WinUI.Controls.Primitives`)
- Modify: `Directory.Packages.props` (add its `PackageVersion` if not already present)

**Interfaces:**
- Produces: `WinTabberUI.Controls.SpatialNavigationListView : Microsoft.UI.Xaml.Controls.ListView`, with `bool HoverSelectionEnabled { get; set; } = true` (the redesigned, non-attached replacement for `HoverSelect`) and `void SuppressHoverUntilPointerMoves()`.
- Consumes: `WinTabber.ViewModels.WindowItem`, `WinTabber.ViewModels.Models.WindowTileGrid`/`WindowTileInfo` (already WPF-free, ported by Task 1.2 — confirm exact namespace via `grep -rn "class WindowTileGrid"` before writing the `using`, since this plan's own history shows namespace assumptions have been wrong before).

`HoverSelect.cs` (the WPF attached-property file) is NOT ported — per the phase-opening
note above, its one job (feed a `Style` `MultiTrigger`) is replaced entirely by a plain
property on this control plus a `ContainerContentChanging`-wired `PointerEntered`
handler per container, so there is nothing left for a separate `HoverSelect` type to
do in WinUI 3.

- [ ] **Step 1: Confirm `WindowTileGrid`/`WindowTileInfo`'s actual current namespace**

Run: `grep -rn "class WindowTileGrid\|class WindowTileInfo" WinTabber.ViewModels`

- [ ] **Step 2: Add the `WrapPanel` package**

```xml
<!-- Directory.Packages.props, if not already present -->
<PackageVersion Include="CommunityToolkit.WinUI.Controls.Primitives" Version="8.2.250402" />
```

(Match whatever version `CommunityToolkit.WinUI.Controls.SettingsControls` — already
referenced — pins, per this package family's convention of shipping in lockstep; verify
against the actual installed `SettingsControls` version rather than guessing 8.2.250402
literally.)

```xml
<!-- winui3/WinTabberUI/WinTabberUI.csproj -->
<PackageReference Include="CommunityToolkit.WinUI.Controls.Primitives" />
```

- [ ] **Step 3: Port `SpatialNavigationListView.cs`**

```csharp
// winui3/WinTabberUI/Controls/SpatialNavigationListView.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;
using WinTabber.ViewModels;
// TODO(verify): confirm this using against Step 1's grep result before compiling.
using WinTabber.ViewModels.Models;

namespace WinTabberUI.Controls;

/// <summary>
/// Arrow-key spatial navigation between tiles, plus hover-to-select gated on the pointer having
/// actually moved (see <see cref="HoverSelectionEnabled" />). Ported from WinTabberUI's WPF
/// SpatialNavigationListView.cs; see WindowSelectorWindow.xaml.cs's port for why the selector
/// window reuses this control's instance across opens rather than recreating it.
/// </summary>
public class SpatialNavigationListView : ListView
{
    private static readonly VirtualKey[] ArrowKeys =
        [VirtualKey.Down, VirtualKey.Up, VirtualKey.Left, VirtualKey.Right];

    private WindowTileGrid? _tileGrid;
    private Point? _hoverAnchor;

    /// <summary>
    /// Replaces WPF's HoverSelect attached property. WinUI 3 has no property-value inheritance
    /// down the visual tree, so this is a plain property on the list itself rather than an
    /// attached one on each container -- each container's PointerEntered handler (wired below,
    /// via ContainerContentChanging) reads this directly off its owning list.
    /// </summary>
    public bool HoverSelectionEnabled { get; private set; } = true;

    public SpatialNavigationListView()
    {
        ContainerContentChanging += OnContainerContentChanging;
    }

    /// <summary>Ignore hover selection until the pointer actually moves.</summary>
    public void SuppressHoverUntilPointerMoves()
    {
        _hoverAnchor = GetCursorPosition();
        HoverSelectionEnabled = false;
    }

    private void OnContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        if (args.InRecycleQueue || args.ItemContainer is not ListViewItem container)
        {
            return;
        }

        // Safe to re-add on every call, including recycled containers: WinUI 3 event handler
        // subscription is idempotent only if this container never got this exact delegate
        // instance before -- ContainerContentChanging can fire more than once for the same
        // container across its lifetime, so guard with -= before += to avoid a double-fire.
        container.PointerEntered -= OnContainerPointerEntered;
        container.PointerEntered += OnContainerPointerEntered;
    }

    private void OnContainerPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (!HoverSelectionEnabled)
        {
            return;
        }

        if (sender is ListViewItem { Content: WindowItem item })
        {
            SelectedItem = item;
        }
    }

    /// <summary>Re-arms hover selection on the first real pointer movement after a suppress.</summary>
    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        base.OnPointerMoved(e);

        if (_hoverAnchor is not { } anchor)
        {
            return;
        }

        var current = GetCursorPosition();
        if (current.X == anchor.X && current.Y == anchor.Y)
        {
            return;
        }

        _hoverAnchor = null;
        HoverSelectionEnabled = true;
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);

        if (!ArrowKeys.Contains(e.Key))
        {
            return;
        }

        if (!TryInitializeTileGrid(out var tileGrid))
        {
            return;
        }

        var next = e.Key switch
        {
            VirtualKey.Down => tileGrid.MoveDown(),
            VirtualKey.Up => tileGrid.MoveUp(),
            VirtualKey.Left => tileGrid.MoveLeft(),
            VirtualKey.Right => tileGrid.MoveRight(),
            _ => null,
        };

        if (next is { })
        {
            SelectedItem = next;
            e.Handled = true;
        }
    }

    // TODO(verify): System.Windows.Forms.Control.MousePosition (used elsewhere in this migration,
    // e.g. WindowSelectorViewModel.CursorScreen) is the established, already-proven way to read the
    // live cursor position without a pointer event in hand. Confirm it is available/appropriate
    // here too before relying on it, or use PointerRoutedEventArgs.GetCurrentPoint(this).Position
    // from within OnPointerMoved instead if a non-event-driven read isn't actually needed by
    // SuppressHoverUntilPointerMoves's call site (it is called from WindowSelectorWindow.xaml.cs's
    // ShowWindowSelector, outside any pointer event).
    private static Point GetCursorPosition()
    {
        var p = System.Windows.Forms.Control.MousePosition;
        return new Point(p.X, p.Y);
    }

    /// <summary>Rebuilds the tile grid unless already built; leaves it unbuilt if containers
    /// are not yet realised, so the next arrow press retries rather than caching a half-built grid.</summary>
    private bool TryInitializeTileGrid(out WindowTileGrid tileGrid)
    {
        if (_tileGrid is { } cached)
        {
            tileGrid = cached;
            return true;
        }

        var infos = new List<WindowTileInfo>(Items.Count);
        for (var i = 0; i < Items.Count; i++)
        {
            if (ContainerFromIndex(i) is not FrameworkElement container)
            {
                tileGrid = null!;
                return false;
            }

            infos.Add(GetTile(i, container));
        }

        if (infos.Count == 0)
        {
            tileGrid = null!;
            return false;
        }

        _tileGrid = WindowTileGrid.Create(infos);
        tileGrid = _tileGrid;
        return true;
    }

    private WindowTileInfo GetTile(int index, FrameworkElement container)
    {
        var item = (WindowItem)Items[index];
        var transform = container.TransformToVisual(this);
        var location = transform.TransformPoint(new Point(0, 0));

        return new WindowTileInfo
        {
            Container = container,
            WindowItem = item,
            Location = location,
            IsSelected = index == SelectedIndex,
            Index = index,
        };
    }
}
```

`Items.Count`/`SelectedIndex`/`SelectedItem` are all inherited from `ListViewBase` and
match the WPF `ListView` surface directly — no conversion needed for those. The
`SelectionChanged`-based `ScrollIntoView` re-selection logic from the WPF original's
`OnSelectionChanged` override is dropped: WinUI 3's `ListView` already scrolls a newly
selected item into view by default (confirm this via the same live UI-Automation
verification Task 4b.4 needs anyway — if it turns out not to, add it back as an
explicit `ScrollIntoView(e.AddedItems[0])` call in an `OnSelectionChanged` override).

**TODO(verify):** `WindowTileInfo.Container`'s and `WindowTileGrid.Create`/`MoveDown`
etc.'s exact parameter types (`System.Windows.Media.Visual` in WPF — confirm the
WinUI3-ported `WindowTileGrid`/`WindowTileInfo` in `WinTabber.ViewModels` actually
takes a `FrameworkElement` here, not still `Visual` or something else; this plan's own
Task 1.2 moved these types into `WinTabber.ViewModels` but did not change their WPF
type dependencies at the time — if `WindowTileGrid`/`WindowTileInfo` still reference
`System.Windows.Media.Visual` internally, that is itself a real gap this task must fix
first, since `WinTabber.ViewModels` is supposed to be WPF-free).

- [ ] **Step 4: Build, resolving all TODO(verify) items above against real compiler output**

Run: `dotnet build WinTabber.slnx`. If `WindowTileGrid`/`WindowTileInfo` turn out to
still carry a WPF type dependency, fix that in `WinTabber.ViewModels` as part of this
task (small, contained fix, consistent with how this plan has handled similar
discoveries in shared projects before) rather than deferring it, since it would block
every future consumer of these types, not just this one.

- [ ] **Step 5: Commit**

Document exactly how each TODO(verify) resolved, per this plan's established
convention — this is the fourth task in a row in this phase alone to carry open
verification items into its commit step; do not let this be the task that breaks the
documentation habit Phase 4a's final review had to fix twice.

```bash
git add winui3/WinTabberUI/Controls/SpatialNavigationListView.cs \
  winui3/WinTabberUI/WinTabberUI.csproj Directory.Packages.props
git commit -m "feat: port SpatialNavigationListView (with redesigned hover-select) to WinUI3

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

### Task 4b.4: Port `WindowSelectorWindow`

**Files:**
- Create: `winui3/WinTabberUI/Views/WindowSelectorWindow.xaml`
- Create: `winui3/WinTabberUI/Views/WindowSelectorWindow.xaml.cs`
- Modify: `winui3/WinTabberUI/Bootstrapper.cs` (register the window itself — Task 4b.1 only registered the ViewModel)

**Interfaces:**
- Produces: `WinTabberUI.Views.WindowSelectorWindow : WinUIEx.WindowEx`, constructor `WindowSelectorWindow(WindowSelectorViewModel viewModel)`, `public void ShowWindowSelector()`, `public WindowSelectorViewModel ViewModel { get; }`.
- Consumes: `WinTabber.ViewModels.WindowSelectorViewModel` (Task 4b.1), `WinTabberUI.Controls.SpatialNavigationListView` (Task 4b.3), `WinTabberUI.Views.EditableTextBlock` (Task 4b.2), `WinTabberUI.Controls.WindowThumbnail` (Task 4a.3/4a.4, already handles dynamic `Source` changes correctly per that phase's fix round), `winui3/WinTabberUI/Windowing/DesktopHelper.cs`'s `ToLogicalBounds`/`GetScaleForWindow` (Task 4a.2).

Per the design spec's backdrop table: `WindowEx` + `DesktopAcrylicBackdrop` (this window
uses WPF's `AcrylicChrome` with `ACCENT_ENABLE_ACRYLICBLURBEHIND`/`DWMWCP_ROUND`, matching
`DockWindow`/`SuspendedWindowsWindow`'s established mapping). `IsShownInSwitchers=False`
(`ShowInTaskbar="False"`), `IsAlwaysOnTop=True` (`Topmost="True"`), `IsTitleBarVisible=False`
(`WindowStyle="None"`) — all four confirmed to exist as real `WindowEx` properties by a
throwaway compile probe during this phase's research. `IsResizable=False` (the WPF style
sets no explicit `ResizeMode`, but with no visible border/title bar and `SizeToContent`
driving the window's actual size, there is no user-facing resize affordance to preserve).

- [ ] **Step 1: Port `WindowSelectorWindow.xaml`**

```xml
<!-- winui3/WinTabberUI/Views/WindowSelectorWindow.xaml -->
<winuiex:WindowEx
    x:Class="WinTabberUI.Views.WindowSelectorWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:winuiex="using:WinUIEx"
    xmlns:controls="using:WinTabberUI.Controls"
    xmlns:views="using:WinTabberUI.Views"
    xmlns:vm="using:WinTabber.ViewModels"
    xmlns:wrap="using:CommunityToolkit.WinUI.Controls"
    Title="WindowSelector"
    IsShownInSwitchers="False"
    IsAlwaysOnTop="True"
    IsTitleBarVisible="False"
    IsResizable="False"
>
    <!--
        NOTE: Resources live on the root Grid, not on the Window itself, per SettingsWindow's
        established precedent (Microsoft.UI.Xaml.Window is not a DependencyObject/FrameworkElement
        and has no Resources property at all).
    -->
    <Grid x:Name="RootGrid" Background="#01000000">
        <Grid.Resources>
            <c:BoolToVisibilityConverter x:Key="BoolToVisibilityConverter" xmlns:c="using:WinTabber.UI.Common.ValueConverters" />
        </Grid.Resources>

        <controls:SpatialNavigationListView
            x:Name="TabListView"
            SelectionMode="Single"
            ScrollViewer.HorizontalScrollBarVisibility="Disabled"
            ItemsSource="{x:Bind ViewModel.WindowItems, Mode=OneWay}"
            SelectedItem="{x:Bind ViewModel.SelectedItem, Mode=TwoWay}"
        >
            <controls:SpatialNavigationListView.ItemsPanel>
                <ItemsPanelTemplate>
                    <wrap:WrapPanel
                        Orientation="Horizontal"
                        HorizontalSpacing="8"
                        VerticalSpacing="8"
                        HorizontalAlignment="Center"
                        VerticalAlignment="Center"
                    />
                </ItemsPanelTemplate>
            </controls:SpatialNavigationListView.ItemsPanel>
            <controls:SpatialNavigationListView.ItemTemplate>
                <DataTemplate x:DataType="vm:WindowItem">
                    <Grid
                        Margin="0"
                        Background="Transparent"
                        MaxWidth="{x:Bind (double)0, Mode=OneWay}"
                        PointerPressed="OnTileGridPointerPressed"
                    >
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto" />
                            <RowDefinition Height="*" />
                        </Grid.RowDefinitions>
                        <Grid.Opacity>
                            <!-- Dim the whole tile, not just the title strip: a suspended window
                                 is hidden, so its thumbnail renders blank and would otherwise just
                                 look broken. A thumbnailed window has been moved off-screen; dim
                                 its tile so it doesn't look like a duplicate of the floating
                                 thumbnail window. Both conditions use the same visual treatment, so
                                 a single converter reads either flag. -->
                            <Binding Path="IsSuspended" Converter="{StaticResource DimIfTrueConverter}" />
                        </Grid.Opacity>
                        <views:EditableTextBlock ViewModel="{x:Bind}" />
                        <Viewbox
                            Grid.Row="1"
                            HorizontalAlignment="Center"
                            VerticalAlignment="Top"
                            Stretch="Uniform"
                            Margin="0,10,0,0"
                        >
                            <controls:WindowThumbnail Source="{x:Bind Handle, Mode=OneWay}" />
                        </Viewbox>
                    </Grid>
                </DataTemplate>
            </controls:SpatialNavigationListView.ItemTemplate>
        </controls:SpatialNavigationListView>

        <Button
            x:Name="CloseApplicationButton"
            Command="{x:Bind ViewModel.CloseApplicationCommand, Mode=OneWay}"
            ToolTipService.ToolTip="Close all windows of this application"
            HorizontalAlignment="Right"
            VerticalAlignment="Top"
            Margin="0"
            Visibility="{x:Bind ViewModel.IsCloseApplicationButtonVisible, Mode=OneWay, Converter={StaticResource BoolToVisibilityConverter}}"
        >
            <!-- TODO(icon): originally a hand-drawn Path glyph matching CaptionButtons.xaml's
                 native close-button X (WinTabber.UI.Common/Chrome/CaptionButtons.xaml,
                 not ported -- the whole Chrome folder is deleted per the design spec). A real
                 FontIcon "Dismiss" glyph is a closer match than a placeholder; not deferred to
                 Phase 6 since this one has an obvious correct answer already (Segoe Fluent
                 Dismiss, U+E711) rather than needing a real icon-mapping decision. -->
            <FontIcon Glyph="&#xE711;" />
        </Button>
    </Grid>
</winuiex:WindowEx>
```

Two things in the draft above are placeholders for the implementer to resolve, not
fabricated final answers: (a) the `MaxWidth="{x:Bind (double)0, Mode=OneWay}"` line is
a deliberately-broken placeholder marking where the WPF original's
`Grid.Style`/`Style.Triggers` `DataTrigger`-based `IsSuspended`/`IsThumbnailed` dimming
needs a real `x:Bind`+converter treatment (Task 3.4's `ConflictIcon` established the
pattern; a new `DimIfTrueConverter` reading either `IsSuspended` or `IsThumbnailed` and
returning `0.4`/`1.0` opacity is the natural shape, but WinUI 3's `Grid.Opacity` cannot
bind to two independent source properties the way a WPF `Style.Triggers` block with two
`DataTrigger`s could without a real multi-value solution — `{x:Bind}` has no
`MultiBinding` equivalent, so this needs either two separate bindings ANDed via a
converter reading the whole `WindowItem` (bind `Opacity` to the item itself with a
converter checking both flags) or a computed `bool IsDimmed` property added to
`WindowItem` combining both — **decide which, against real compiler output, before
finalizing this task**, this is the one design point in the whole phase intentionally
left for the implementer since it needs a `WindowItem`-level decision this research
pass should not make unilaterally on a shared ViewModel type without more context on
whether `IsDimmed` belongs there); (b) `PointerPressed="OnTileGridPointerPressed"`
needs the exact click-to-select-and-close logic from the WPF original's `Grid_MouseUp`
(ported in Step 2 below).

Confirm `winui3/WinTabber.UI.Common`'s `ValueConvertersResources.xaml` merge scope
(App-wide, or does this file need its own explicit `Grid.Resources` merge, the way
`SettingsWindow.xaml` needed one for `ShortcutsSettingsPage`'s `ms-appx:///` merge) —
this XAML draft's `xmlns:c` inline-namespace approach for `BoolToVisibilityConverter`
avoids the question entirely by declaring the converter directly rather than merging a
dictionary; keep this approach unless a future task establishes a cleaner app-wide
merge story.

- [ ] **Step 2: Port `WindowSelectorWindow.xaml.cs`**

```csharp
// winui3/WinTabberUI/Views/WindowSelectorWindow.xaml.cs
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.System;
using WinTabber.ViewModels;
using WinTabberUI.Windowing;
using WinUIEx;

namespace WinTabberUI.Views;

public sealed partial class WindowSelectorWindow : WindowEx
{
    private readonly nint _hwnd;
    private Rect? _screenBounds;
    private bool _parked;
    private int _framesBeforeReveal;

    public WindowSelectorViewModel ViewModel { get; }

    public WindowSelectorWindow(WindowSelectorViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        SystemBackdrop = new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop();
        _hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        RootGrid.SizeChanged += (_, _) => CenterWindow();
        Activated += (_, _) => { ScaleTiles(); CenterWindow(); };
        Closed += (_, _) => DisarmReveal();
    }

    /// <summary>
    /// Frames still to be composed before revealing. Two rather than one because
    /// CompositionTarget.Rendering is raised while a frame is still being built, not after it has
    /// been presented -- ported from the WPF original's identical comment; this plan confirmed
    /// Microsoft.UI.Xaml.Media.CompositionTarget.Rendering exists with the identical shape via a
    /// real compile, but its runtime timing semantics (does it fire mid-composition here too, or
    /// does the WinUI 3 compositor make this unnecessary) still needs live verification -- see the
    /// self-review note below.
    /// </summary>
    private void ArmReveal()
    {
        _parked = true;
        _framesBeforeReveal = 2;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= RevealWhenComposed;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += RevealWhenComposed;

        // TODO(verify): WPF's fallback here was Dispatcher.BeginInvoke(DispatcherPriority.Render, RevealNow) --
        // a safety net in case Rendering never fires for this open. Confirm DispatcherQueue's
        // priority enum has an equivalent "after layout/render, before input" priority
        // (Microsoft.UI.Dispatching.DispatcherQueuePriority has High/Normal/Low, not WPF's
        // fine-grained Render-specific priority) before deciding whether this fallback still needs
        // a post at all, or whether WinUI 3's compositor makes the original flicker this guards
        // against structurally impossible (its stale-frame problem was specific to WPF's render
        // thread reusing a Window's surface across Show/Hide; confirm whether a WinUI 3 Window's
        // Show/Hide has the same surface-reuse behavior before assuming the fallback is still
        // needed at all).
    }

    private void DisarmReveal()
    {
        _parked = false;
        _framesBeforeReveal = 0;
        Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= RevealWhenComposed;
    }

    private void RevealWhenComposed(object? sender, object e)
    {
        if (--_framesBeforeReveal > 0)
        {
            return;
        }

        RevealNow();
    }

    private void RevealNow()
    {
        if (!_parked)
        {
            return;
        }

        DisarmReveal();
        CenterWindow();
    }

    private const double FillPercent = 0.8;

    private void ScaleTiles()
    {
        // TODO(verify): WindowTileWidth/ScaleFactor read from WinTabber.ViewModels' SettingsViewModel
        // via the same ApplicationSettings singleton Task 4b.1's DI graph already provides -- confirm
        // the exact property path (this draft assumes AppearanceSettings.WindowTileWidth/ScaleFactor,
        // matching the WPF original's `_settings.Appearance.*`) against the real registered
        // ApplicationSettings type before finalizing.
    }

    private Rect GetScreenBounds()
    {
        if (_screenBounds is { } cached)
        {
            return cached;
        }

        var cursorScreenBounds = ViewModel.CursorScreen.Bounds;
        var rect = DesktopHelper.ToLogicalBounds(_hwnd, new System.Drawing.Rectangle(
            cursorScreenBounds.X, cursorScreenBounds.Y, cursorScreenBounds.Width, cursorScreenBounds.Height));
        var logical = new Rect(rect.X, rect.Y, rect.Width, rect.Height);

        _screenBounds = logical;
        return logical;
    }

    private void ApplyScreenBounds()
    {
        var bounds = GetScreenBounds();
        MaxHeight = bounds.Height * FillPercent;
        MaxWidth = bounds.Width * FillPercent;
    }

    private void CenterWindow()
    {
        if (_parked)
        {
            return;
        }

        var bounds = GetScreenBounds();
        var scale = DesktopHelper.GetScaleForWindow(_hwnd);
        var x = bounds.Left + (bounds.Width - Bounds.Width) / 2;
        var y = bounds.Top + (bounds.Height - Bounds.Height) / 2;

        AppWindow.Move(new Windows.Graphics.PointInt32((int)(x * scale), (int)(y * scale)));
    }

    public void ShowWindowSelector()
    {
        _screenBounds = null;
        var bounds = GetScreenBounds();

        ScaleTiles();
        ApplyScreenBounds();

        TabListView.SuppressHoverUntilPointerMoves();

        var scale = DesktopHelper.GetScaleForWindow(_hwnd);
        AppWindow.Move(new Windows.Graphics.PointInt32(
            (int)(bounds.Left * scale), (int)((bounds.Top - bounds.Height) * scale)));

        ArmReveal();
        Activate();
        TabListView.Focus(FocusState.Programmatic);
    }

    public void SwitchWindowAndClose()
    {
        if (ViewModel.SelectedIndex >= 0 && ViewModel.SelectedIndex < ViewModel.WindowItems.Length)
        {
            ViewModel.SelectedItem?.WindowRef.Activate();
        }

        ViewModel.EndPreview();
        ViewModel.NotifySwitcherClosed();
        this.Hide();
    }

    private void OnTileGridPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsUnderEditableTextBlock(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (sender is FrameworkElement { DataContext: WindowItem clicked })
        {
            ViewModel.SelectedItem = clicked;
        }

        SwitchWindowAndClose();
    }

    private static bool IsUnderEditableTextBlock(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is EditableTextBlock)
            {
                return true;
            }

            node = VisualTreeHelper.GetParent(node);
        }

        return false;
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.Key)
        {
            case VirtualKey.Enter:
                e.Handled = true;
                ViewModel.CommitSelection();
                this.Hide();
                return;
            case VirtualKey.Escape:
                e.Handled = true;
                ViewModel.CancelSelection();
                this.Hide();
                return;
        }
    }
}
```

Several real gaps left as explicit `TODO(verify)` markers above, per this task's own
draft status — resolve each against real compiler/runtime output, not assumption,
before this task is done:

1. **`RevealWhenComposed`'s event-arg type** — WPF's `CompositionTarget.Rendering` is
   `EventHandler`; confirm whether WinUI 3's is the same delegate shape or something
   else (the draft above uses `object e` defensively; fix to the real type once known).
2. **The `Dispatcher.BeginInvoke(DispatcherPriority.Render, ...)` fallback's WinUI 3
   equivalent**, and whether WinUI 3's Window reuse even needs it (see the inline note).
3. **`ScaleTiles`'s exact settings property path.**
4. **The `OnKeyDown` "editor has focus, don't intercept" guard** — the WPF original
   checked `Keyboard.FocusedElement is TextBox` before committing/cancelling via
   Enter/Escape, so a rename-in-progress doesn't get swallowed by the switcher's own
   handler. `WindowEx`/`Window`-level `OnKeyDown` needs the WinUI 3 equivalent focus
   check (`FocusManager.GetFocusedElement(this.Content.XamlRoot) is TextBox`, or
   similar) before this task is done — dropping it silently would reintroduce the
   exact bug class `EditableTextBlock`'s `KeyDown` handler (Task 4b.2) already
   handles at its own level, but the window-level guard still matters if focus
   somehow isn't on the `TextBox` itself.
5. **The dimming converter/`WindowItem.IsDimmed` design decision** (Step 1's note).
6. **`WindowRef.Activate()`'s exact call shape** — confirm against the real
   `WinTabber.Api.Windowing.WindowRef` type (already WPF-free, used elsewhere in this
   migration) rather than assumed from the WPF original's `.Activate()` call.

Given how many of the WPF original's own comments document already-fixed timing bugs
from real user-facing symptoms (the selection "jump" `HoverSelect` exists to prevent,
the tile-reshuffle flicker `ArmReveal` exists to prevent), **expect this task to need
at least one fix round found via real execution**, the same as Tasks 4a.3/4a.4 — do not
treat a clean build as sufficient evidence this window behaves correctly.

- [ ] **Step 3: Register the window in DI**

```csharp
// winui3/WinTabberUI/Bootstrapper.cs — add to AddWindowSelectorGraph (Task 4b.1)
.AddTransient<Views.WindowSelectorWindow>();
```

- [ ] **Step 4: Build and verify real UI Automation evidence**

Run: `dotnet build WinTabber.slnx` — resolve every `TODO(verify)` above against real
compiler output first. Then, following Task 4a.4/4a.5's established verification
method (temporarily wire `App.xaml.cs` to launch `WindowSelectorWindow` and call
`ShowWindowSelector()`, reverted before commit): confirm via UI Automation that (a)
real window tiles render with real thumbnails and real titles for actual open windows
on the system, (b) arrow-key navigation actually moves selection between tiles in the
expected spatial direction, (c) hovering a tile after a genuine pointer move selects
it, but the initial reveal does not spuriously select whatever tile happens to be
under a stationary cursor (the exact bug `HoverSelect`/`SuppressHoverUntilPointerMoves`
exists to prevent — this is the single most important behavior to verify live, since
it has no compile-time signal at all if the redesign is wrong), (d) Enter/Escape
commit/cancel and close the window, (e) no stale-frame flicker is visible across at
least two consecutive opens (the `ArmReveal` hack's whole reason to exist) — if this
cannot be observed reliably via UI Automation alone, screenshot evidence across two
opens is the fallback, matching Task 4a.4's `PrintWindow`-based verification method.

- [ ] **Step 5: Commit**

```bash
git add winui3/WinTabberUI/Views/WindowSelectorWindow.xaml winui3/WinTabberUI/Views/WindowSelectorWindow.xaml.cs \
  winui3/WinTabberUI/Bootstrapper.cs
git commit -m "feat: port WindowSelectorWindow to WinUI3

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>"
```

---

## Phase 4c onward — scope note

`WindowSelectorWindow` (Phase 4b) was the last of the three windows this plan's
original Phase 4 split identified as needing dedicated research passes beyond the
mechanical Phase 4a pair. Two pieces of Phase 4's original scope remain, in the order
the original Phase 4 note recommended:

- **`ThumbnailWindow`** — blocked on researching WinUI 3's `WM_NCHITTEST`/`WM_SIZING`
  subclassing mechanism (`SetWindowSubclass`/`SetWindowLongPtr(GWLP_WNDPROC)`,
  real and documented, but CsWin32 metadata coverage for it was never checked in this
  plan) for the hand-tuned resize-grab hit-testing WPF's `HwndSource.AddHook` gave it.
  Not otherwise blocked — `WindowThumbnail` itself (this window's core control) is
  already ported and proven (Task 4a.3/4a.4).
- **`MediaControlsWindow`** — not yet read at all. Flagged early in this migration's
  design spec as large and complex; should not be assumed simpler than
  `WindowSelectorWindow` turned out to be just because it hasn't been opened. This is
  also where the temporary `StubMediaControlsStateService` (Task 4b.1) gets replaced
  with the real, ported `MediaControlsStateService` — read that stub's doc comment
  first when starting this window's research.

Also carried forward from Phase 4a's final review: I3 (non-activating show-path timing)
remains open — still not exercised, since `WindowSelectorWindow` uses `ShowActivated`-equivalent
activation like `DockWindow`, not a non-activating show path. M8 (`Bootstrapper` grouping naming)
was settled during the Phase 4c `ThumbnailWindow` task: kept this port's per-window/feature
grouping (`AddSettingsGraph`, `AddDockAndSuspendedWindowsGraph`, `AddWindowSelectorGraph`,
`AddThumbnailWindowGraph`), documented at the new method rather than switching to the WPF
original's per-kind grouping. Not open any longer.

The deferred hint-overlay system (Phase 2c) remains untouched and unresearched since
its own scope note.
