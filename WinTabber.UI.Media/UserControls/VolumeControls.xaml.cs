using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WinTabber.UI.Media.UserControls;
/// <summary>
/// Interaction logic for VolumeControls.xaml
/// </summary>
public partial class VolumeControls : UserControl
{
    public VolumeControls()
    {
        InitializeComponent();
    }

    //// CanSetVolume Dependency Property
    //public static readonly DependencyProperty CanSetVolumeProperty =
    //    DependencyProperty.Register(
    //        nameof(CanSetVolume),
    //        typeof(bool),
    //        typeof(VolumeControls),
    //        new PropertyMetadata(true));

    //public bool CanSetVolume
    //{
    //    get => (bool)GetValue(CanSetVolumeProperty);
    //    set => SetValue(CanSetVolumeProperty, value);
    //}

    //// Volume Dependency Property
    //public static readonly DependencyProperty VolumeProperty =
    //    DependencyProperty.Register(
    //        nameof(Volume),
    //        typeof(float),
    //        typeof(VolumeControls),
    //        new PropertyMetadata(0f));

    //public float Volume
    //{
    //    get => (float)GetValue(VolumeProperty);
    //    set => SetValue(VolumeProperty, value);
    //}

    //// CanMute Dependency Property
    //public static readonly DependencyProperty CanMuteProperty =
    //    DependencyProperty.Register(
    //        nameof(CanMute),
    //        typeof(bool),
    //        typeof(VolumeControls),
    //        new PropertyMetadata(true));

    //public bool CanMute
    //{
    //    get => (bool)GetValue(CanMuteProperty);
    //    set => SetValue(CanMuteProperty, value);
    //}

    //// IsMuted Dependency Property
    //public static readonly DependencyProperty IsMutedProperty =
    //    DependencyProperty.Register(
    //        nameof(IsMuted),
    //        typeof(bool),
    //        typeof(VolumeControls),
    //        new PropertyMetadata(false));

    //public bool IsMuted
    //{
    //    get => (bool)GetValue(IsMutedProperty);
    //    set => SetValue(IsMutedProperty, value);
    //}

    //// ToggleMute Dependency Property
    //public static readonly DependencyProperty ToggleMuteProperty =
    //    DependencyProperty.Register(
    //        nameof(ToggleMute),
    //        typeof(ICommand),
    //        typeof(VolumeControls),
    //        new PropertyMetadata(null));

    //public ICommand ToggleMute
    //{
    //    get => (ICommand)GetValue(ToggleMuteProperty);
    //    set => SetValue(ToggleMuteProperty, value);
    //}

    //// VolumeHintText Dependency Property
    //public static readonly DependencyProperty VolumeHintTextProperty =
    //    DependencyProperty.Register(
    //        nameof(VolumeHintText),
    //        typeof(string),
    //        typeof(VolumeControls),
    //        new PropertyMetadata(string.Empty));

    //public string VolumeHintText
    //{
    //    get => (string)GetValue(VolumeHintTextProperty);
    //    set => SetValue(VolumeHintTextProperty, value);
    //}

    //// MuteHintText Dependency Property
    //public static readonly DependencyProperty MuteHintTextProperty =
    //    DependencyProperty.Register(
    //        nameof(MuteHintText),
    //        typeof(string),
    //        typeof(VolumeControls),
    //        new PropertyMetadata(string.Empty));

    //public string MuteHintText
    //{
    //    get => (string)GetValue(MuteHintTextProperty);
    //    set => SetValue(MuteHintTextProperty, value);
    //}

}



