using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.Text;

namespace WinTabber.UI.Common.AccessKeys;

/// <summary>
/// A custom access-key badge matching the WPF app's HintAdorner look (rounded rect, accent-color
/// fill, bold text, underlined typed prefix), since WinUI 3's default key-tip badge cannot be
/// restyled -- see docs/superpowers/specs/2026-09-23-access-key-custom-rendering-winui3-design.md.
/// Built in code, not XAML, since instances are created and destroyed dynamically per active hint.
/// </summary>
internal sealed class AccessKeyBadge
{
    private const double PaddingX = 5;
    private const double PaddingY = 4;
    private const double CornerRadiusValue = 4;
    private const double FontSizeValue = 10;

    // Fallbacks only reached if the app-level resource dictionary is ever missing one of these
    // keys (should not happen in a running app -- every default control style already depends on
    // them -- but a direct indexer lookup throws on a miss with nothing above this constructor to
    // catch it, so TryGetResource below degrades to a plain, still-visible color instead).
    private static readonly Color FallbackFill = Color.FromArgb(255, 0, 120, 215);
    private static readonly Color FallbackBorder = Color.FromArgb(255, 0, 84, 153);

    private readonly Border _border;
    private readonly TextBlock _textBlock;
    private string _fullText = "";

    public AccessKeyBadge(string text)
    {
        _textBlock = new TextBlock
        {
            FontSize = FontSizeValue,
            FontWeight = new Windows.UI.Text.FontWeight { Weight = 700 },
            Foreground = TryGetResource<Brush>("TextOnAccentFillColorPrimaryBrush")
                ?? new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)),
        };

        _border = new Border
        {
            CornerRadius = new CornerRadius(CornerRadiusValue),
            Padding = new Thickness(PaddingX, PaddingY, PaddingX, PaddingY),
            // SystemAccentColorLight2/Dark1 are Color resources, not Brush resources -- verified
            // against the SDK's own generic.xaml (e.g. AcrylicBrush.TintColor="{ThemeResource
            // SystemAccentColorDark1}"). Wrap each in a SolidColorBrush explicitly.
            Background = new SolidColorBrush(TryGetColorResource("SystemAccentColorLight2") ?? FallbackFill),
            BorderBrush = new SolidColorBrush(TryGetColorResource("SystemAccentColorDark1") ?? FallbackBorder),
            BorderThickness = new Thickness(0.5),
            IsHitTestVisible = false,
            Child = _textBlock,
        };

        _fullText = text;
        Render(0);
    }

    public UIElement Visual => _border;

    private static T? TryGetResource<T>(string key)
        where T : class
    {
        return Application.Current.Resources.TryGetValue(key, out var value) ? value as T : null;
    }

    // Color is a value type -- "as T" in TryGetResource<T> above only works for reference types,
    // so a boxed Color resource needs its own lookup rather than sharing that generic helper.
    private static Color? TryGetColorResource(string key)
    {
        return Application.Current.Resources.TryGetValue(key, out var value) && value is Color color
            ? color
            : null;
    }

    /// <summary>
    /// Redraws the badge's text with the first <paramref name="underlineLength"/> characters
    /// underlined -- the typed portion of the access key so far -- and the rest plain, matching
    /// HintAdorner.OnRender's SetTextDecorations(Underline, 0, _selectionLength). -1 hides the
    /// badge entirely (the typed input has diverged from this element's own key), matching
    /// HintAdorner.OnInput's _selectionLength = -1 early return.
    /// </summary>
    public void UpdatePrefix(int underlineLength)
    {
        if (underlineLength < 0)
        {
            _border.Visibility = Visibility.Collapsed;
            return;
        }

        _border.Visibility = Visibility.Visible;
        Render(underlineLength);
    }

    /// <summary>Redraws _textBlock's Inlines for the current _fullText, underlining the first
    /// underlineLength characters. Takes no text parameter and never assigns _fullText -- setting
    /// the text (constructor only) and rendering it (constructor and every later UpdatePrefix
    /// call) are kept as two separate jobs.</summary>
    private void Render(int underlineLength)
    {
        _textBlock.Inlines.Clear();

        if (underlineLength <= 0)
        {
            _textBlock.Inlines.Add(new Run { Text = _fullText });
            return;
        }

        // TextBlock.TextDecorations applies to the whole string -- there is no WinUI 3 equivalent
        // of WPF's FormattedText.SetTextDecorations(decorations, start, length) on a plain Text
        // property. Splitting into two Runs, only the first carrying the decoration, reproduces
        // the same partial-underline effect.
        _textBlock.Inlines.Add(new Run
        {
            Text = _fullText[..underlineLength],
            TextDecorations = TextDecorations.Underline,
        });
        if (underlineLength < _fullText.Length)
        {
            _textBlock.Inlines.Add(new Run { Text = _fullText[underlineLength..] });
        }
    }

    /// <summary>
    /// Pure logic, unit-tested independent of any visual: how much of <paramref name="accessKey"/>
    /// to underline given what has been typed so far, or -1 if <paramref name="pressedKeys"/> is
    /// not a prefix of it (matching HintAdorner.OnInput's HintText.StartsWith(currentInput) check).
    /// </summary>
    internal static int ComputeUnderlineLength(string accessKey, string pressedKeys)
    {
        return accessKey.StartsWith(pressedKeys, StringComparison.OrdinalIgnoreCase)
            ? pressedKeys.Length
            : -1;
    }
}
