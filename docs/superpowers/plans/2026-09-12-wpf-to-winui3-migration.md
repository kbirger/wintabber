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

## Phase 2 onward — scope note

Phases 2 through 7 (porting `WinTabber.UI.Common`'s behaviors/controls, converting all six windows, tray icon and bootstrap parity, the icon-mapping pass, and final verification) are **not detailed task-by-task in this document**. Producing bite-sized, no-placeholder tasks with real, verified code for that work requires reading several files in full that were not read while writing this plan — most importantly `WinTabber.UI.Common/Controls/SpatialNavigationListView.cs`, `WindowThumbnail.cs`, `ShortcutCaptureBox.cs`, `ShortcutPresenter.cs`, and `WinTabber.UI.Common/Behaviors/HintBehavior.cs`, all of which the switcher, dock, and media-controls windows depend on directly. Writing conversion code against these without having read them would violate this skill's "No Placeholders" rule in substance even if not in form — it would be fabricated, unverified code presented as ready to commit.

**Before continuing past Phase 1**, read those files in full, then write Phases 2–7 as a continuation of this plan (or a follow-on plan document), using the same task structure established above. The design spec (`docs/superpowers/specs/2026-09-12-wpf-to-winui3-migration-design.md`) already fixes the phase boundaries, the backdrop-per-window table, and the control-mapping rules — that scoping work does not need to be redone, only the file-level task breakdown for Phases 2–7.

**Architecture gap found during final review, unaddressed by Phases 0–1:** `WinTabber.Api.Media` and `WinTabber.Infrastructure` are not actually fully UI-framework-agnostic, despite this plan's Architecture section describing them that way. `WinTabber.Api.Media/ShellApplications/Models/InstalledApplicationInfo.cs:11` declares `public required IObservable<ImageSource> Icon { get; init; }` using WPF's `System.Windows.Media.ImageSource`, and `WinTabber.Infrastructure/AppCache.cs` uses `System.Windows.Media.Imaging` types directly, which is why `WinTabber.Infrastructure.csproj` still carries `<UseWPF>true</UseWPF>`. Because `AggregateSession` (moved into `WinTabber.ViewModels` by Task 1.2) exposes a public `InstalledApplicationInfo App` property, a WPF type is reachable through `WinTabber.ViewModels`'s public surface today — it only compiles clean because `winui3/WinTabberUI` has no real code yet exercising that path. Phase 2 should open with a task to de-WPF `InstalledApplicationInfo.Icon` (e.g. to `IObservable<Stream>` or a new per-UI-framework `IIconSource` abstraction) and `AppCache`'s imaging code, before any winui3 media-related conversion work begins.
