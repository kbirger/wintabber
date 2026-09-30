using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace WinTabber.UI.Common.AccessKeys;

/// <summary>
/// Assigns a sequential digit AccessKey ("1", "2", "3", ...) to each item container of a
/// ComboBox's drop-down as it is realized, and routes activation through onActivated. ComboBox
/// has no ContainerContentChanging (confirmed unavailable on this type in the Windows App SDK),
/// so containers are only reachable via ContainerFromIndex once DropDownOpened fires.
/// </summary>
public static class DynamicAccessKeyScope
{
    /// <summary>
    /// ExitDisplayModeOnAccessKeyInvoked=false plus the explicit EnterDisplayMode call keep the
    /// access-key badge/chord session continuous across the transition from the ComboBox's own key
    /// into its items' keys -- without both, the user must press Alt a second time after the
    /// drop-down opens. The HashSet dedup is required because DropDownOpened fires on every open and
    /// containers can be reused; without it, AccessKeyInvoked handlers would stack across repeated
    /// opens. Item badges use the SAME badgeLayer.Watch(container) call any static element uses --
    /// no special popup-aware path needed, since badges now draw on a separate overlay window that
    /// sits above the drop-down regardless (see
    /// docs/superpowers/specs/2026-09-28-access-key-overlay-window-design.md).
    /// </summary>
    public static void AttachSequentialKeys(
        ComboBox owner,
        AccessKeyBadgeLayer badgeLayer,
        Action<ComboBoxItem, int> onActivated
    )
    {
        owner.IsAccessKeyScope = true;
        owner.ExitDisplayModeOnAccessKeyInvoked = false;

        var wired = new HashSet<ComboBoxItem>();

        // Set only when the owner's own access key opened the drop-down, so a mouse or arrow-key
        // open does not start an access-key display session the user never asked for.
        var openedByAccessKey = false;
        owner.AccessKeyInvoked += (_, _) => openedByAccessKey = true;

        owner.DropDownOpened += (_, _) =>
        {
            var continueChord = openedByAccessKey;
            openedByAccessKey = false;

            owner.DispatcherQueue.TryEnqueue(() =>
            {
                for (int i = 0; i < owner.Items.Count; i++)
                {
                    if (owner.ContainerFromIndex(i) is not ComboBoxItem container)
                    {
                        continue;
                    }

                    container.AccessKey = (i + 1).ToString();

                    if (!wired.Add(container))
                    {
                        continue;
                    }

                    badgeLayer.Watch(container);
                    container.AccessKeyInvoked += (_, args) =>
                    {
                        onActivated(container, owner.IndexFromContainer(container));
                        args.Handled = true;
                    };
                }

                if (continueChord)
                {
                    // EnterDisplayMode alone is a no-op here -- display mode is already on (the
                    // user's own Alt press turned it on before this ComboBox's key was pressed), so
                    // the framework never re-scans and never asks the newly-assigned item keys to
                    // display (confirmed live: zero AccessKeyDisplayRequested events ever fired for
                    // any ComboBoxItem without this). Exiting first forces a re-scan on entry, which
                    // does ask every element in scope -- root elements included, which is why they
                    // blink off and back on for one frame; accepted trade-off, see the plan/spec.
                    AccessKeyManager.ExitDisplayMode();
                    AccessKeyManager.EnterDisplayMode(owner.XamlRoot);
                }
            });
        };
    }
}
