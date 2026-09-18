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
    /// opens.
    /// </summary>
    public static void AttachSequentialKeys(ComboBox owner, Action<ComboBoxItem, int> onActivated)
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
