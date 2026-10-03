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
