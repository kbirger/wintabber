using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinTabber.API
{
    public class WindowRef(int handle, WindowProcessRef process) : IEquatable<WindowRef>
    {
        public int Handle { get; } = handle;

        public string Title
        {
            get
            {
                return Process.WindowManager.Interop.GetWindowTitle(Handle);
            }
        }

        public WindowProcessRef Process { get; } = process;
        public override bool Equals(object? obj)
        {
            return Equals(obj as WindowRef);
        }
        public bool Equals(WindowRef? other)
        {
            return other is not null &&
                   Handle == other.Handle;
        }
        public override int GetHashCode()
        {
            return HashCode.Combine(Handle, Process);
        }
        public static bool operator ==(WindowRef? left, WindowRef? right)
        {
            return EqualityComparer<WindowRef>.Default.Equals(left, right);
        }
        public static bool operator !=(WindowRef? left, WindowRef? right)
        {
            return !(left == right);
        }

        public void BringToFront()
        {
            Process.WindowManager.Interop.BringWindowToFront(Handle);
        }

        public void Maximize()
                    {
            Process.WindowManager.Interop.MaximizeWindow(Handle);
        }

        public void Minimize()
        {
            Process.WindowManager.Interop.MinimizeWindow(Handle);
        }

   

        public void Preview(nint handleToSpare)
        {
            Process.WindowManager.Interop.ActivateLivePreview(Handle, handleToSpare);
        }


    }
}
