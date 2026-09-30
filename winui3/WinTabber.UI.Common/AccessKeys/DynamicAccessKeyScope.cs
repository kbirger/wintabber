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

        badgeLayer.OwnerHidden += () => owner.IsDropDownOpen = false;

        owner.DropDownOpened += (_, _) =>
        {
            var continueChord = openedByAccessKey;
            openedByAccessKey = false;

            owner.DispatcherQueue.TryEnqueue(() =>
            {
                var anyNewlyWired = false;
                for (int i = 0; i < owner.Items.Count; i++)
                {
                    if (owner.ContainerFromIndex(i) is not ComboBoxItem container)
                    {
                        continue;
                    }

                    container.AccessKey = (i + 1).ToString();
                    // Tells the framework these items belong to the ComboBox's access-key scope even
                    // though they live in a Popup, outside its visual tree -- IsAccessKeyScope alone
                    // does not cover Popup content. With this set, the framework correctly dismisses
                    // root-level badges and requests only these item badges on its own, no manual
                    // Exit/Enter needed (confirmed live -- see this plan's ledger for the spike that
                    // established this).
                    container.AccessKeyScopeOwner = owner;

                    if (!wired.Add(container))
                    {
                        continue;
                    }

                    anyNewlyWired = true;
                    badgeLayer.Watch(container);
                    container.AccessKeyInvoked += (_, args) =>
                    {
                        onActivated(container, owner.IndexFromContainer(container));
                        args.Handled = true;
                    };
                }

                if (continueChord && anyNewlyWired)
                {
                    // AccessKeyScopeOwner alone handles every open correctly EXCEPT the very first
                    // time a given container is realized -- the framework has not yet built an
                    // internal scope-tree entry for it, so its access key doesn't get asked to
                    // display for several seconds. Forcing one display-mode refresh here closes that
                    // gap, and ONLY here: gating on anyNewlyWired (not a separate "first open" flag)
                    // means this never fires on a later open of the same, already-registered
                    // containers, where the framework's own scoping already responds within
                    // milliseconds on its own -- confirmed live; doing this unconditionally on every
                    // open would re-introduce root and item badges showing together, since re-entering
                    // display mode at all re-triggers the framework's own scope evaluation from
                    // scratch each time, and that evaluation is fast enough on an already-registered
                    // container to look identical to not having done it -- but is not proven safe to
                    // repeat on every single open, so it is intentionally limited to exactly the
                    // condition that needs it.
                    AccessKeyManager.ExitDisplayMode();
                    AccessKeyManager.EnterDisplayMode(owner.XamlRoot);
                }
            });
        };
    }
}
