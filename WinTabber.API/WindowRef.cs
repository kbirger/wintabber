using System.Diagnostics;
using System.Drawing;
using WinTabber.Interop;
using static WinTabber.Interop.WindowPlacement;

namespace WinTabber.API;

[DebuggerDisplay("{Process.Process.ProcessName} - {Title}", Name = "WindowRef")]
public partial class WindowRef : IEquatable<WindowRef>
{
    public WindowRef(int handle, WindowProcessRef process)
    {
        Handle = handle;
        Process = process;
    }
    public int Handle { get; }

    [Lazy]
    public bool GetIsValidUserWindow()
    {
        return IsVisible
            && IsTopLevel
            && !Style.ToolWindow
            && !Style.CannotBeActivated
            && !string.IsNullOrWhiteSpace(Title);
    }

    [Lazy]
    public bool GetIsVisible()
    {
        return Process.Manager.Interop.IsWindowVisible(Handle);
    }

    //public string Title
    //{
    //    get
    //    {
    //        return Process.WindowManager.Interop.GetWindowTitle(Handle);
    //    }
    //}

    [Lazy]
    public string GetTitle()
    {
        return Process.Manager.Interop.GetWindowTitle(Handle);
    }

    //public string Class
    //{
    //    get
    //    {
    //        return Process.WindowManager.Interop.GetClassName(Handle);
    //    }
    //}

    [Lazy]
    private string GetClass()
    {
        return Process.Manager.Interop.GetClassName(Handle);
    }

    public WindowProcessRef Process { get; }
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
        Process.Manager.Interop.BringWindowToFront(Handle);
    }

    public void Maximize()
    {
        Process.Manager.Interop.MaximizeWindow(Handle);
    }

    public void Minimize()
    {
        Process.Manager.Interop.MinimizeWindow(Handle);
    }

    //public WindowState State
    //{
    //    get
    //    {
    //        return Process.WindowManager.Interop.GetWindowState(Handle);
    //    }
    //}

    [Lazy]
    private WindowState GetState()
    {
        return Process.Manager.Interop.GetWindowState(Handle);
    }

    

    public Rectangle Bounds
    {
        get
        {
            var placement = Process.Manager.Interop.GetWindowPlacement(Handle);
            return placement.Bounds;
        }
    }

    public void MoveTo(Point point)
    {
        if (!Process.IsProcessElevated)
        {
            Process.Manager.Interop.MoveWindow(Handle, point);
        }
    }


    public void Preview(nint handleToSpare)
    {
        Process.Manager.Interop.ActivateLivePreview(Handle, handleToSpare);
    }

    public void SetTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return; // not allowed
        }
        Process.Manager.Interop.SetWindowText(Handle, title);
        Process.Manager.TitleStore.OverrideTitle(Handle, title);
    }

    //public bool IsTopLevel
    //{
    //    get
    //    {
    //        return Process.WindowManager.Interop.IsTopLevel(Handle);
    //    }
    //}

    //public WindowStyles Style
    //{
    //    get
    //    {
    //        return Process.WindowManager.Interop.GetWindowStyles(Handle);
    //    }
    //}

    [Lazy]
    private bool GetIsTopLevel()
    {
        return Process.Manager.Interop.IsTopLevel(Handle);
    }
    [Lazy]
    private WindowStyles GetStyle()
    {
        return Process.Manager.Interop.GetWindowStyles(Handle);

    }

}
