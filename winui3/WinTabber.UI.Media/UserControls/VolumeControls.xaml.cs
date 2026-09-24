using Microsoft.UI.Xaml.Controls;
using WinTabber.UI.Common.AccessKeys;

namespace WinTabber.UI.Media.UserControls;

public sealed partial class VolumeControls : UserControl
{
    public VolumeControls()
    {
        InitializeComponent();
    }

    /// <summary>Registers this control's two hinted elements with a window's badge layer -- called
    /// once per instance from the owning window, since AccessKeyDisplayRequested/Dismissed do not
    /// bubble (see docs/superpowers/specs/2026-09-23-access-key-custom-rendering-winui3-design.md),
    /// so the window cannot discover them on its own.</summary>
    public void RegisterAccessKeyBadges(AccessKeyBadgeLayer layer)
    {
        layer.Watch(VolumeSlider);
        layer.Watch(MuteToggleButton);
    }
}
