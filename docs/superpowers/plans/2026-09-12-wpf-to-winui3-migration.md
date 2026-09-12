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

## Phase 2b onward — scope note

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
- **The shortcut-capture custom controls** —
  `WinTabber.UI.Common/Controls/ShortcutCaptureBox.cs`, `ShortcutChip.cs`,
  `ShortcutPresenter.cs`, plus their control templates in
  `WinTabber.UI.Common/Themes/Generic.xaml` (not read for this plan). These
  are custom `Control`-derived types with `[TemplatePart]`, which does port
  to WinUI 3, but `ShortcutCaptureBox` also uses `DispatcherTimer` (→
  `Microsoft.UI.Dispatching.DispatcherQueueTimer`, a real API change, not a
  rename), `Keyboard.Focus(this)` (→ `this.Focus(FocusState.Programmatic)`),
  and `ShortcutChip`'s `ShortcutChips.GetDisplayName` falls back to WPF's
  `System.Windows.Input.KeyInterop.KeyFromVirtualKey`, which has no direct
  WinUI 3 equivalent and needs either a replacement virtual-key-to-name
  table or confirmation that `ShortcutDisplayNames`'s canonical table already
  covers every key this app can bind (in which case the fallback may be
  droppable, not just portable — a design decision, not a translation).

Before writing Phase 2b, read `Hints/**` (all 7 files),
`WinTabber.UI.Common/HintAdorner.cs`, and
`WinTabber.UI.Common/Themes/Generic.xaml` in full, and research the actual
WinUI 3 replacement for `AdornerLayer`-based overlays and the UI Automation
invoke pattern before writing any task — the same "No Placeholders"
discipline that limited this pass to Phase 2's mechanical subset applies
there too.

`WinTabberUI/Controls/SpatialNavigationListView.cs` and
`WindowThumbnail.cs` (corrected location, see the note at the top of Phase
2) belong to the window-conversion phases (3–4), since they live in the app
project, not `WinTabber.UI.Common` — read them when writing Phase 3/4, not
Phase 2b.

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
Phase 2b (or Phase 3, whichever starts first) should open with a task to
de-WPF `InstalledApplicationInfo.Icon` (e.g. to `IObservable<Stream>` or a
new per-UI-framework `IIconSource` abstraction) and `AppCache`'s imaging
code, before any winui3 media-related conversion work begins. Still
unaddressed as of Phase 2.
