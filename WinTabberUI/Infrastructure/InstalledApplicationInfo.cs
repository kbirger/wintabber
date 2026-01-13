using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace WinTabberUI.Infrastructure;

public class InstalledApplicationInfo : IEquatable<InstalledApplicationInfo>
{
    public required string AppUserModelId { get; init; }
    public required string Name { get; init; }
    public string? TargetPath { get; init; }
    public string? PackageInstallPath { get; init; }
    public required IObservable<ImageSource> Icon { get; init; }

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
