using System.Threading.Channels;

namespace WinTabber.API
{
    public abstract class WindowOwner
    {

        protected WindowOwner() { }
        protected WindowOwner(WindowManager windowManager)
        {
            WindowManager = windowManager;
        }
        protected abstract void AssertOwnsWindow(WindowRef window);
        public abstract WindowRef[] GetWindows();
        public WindowManager WindowManager { get; init; }
        
        internal int IndexOf(WindowRef window)
        {
            AssertOwnsWindow(window);
            var currentWindows = GetWindows();
            return IndexOf(window, currentWindows);
        }
        internal int Nextlndex(WindowRef window)
        {
            AssertOwnsWindow(window);
            var currentWindows = GetWindows();
            return NextIndex(window, currentWindows);
        }
        public WindowRef NextWindow(WindowRef window)
        {
            AssertOwnsWindow(window);
            var currentWindows = GetWindows();
            return NextWindow(window, currentWindows);
        }
        internal int PreviousIndex(WindowRef window)
        {
            AssertOwnsWindow(window);
            var currentWindows = GetWindows();
            return PreviousIndex(window, currentWindows);
        }

        public WindowRef PreviousWindow(WindowRef window)
        {
            AssertOwnsWindow(window);
            var currentWindows = GetWindows();
            return PreviousWindow(window, currentWindows);
        }
        private int IndexOf(WindowRef window, WindowRef[] windows)
        {
            return Array.IndexOf(windows, window);
        }

        private int NextIndex(WindowRef window, WindowRef[] windows)
        {
            var currentIndex = IndexOf(window, windows);

            return (currentIndex + 1) % windows.Length;
        }

        private WindowRef NextWindow(WindowRef window, WindowRef[] windows)
        {
            AssertOwnsWindow(window);
            var nextIndex = NextIndex(window, windows);
            return windows[nextIndex];
        }

        private int PreviousIndex(WindowRef window, WindowRef[] windows)
        {
            AssertOwnsWindow(window);

            var currentIndex = IndexOf(window, windows);
            return (currentIndex - 1 + windows.Length) % windows.Length;
        }

        private WindowRef PreviousWindow(WindowRef window, WindowRef[] windows)
        {
            AssertOwnsWindow(window);
            var previousIndex = PreviousIndex(window, windows);
            return windows[previousIndex];
        }

    }
}
