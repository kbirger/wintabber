using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using WinTabberUI.ViewModels;
using WinTabberUI.ViewModels.Settings;

namespace WinTabberUI.Services
{
    public class UIScalingService : IDisposable
    {
        private readonly SettingsViewModel _settings;

        public UIScalingService(SettingsViewModel settings)
        {
            _settings = settings;
        }
        public void Dispose()
        {
            throw new NotImplementedException();
        }

        private System.Drawing.Point GetCursor() => Control.MousePosition;

        private Screen GetCursorScreen() => Screen.FromPoint(GetCursor());

        public System.Drawing.Point GetDeviceCenterScreen()
        {
            var screen = GetCursorScreen();
            return GetCenter(screen.Bounds);
        }

        public System.Drawing.Point GetCenter(Rectangle rect)
        {
            return new System.Drawing.Point(
                rect.X + rect.Width / 2, 
                rect.Y + rect.Height / 2
            );
        }

        public IObservable<Vector> GetCurrentScreenSize (Window window)
        {

            var source = PresentationSource.FromVisual(window);
            var transformToDevice = source.CompositionTarget.TransformToDevice;
            var transformtoDip = source.CompositionTarget.TransformFromDevice;
            return ObserveDpiChanges(window)
                .CombineLatest(
                    _settings.Appearance.WhenAnyValue(ap => ap.ScaleFactor),
                    _settings.Appearance.WhenAnyValue(ap => ap.ScaleToDpi)
                    )
                .Select(values =>
                {
                    var dpiEvent = values.First;
                    var scaleFactor = values.Second;
                    var scaleToDpi = values.Third;
                    var dpi = dpiEvent.EventArgs.NewDpi;
                    var bounds = GetCursorScreen();
                    var boundsVector = GetVector(bounds, scaleToDpi, transformtoDip);
                    boundsVector.X /= scaleFactor;
                    boundsVector.Y /= scaleFactor;
                    return boundsVector;
                });
        }

        private Vector GetVector(Screen bounds, bool scaleToDpi, Matrix transformtoDip)
        {
            return scaleToDpi switch
            {
                true =>
                    transformtoDip.Transform(
                        new Vector(bounds.Bounds.Width, bounds.Bounds.Height)
                    ),
                false => new Vector(bounds.Bounds.Width, bounds.Bounds.Height)
            };
        }

        private static IObservable<System.Reactive.EventPattern<System.Windows.DpiChangedEventHandler, System.Windows.DpiChangedEventArgs>> ObserveDpiChanges(Window window)
        {
            return Observable.FromEventPattern<System.Windows.DpiChangedEventHandler, System.Windows.DpiChangedEventArgs>(window, nameof(window.DpiChanged));
        }
    }
}
