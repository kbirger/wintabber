using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace WinTabber.Interop;

public static class ProcessExtensions
{
    extension(Process process)
    {
        public bool IsSystemProcess => ProcessHelper.IsSystemProcess(process);
        public Process? Parent => ProcessHelper.GetParentProcess(process);

        public IEnumerable<Process> GetAncestors() => ProcessHelper.GetAncestors(process.Id);
        public bool TryGetExecutablePath([MaybeNullWhen(false)] out string executablePath) =>
            ProcessHelper.TryGetProcessExecutablePath((uint)process.Id, out executablePath);
    }

    extension(ProcessInfo process)
    {
        public bool IsSystemProcess => ProcessHelper.IsSystemProcess(process.Id);

        public Process? Parent =>  ProcessHelper.GetParentProcess(Process.GetProcessById(process.Id));

        public bool TryGetExecutablePath([MaybeNullWhen(false)] out string executablePath) =>
            ProcessHelper.TryGetProcessExecutablePath((uint)process.Id, out executablePath);
    }
}
