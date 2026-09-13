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
