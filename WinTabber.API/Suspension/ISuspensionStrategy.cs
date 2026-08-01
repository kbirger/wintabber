namespace WinTabber.API.Suspension;

public interface ISuspensionStrategy
{
    string Name { get; }
    void Suspend(int pid);
    void Resume(int pid);
}
