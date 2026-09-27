using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
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
    /// access-key badge/chord session continuous across the transition from the ComboBox's own
    /// key into its items' keys -- without both, the user must press Alt a second time after the
    /// drop-down opens. The HashSet dedup is required because DropDownOpened fires on every open
    /// and containers can be reused; without it, badges would be added to the drop-down panel
    /// again on every re-open, duplicating them. Item badges are added directly into the
    /// drop-down's own Popup panel via badgeLayer.SnapshotOpenPopups/InjectIntoNewPopup, not
    /// through badgeLayer.Watch -- a ComboBox drop-down is a light-dismiss Popup and always draws
    /// above a badge's own sibling Popup regardless of open order, so an item badge must live
    /// inside the SAME Popup as the drop-down to be visible over it. The snapshot/inject split
    /// (rather than a single bracketing call) exists because the drop-down's Popup opens
    /// asynchronously, driven by the framework, before DropDownOpened fires -- so the "before"
    /// snapshot must be captured earlier, at AccessKeyInvoked.
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
        // open does not start an access-key display session the user never asked for. The popup
        // snapshot is captured HERE, not in DropDownOpened below -- AccessKeyInvoked is the last
        // point before the drop-down's own Popup opens; DropDownOpened fires after it already has,
        // by which point there is nothing left to diff against (see AccessKeyBadgeLayer's
        // SnapshotOpenPopups/InjectIntoNewPopup doc comments for why this two-point split exists).
        var openedByAccessKey = false;
        IReadOnlyList<Popup>? popupsBeforeOpen = null;
        owner.AccessKeyInvoked += (_, _) =>
        {
            openedByAccessKey = true;
            popupsBeforeOpen = badgeLayer.SnapshotOpenPopups(owner);
        };

        owner.DropDownOpened += (_, _) =>
        {
            var continueChord = openedByAccessKey;
            var before = popupsBeforeOpen;
            openedByAccessKey = false;
            popupsBeforeOpen = null;

            owner.DispatcherQueue.TryEnqueue(() =>
            {
                Panel? dropDownPanel = null;
                if (continueChord && before != null)
                {
                    badgeLayer.InjectIntoNewPopup(before, panel => dropDownPanel = panel);
                }

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

                    container.AccessKeyInvoked += (_, args) =>
                    {
                        onActivated(container, owner.IndexFromContainer(container));
                        args.Handled = true;
                    };

                    if (dropDownPanel != null)
                    {
                        AccessKeyBadgeLayer.AddItemBadge(dropDownPanel, container);
                    }
                }

                if (continueChord)
                {
                    AccessKeyManager.EnterDisplayMode(owner.XamlRoot);
                }
            });
        };
    }
}
