namespace WinTabber.Api.Media.ShellApplications.Caching;

public sealed record CachedApplicationEntry
{
    public required string AppUserModelId { get; init; }
    public required string Name { get; init; }
    public string? TargetPath { get; init; }
    public string? PackageInstallPath { get; init; }

    /// <summary>Byte offset of this app's icon in the icon blob file. -1 when no icon is cached.</summary>
    public long IconOffset { get; init; } = -1;

    /// <summary>Length in bytes of this app's icon in the icon blob file. 0 when no icon is cached.</summary>
    public int IconLength { get; init; }

    /// <summary>
    /// True when <paramref name="name"/>, <paramref name="targetPath"/> and <paramref name="packageInstallPath"/>
    /// match this entry, so an icon saved for it still belongs to the same app. The icon offset and
    /// length are not compared: a freshly scanned entry never has them.
    /// </summary>
    public bool HasSameMetadataAs(string name, string? targetPath, string? packageInstallPath) =>
        Name == name && TargetPath == targetPath && PackageInstallPath == packageInstallPath;

    public bool HasSameMetadataAs(CachedApplicationEntry other) =>
        AppUserModelId == other.AppUserModelId
        && HasSameMetadataAs(other.Name, other.TargetPath, other.PackageInstallPath);
}
