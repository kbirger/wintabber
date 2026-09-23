using System.Drawing;
using System.Drawing.Imaging;
using WinTabber.Api.Media.ShellApplications.Caching;

namespace WinTabber.Api.Media.Tests.ShellApplications.Caching;

public class FileInstalledApplicationCacheStoreTests
{
    private static string CreateTempCacheDirectory() =>
        Path.Combine(Path.GetTempPath(), "WinTabberCacheTests_" + Guid.NewGuid().ToString("N"));

    [Test]
    public async Task Load_ReturnsEmpty_WhenNoCacheFilesExist()
    {
        var directory = CreateTempCacheDirectory();
        try
        {
            var store = new FileInstalledApplicationCacheStore(directory);

            var entries = store.Load();

            await Assert.That(entries).IsEmpty();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public async Task Load_ReturnsEmpty_WhenMetadataFileIsCorrupt()
    {
        var directory = CreateTempCacheDirectory();
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "installed-apps.json"), "{ not valid json");
            var store = new FileInstalledApplicationCacheStore(directory);

            var entries = store.Load();

            await Assert.That(entries).IsEmpty();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public async Task Save_ThenLoad_RoundTripsMetadata()
    {
        var directory = CreateTempCacheDirectory();
        try
        {
            var store = new FileInstalledApplicationCacheStore(directory);
            var entries = new[]
            {
                new CachedApplicationEntry
                {
                    AppUserModelId = "App.One",
                    Name = "App One",
                    TargetPath = @"C:\Apps\One.exe",
                    PackageInstallPath = null,
                },
                new CachedApplicationEntry
                {
                    AppUserModelId = "App.Two",
                    Name = "App Two",
                    TargetPath = null,
                    PackageInstallPath = @"C:\Program Files\Two",
                },
            };

            store.Save(entries, new Dictionary<string, byte[]?>());
            var loaded = store.Load();

            await Assert.That(loaded.Count).IsEqualTo(2);
            var one = loaded.Single(e => e.AppUserModelId == "App.One");
            await Assert.That(one.Name).IsEqualTo("App One");
            await Assert.That(one.TargetPath).IsEqualTo(@"C:\Apps\One.exe");
            var two = loaded.Single(e => e.AppUserModelId == "App.Two");
            await Assert.That(two.PackageInstallPath).IsEqualTo(@"C:\Program Files\Two");
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public async Task Save_ThenLoadIconBytes_RoundTripsRawBytes()
    {
        var directory = CreateTempCacheDirectory();
        try
        {
            var store = new FileInstalledApplicationCacheStore(directory);
            byte[] iconBytes = [1, 2, 3, 4, 5];
            var entry = new CachedApplicationEntry { AppUserModelId = "App.Icon", Name = "App Icon" };

            store.Save([entry], new Dictionary<string, byte[]?> { ["App.Icon"] = iconBytes });
            var loadedEntry = store.Load().Single();
            var loadedBytes = store.LoadIconBytes(loadedEntry);

            await Assert.That(loadedBytes).IsEquivalentTo(iconBytes);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public async Task Save_ThenLoadIcon_DecodesToBitmap()
    {
        var directory = CreateTempCacheDirectory();
        try
        {
            var store = new FileInstalledApplicationCacheStore(directory);
            using var sourceBitmap = new Bitmap(4, 4, PixelFormat.Format32bppArgb);
            sourceBitmap.SetPixel(0, 0, Color.FromArgb(255, 10, 20, 30));
            using var pngStream = new MemoryStream();
            sourceBitmap.Save(pngStream, ImageFormat.Png);
            var iconBytes = pngStream.ToArray();

            var entry = new CachedApplicationEntry { AppUserModelId = "App.Icon", Name = "App Icon" };
            store.Save([entry], new Dictionary<string, byte[]?> { ["App.Icon"] = iconBytes });
            var loadedEntry = store.Load().Single();

            using var loadedIcon = store.LoadIcon(loadedEntry);

            await Assert.That(loadedIcon).IsNotNull();
            await Assert.That(loadedIcon!.Width).IsEqualTo(4);
            await Assert.That(loadedIcon.GetPixel(0, 0)).IsEqualTo(Color.FromArgb(255, 10, 20, 30));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public async Task LoadIcon_And_LoadIconBytes_ReturnNull_WhenEntryHasNoCachedIcon()
    {
        var directory = CreateTempCacheDirectory();
        try
        {
            var store = new FileInstalledApplicationCacheStore(directory);
            var entry = new CachedApplicationEntry { AppUserModelId = "App.NoIcon", Name = "No Icon" };

            await Assert.That(store.LoadIcon(entry)).IsNull();
            await Assert.That(store.LoadIconBytes(entry)).IsNull();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Test]
    public async Task Save_LeavesNoTempFilesBehind()
    {
        var directory = CreateTempCacheDirectory();
        try
        {
            var store = new FileInstalledApplicationCacheStore(directory);
            var entry = new CachedApplicationEntry { AppUserModelId = "App.One", Name = "App One" };

            store.Save([entry], new Dictionary<string, byte[]?>());

            var remainingTempFiles = Directory.GetFiles(directory, "*.tmp");
            await Assert.That(remainingTempFiles).IsEmpty();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
