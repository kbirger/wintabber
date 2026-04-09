namespace WinTabber.Interop;

public record ActiveWindowChangeData(int Handle, int IdChild, uint ThreadId, uint Time);
