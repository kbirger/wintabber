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
    /// access-key badge/chord session continuous across the transition from the ComboBox's own
    /// key into its items' keys -- without both, the user must press Alt a second time after the
    /// drop-down opens. The HashSet dedup is required because DropDownOpened fires on every open
    /// and containers can be reused; without it, AccessKeyInvoked handlers stack across repeated
    /// opens. <paramref name="badgeLayer"/> gets the same <c>Watch</c> registration a static element
    /// would, since AccessKeyDisplayRequested/Dismissed do not bubble and a ComboBoxItem is
    /// otherwise invisible to it.
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
        owner.DropDownOpened += (_, _) =>
        {
            owner.DispatcherQueue.TryEnqueue(() =>
            {
                for (int i = 0; i < owner.Items.Count; i++)
                {
                    if (owner.ContainerFromIndex(i) is ComboBoxItem container)
                    {
                        container.AccessKey = (i + 1).ToString();
                        if (wired.Add(container))
                        {
                            // AccessKeyBadgeLayer only learns an element exists via Watch -- there
                            // is no bubbling to rely on here either, so a dynamically-realized
                            // ComboBoxItem needs the same explicit registration a static XAML
                            // element gets. Guarded by the same wired-dedup as AccessKeyInvoked
                            // just below it, for the same reason: DropDownOpened fires on every
                            // open and containers can be reused.
                            badgeLayer.Watch(container);
                            container.AccessKeyInvoked += (_, args) =>
                            {
                                onActivated(container, owner.IndexFromContainer(container));
                                args.Handled = true;
                            };
                        }
                    }
                }

                AccessKeyManager.EnterDisplayMode(owner.XamlRoot);
            });
        };
    }
}
