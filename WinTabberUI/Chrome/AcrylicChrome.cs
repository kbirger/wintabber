using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Windows.Win32.Graphics.Dwm;

namespace WinTabberUI.Chrome;

public sealed class AcrylicChrome : WindowChrome
{
    #region DependencyProperty AllowStartUpFrozen
    public static DependencyProperty AllowStartUpFrozenProperty =
        DependencyProperty.Register("AllowStartUpFrozen", typeof(bool), typeof(AcrylicChrome),
            new FrameworkPropertyMetadata(false));


    public bool AllowStartUpFrozen
    {
        get => (bool)GetValue(AllowStartUpFrozenProperty);
        set => SetValue(AllowStartUpFrozenProperty, value);
    }
    #endregion

    internal bool IsAccentEnabled => AccentState != AccentState.ACCENT_DISABLED;

    #region Attached property AcrylicChrome
    [DefaultValue("Null")]
    public static readonly DependencyProperty AcrylicChromeProperty =
        DependencyProperty.RegisterAttached(
            "AcrylicChrome",
            typeof(AcrylicChrome),
            typeof(AcrylicChrome),
            new FrameworkPropertyMetadata(null, AcrylicChromePropertyChanged, AcrylicChromeCoerceValue));

    [AttachedPropertyBrowsableForType(typeof(Window))]
    public static AcrylicChrome GetAcrylicChrome(Window obj)
    {
        return (AcrylicChrome)obj.GetValue(AcrylicChromeProperty);
    }

    [AttachedPropertyBrowsableForType(typeof(Window))]
    public static void SetAcrylicChrome(Window obj, AcrylicChrome value)
    {
        obj.SetValue(AcrylicChromeProperty, value);
    }

    public static object AcrylicChromeCoerceValue(DependencyObject d, object baseValue)
    {

        if (DesignerProperties.GetIsInDesignMode(d))
        {
            return null;
        }

        if (baseValue == null) return null;

        if (!(d is Window)) throw new ArgumentException("d must be Window");

        if (!(baseValue is AcrylicChrome ch))
            throw new ArgumentException("baseValue must be AcrylicChrome");

        if (!ch.AllowStartUpFrozen && ch.IsFrozen)
        {
            return ch.CloneCurrentValue();
        }
        return baseValue;
    }

    [AttachedPropertyBrowsableForType(typeof(Window))]
    private static void AcrylicChromePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (DesignerProperties.GetIsInDesignMode(d))
        {
            return;
        }

        if (!(d is Window window)) throw new ArgumentException("d must be Window");
        var ch = e.NewValue as AcrylicChrome;
        if (ch == null) throw new ArgumentException("AeroGlass type expected ");

        ch.OwnerWindow = window;

        //associate initializer to window
        //if (ch.SuppressLagging)
        //{
        SetAcrylicInitializer(ch, window);
        //}
        //else
        //{
        //SetChromeInitializer(ch, window);
        //}

        if (!window.IsLoaded)
        {
            window.Loaded += WindowLoadedHandler;
        }
        else
        {
            if (!ch.IsInitialized)
                WindowLoadedHandler.Invoke(window, null);
        }
    }
    #endregion

    #region DependencyProperty UnderStratumColor
    public static DependencyProperty UnderStratumColorProperty =
        DependencyProperty.Register("UnderStratumColor", typeof(object), typeof(AcrylicChrome),
            new PropertyMetadata(ColorFrom.BlackOpacity, UnderStratumColorPropertyChangedCallback, UnderStratumColorPropertyCoerceValueCallback));

    /// <summary>
    /// Validate Color fpr 0x00000000 value
    /// </summary>
    /// <param name="d"></param>
    /// <param name="baseValue"></param>
    /// <returns></returns>
    private static object UnderStratumColorPropertyCoerceValueCallback(DependencyObject d, object baseValue)
    {
        //Color #0000 in user32 is SystemColors.WindowColor 
        return (baseValue == null || IsEqual(baseValue, ColorFrom.Zero))
            ? ColorFrom.BlackOpacity
            : ColorFrom.Object(baseValue);
    }

    public object UnderStratumColor
    {
        get => GetValue(UnderStratumColorProperty);
        set => SetValue(UnderStratumColorProperty, value);
    }

    public static void UnderStratumColorPropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (!(d is AcrylicChrome ch))
            throw new ArgumentException("CompositeChrome type expected");

        //if (ch.OwnerWindow == null) return;
        if (ch.HWndSource == null) return;

        var colorValue = ColorFrom.Object(e.NewValue);
        ch.gbrColor = colorValue.ToABGRhex();


        if (ch.DependencyOpacity != colorValue.A)
            ch.DependencyOpacity = colorValue.A;



        if (!ch.IsAccentEnabled) ch.DisableBlur();
        else ch.EnableBlur();

    }
    #endregion

    #region DependencyProperty RgbChannelProperty
    public static DependencyProperty RgbChannelProperty =
        DependencyProperty.Register("RgbChannel", typeof(object), typeof(AcrylicChrome),
            new PropertyMetadata("Black", RgbChannelPropertyChangedCallback, RgbChannelPropertyCoerceValueCallback));

    private static object RgbChannelPropertyCoerceValueCallback(DependencyObject d, object baseValue)
    {
        return (baseValue == null)
            ? Color.FromArgb(1, 0, 0, 0)
            : ColorFrom.Object(baseValue);
    }

    public object RgbChannel
    {
        get => GetValue(RgbChannelProperty);
        set => SetValue(RgbChannelProperty, value);
    }

    public static void RgbChannelPropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (!(d is AcrylicChrome ch)) throw new ArgumentException();

        var colorValue = ColorFrom.Object(e.NewValue);
        colorValue.A = ch.DependencyOpacity;

        if (!IsEqual(ch.UnderStratumColor, colorValue))
            ch.UnderStratumColor = colorValue;
    }
    #endregion

    #region DependencyProperty DependencyOpacity
    public static DependencyProperty DependencyOpacityProperty =
        DependencyProperty.Register("DependencyOpacity", typeof(byte), typeof(AcrylicChrome),
            new PropertyMetadata((byte)0, UnderStratumOpacityPropertyChangedCallback));

    public byte DependencyOpacity
    {
        get => (byte)GetValue(DependencyOpacityProperty);
        set => SetValue(DependencyOpacityProperty, value);
    }

    public static void UnderStratumOpacityPropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (!(d is AcrylicChrome ch)) throw new ArgumentException();

        var opacity = (byte)e.NewValue;
        var color = ColorFrom.Object(ch.UnderStratumColor);

        if (color.A == opacity) return;

        color.A = opacity;
        ch.UnderStratumColor = color;

    }
    #endregion

    /// <summary>
    /// Window Loaded handler
    /// </summary>
    private static readonly RoutedEventHandler WindowLoadedHandler = (sender, args) =>
    {
        if (!(sender is Window window)) return;

        if (!(window.GetValue(AcrylicChromeProperty) is AcrylicChrome chrome)) return;

        chrome.IsInitialized = true;

        chrome.OwnerWindow = window;
        chrome.HWndSource = (HwndSource)PresentationSource.FromVisual(window);

        chrome.HideFromPeek();
        var blurDesc =
            DependencyPropertyDescriptor.FromProperty(AccentStateProperty, typeof(AcrylicChrome));
        blurDesc.RemoveValueChanged(window, AccentStatePropertyChanged);
        blurDesc.AddValueChanged(window, AccentStatePropertyChanged);

        var accentState = (AccentState?)(window.GetValue(AccentStateProperty)) ?? (AccentState?)(chrome.GetValue(AccentStateProperty));

        if (accentState == AccentState.ACCENT_DISABLED) chrome.DisableBlur();
        else chrome.EnableBlur();

        chrome.SetCornerPreference();

        window.Loaded -= WindowLoadedHandler;
    };

    public void SetCornerPreference()
    {
        CornerHelper.SetCornerPreference(HWndSource.Handle, CornerPreference);
    }

    /// <summary>
    /// PropertyChanged Callback for Windows object
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private static void AccentStatePropertyChanged(object sender, EventArgs e)
    {
        if (!(sender is Window w)) return;

        if (!(w.GetValue(AcrylicChromeProperty) is AcrylicChrome ch))
            throw new ArgumentException("sender must be Window or AcrylicChrome");

        if (ch.HWndSource == null) return;

        //var accentState = (AccentState)w.GetValue(AccentStateProperty);//(bool)e.NewValue;
        if (ch.IsAccentEnabled)
        {
            ch.EnableBlur();
        }
        else
        {
            ch.DisableBlur();
        }
    }
    private static void SetAcrylicInitializer(AcrylicChrome ch, Window window)
    {
        //var initializer = AcrylicInitializer.GetAcrylicInitializer(window);
        //if (initializer == null)
        //{
        //    initializer = new AcrylicInitializer { ChromeBase = ch }; //initialize source
        //    AcrylicInitializer.SetAcrylicInitializer(window, initializer);
        //}
        //else
        //{
        //    initializer.ChromeBase = ch;
        //}
    }


    public void EnableBlur()
    {

        var hWnd = HWndSource.Handle;

        AccentHelper.EnableBlur(hWnd, AccentState, gbrColor);
        
    }

    public void HideFromPeek()
    {
        PeekHelper.HideFromPeek(HWndSource.Handle);
        PeekHelper.ExcludeFromPeek(HWndSource.Handle);
    }


    public void DisableBlur()
    {
        #region windows BUG fix
        //if (atFirstTime) //windows BUG fix
        //{
        //    atFirstTime = false;
        //    DisableBlur();
        //    EnableBlur();
        //}
        #endregion

        var hWnd = HWndSource.Handle;
        AccentHelper.DisableBlur(hWnd, gbrColor);

    }

    /// <summary>
    /// Current wWindow object
    /// </summary>
    public Window OwnerWindow { set; get; }

    /// <summary>
    /// Handle Source of current Window
    /// </summary>
    public HwndSource HWndSource { set; get; }

    public bool IsInitialized { set; get; }

    private uint gbrColor = (uint)UsingColors.Transparent;

    protected override Freezable CreateInstanceCore()
    {
        return (Freezable)Activator.CreateInstance(typeof(AcrylicChrome))!;
    }


    internal static readonly DependencyProperty CornerPreferenceProperty = DependencyProperty.Register(
        "CornerPreference",
        typeof(DWM_WINDOW_CORNER_PREFERENCE),
        typeof(AcrylicChrome),
        new PropertyMetadata(DWM_WINDOW_CORNER_PREFERENCE.DWMWCP_DEFAULT));

    internal DWM_WINDOW_CORNER_PREFERENCE CornerPreference
    {
        get => (DWM_WINDOW_CORNER_PREFERENCE)GetValue(CornerPreferenceProperty);
        set => SetValue(CornerPreferenceProperty, value);
    }

    internal static void CornerPreferencePropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (!(d is AcrylicChrome ch))
            throw new ArgumentException("CompositeChrome type expected");

        if (ch.OwnerWindow == null) return;
        if (ch.HWndSource == null) return;

        ch.SetCornerPreference();

    }

    public static readonly DependencyProperty AccentStateProperty = DependencyProperty.Register(
        "AccentState",
        typeof(AccentState),
        typeof(AcrylicChrome),
        new PropertyMetadata(AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND));

    public AccentState AccentState
    {
        get { return (AccentState)GetValue(AccentStateProperty); }
        set { SetValue(AccentStateProperty, value); }
    }

    //public static readonly DependencyProperty GradientColorProperty = DependencyProperty.Register(
    //    "GradientColor",
    //    typeof(object),
    //    typeof(AcrylicChrome),
    //    new PropertyMetadata(
    //        ColorFrom.BlackOpacity,
    //        GradientColorPropertyChangedCallback,
    //        GradientColorPropertyCoerceValueCallback)

    //    );

    //private static object GradientColorPropertyCoerceValueCallback(DependencyObject d, object baseValue)
    //{
    //    //Color #0000 in user32 is SystemColors.WindowColor 
    //    return (baseValue == null || IsEqual(baseValue, ColorFrom.Zero))
    //        ? ColorFrom.BlackOpacity
    //        : ColorFrom.Object(baseValue);
    //}

    //public static void GradientColorPropertyChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
    //{
    //    if (!(d is AcrylicChrome ch))
    //        throw new ArgumentException("CompositeChrome type expected");

    //    if (ch.OwnerWindow == null) return;
    //    if (ch.HWndSource == null) return;

    //    var colorValue = ColorFrom.Object(e.NewValue);
    //    ch.gbrColor = colorValue.ToABGRhex();


    //    //if (ch.DependencyOpacity != colorValue.A)
    //    //    ch.DependencyOpacity = colorValue.A;


    //    var accentState = (AccentState)ch.OwnerWindow.GetValue(AccentStateProperty);

    //    if (accentState == AccentState.ACCENT_DISABLED) ch.DisableBlur();
    //    else ch.EnableBlur();

    //}

    //public object GradientColor
    //{
    //    get { return GetValue(GradientColorProperty); }
    //    set { SetValue(GradientColorProperty, value); }
    //}


    public static bool IsEqual(object value, object eqValue)
    {
        return value?.Equals(eqValue) ?? eqValue == null;
    }
}


