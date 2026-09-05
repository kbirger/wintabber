# WinTabber.Interop.Tests

Deliberately narrow. Covered:

- `ProcessHelper.IsSystemProcess` and `ProcessHelper.ClassifyNonSystemProcesses` — the only pure
  logic in this project not requiring a real Win32 call.

**Not covered, and why:**

- `InteropProxy`, `NativeMethods`, `NtNativeMethods`, `PInvoke`, `MediaKeySender`, `UacHelper`'s
  elevation check, `ChromeInterop` — all P/Invoke wrapper glue. A test here would only verify
  that the wrapper calls the Win32 function it wraps (mock-verifies-the-mock), the same argument
  T3.1 already made about `WinTabber.UI.Common/Chrome`'s `CloakHelper`/`PeekHelper`.
- `WindowPlacement`, `ProcessInfo` — plain data records, nothing to test.
- `ProcessHelper.GetParentProcess`, `GetProcesses`, `GetProcessesByName`,
  `TryGetProcessExecutablePath` — all directly call live Win32 APIs (`NtQueryInformationProcess`,
  `CreateToolhelp32Snapshot`, `OpenProcess`/`QueryFullProcessImageName`); no seam without
  introducing an abstraction over the OS process table, which is not justified by this task.

See `docs/superpowers/specs/2026-09-04-phase-5-design.md`'s T5.4 section for the full reasoning.
