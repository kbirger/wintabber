using System.Diagnostics;
using System.Drawing;
using WinTabber.Interop;

namespace WinTabber.Api.Windowing.Tests.Fakes;

/// <summary>
/// Fake for <see cref="IWindowInterop"/> covering only what
/// <see cref="ApplicationRef.CloseAllWindows"/> exercises: elevation checks and the two close
/// paths. Every other member throws <see cref="NotSupportedException"/> — nothing in this
/// project's tests exercises the rest of this (large) interface, and a partial fake documents
/// that rather than silently no-opping.
/// </summary>
public sealed class FakeWindowInterop : IWindowInterop
{
    /// <summary>Process objects that should report as elevated. Compared by reference —
    /// <see cref="Process"/> doesn't override equality, so default <see cref="HashSet{T}"/>
    /// membership is already reference-based.</summary>
    public HashSet<Process> ElevatedProcesses { get; } = [];

    public List<int> ClosedHandles { get; } = [];
    public List<(ElevatedWindowAction Action, IReadOnlyList<int> Handles)> ElevatedActionCalls { get; } = [];

    public bool IsProcessElevated(Process process) => ElevatedProcesses.Contains(process);

    public void CloseWindow(int handle) => ClosedHandles.Add(handle);

    public void RunElevatedAction(ElevatedWindowAction action, IEnumerable<int> handles) =>
        ElevatedActionCalls.Add((action, handles.ToList()));

    public void BringWindowToFront(int handle) => throw new NotSupportedException();

    public IEnumerable<int> EnumerateProcessWindowHandles(Process process) =>
        throw new NotSupportedException();

    public void ForceForeground(int hWnd) => throw new NotSupportedException();

    public Process? GetForegroundProcess() => throw new NotSupportedException();

    public Process? GetWindowProcess(int handle) => throw new NotSupportedException();

    public int GetWindowProcessId(int handle) => throw new NotSupportedException();

    public string GetWindowTitle(int hWnd) => throw new NotSupportedException();

    public void MaximizeWindow(int handle) => throw new NotSupportedException();

    public List<int> MinimizedHandles { get; } = [];

    public void MinimizeWindow(int handle) => MinimizedHandles.Add(handle);

    public int GetForegroundWindowHandle() => throw new NotSupportedException();

    public void ActivateLivePreview(IntPtr targetWindow, IntPtr windowToSpare) =>
        throw new NotSupportedException();

    public void DeactivateLivePreview() => throw new NotSupportedException();

    public WindowPlacement.WindowState GetWindowState(int handle) =>
        throw new NotSupportedException();

    public WindowPlacement GetWindowPlacement(int handle) => throw new NotSupportedException();

    public void SetWindowText(int handle, string title) => throw new NotSupportedException();

    public IObservable<ActiveWindowChangeData> ActiveWindowChangedEvents() =>
        throw new NotSupportedException();

    public string GetClassName(int handle) => throw new NotSupportedException();

    public void MoveWindow(int handle, Point point) => throw new NotSupportedException();

    public bool IsTopLevel(int handle) => throw new NotSupportedException();

    public WindowStyles GetWindowStyles(int handle) => throw new NotSupportedException();

    public bool IsWindowVisible(int handle) => throw new NotSupportedException();

    public void SendInput(ushort key, bool down) => throw new NotSupportedException();

    public void MakeWindowNonActivating(nint handle) => throw new NotSupportedException();

    public bool IsWindow(int handle) => throw new NotSupportedException();

    public void HideWindow(int handle) => throw new NotSupportedException();

    public void RestoreWindow(int handle) => throw new NotSupportedException();
}
