using System.Drawing;

namespace WinTabber.Api.Media.ShellApplications.Models;

public class InstalledApplicationInfo : IEquatable<InstalledApplicationInfo>
{
    public required string AppUserModelId { get; init; }
    public required string Name { get; init; }
    public string? TargetPath { get; init; }
    public string? PackageInstallPath { get; init; }

    /// <summary>
    /// Emits the app's icon as a framework-neutral <see cref="Bitmap"/>, or <see langword="null"/>
    /// while it has not loaded yet. Decoding into a UI-framework-specific image type (WPF
    /// <c>ImageSource</c> vs WinUI 3 <c>BitmapImage</c>) belongs to each app's own view model, not
    /// this UI-less repository.
    /// </summary>
    public required IObservable<Bitmap?> Icon { get; init; }

    public bool Equals(InstalledApplicationInfo? other)
    {
        return string.Equals(AppUserModelId, other?.AppUserModelId, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return base.Equals(obj as InstalledApplicationInfo);
    }

    public override int GetHashCode()
    {
        return AppUserModelId.GetHashCode();
    }
}
