using WinTabber.API.Suspension;

namespace WinTabber.Api.Tests.Suspension;

public class SuspendedWindowFileStoreTests
{
    private static string CreateTempDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "WinTabberApiTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Test]
    public async Task RoundTrips_Entries()
    {
        string dir = CreateTempDirectory();
        try
        {
            var store = new SuspendedWindowFileStore(dir);
            var entries = new[]
            {
                new SuspendedWindowEntry(111, [1, 2, 3], "abc123", "notepad", "Untitled - Notepad", "process"),
                new SuspendedWindowEntry(222, [4], "def456", "calc", "Calculator", "thread"),
            };

            store.Save(entries);
            var loaded = store.Load();

            await Assert.That(loaded.Count).IsEqualTo(2);
            var first = loaded.Single(e => e.ProcessId == 111);
            await Assert.That(first.WindowHandles.ToArray()).IsEquivalentTo(new[] { 1, 2, 3 });
            await Assert.That(first.PathHash).IsEqualTo("abc123");
            await Assert.That(first.ProcessName).IsEqualTo("notepad");
            await Assert.That(first.Title).IsEqualTo("Untitled - Notepad");
            await Assert.That(first.StrategyName).IsEqualTo("process");

            var second = loaded.Single(e => e.ProcessId == 222);
            await Assert.That(second.StrategyName).IsEqualTo("thread");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task Load_ReturnsEmpty_ForMissingFile()
    {
        string dir = CreateTempDirectory();
        try
        {
            var store = new SuspendedWindowFileStore(dir);

            var loaded = store.Load();

            await Assert.That(loaded.Count).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task Load_ReturnsEmpty_ForCorruptFile()
    {
        string dir = CreateTempDirectory();
        try
        {
            File.WriteAllText(Path.Combine(dir, "suspended_state.json"), "{ this is not valid json ][");
            var store = new SuspendedWindowFileStore(dir);

            var loaded = store.Load();

            await Assert.That(loaded.Count).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public async Task Delete_RemovesTheFile()
    {
        string dir = CreateTempDirectory();
        try
        {
            var store = new SuspendedWindowFileStore(dir);
            store.Save([new SuspendedWindowEntry(1, [1], "hash", "p", "t")]);
            string path = Path.Combine(dir, "suspended_state.json");
            await Assert.That(File.Exists(path)).IsTrue();

            store.Delete();

            await Assert.That(File.Exists(path)).IsFalse();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
