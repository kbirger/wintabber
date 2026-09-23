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
}
