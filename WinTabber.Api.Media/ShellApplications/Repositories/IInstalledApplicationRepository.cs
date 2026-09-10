using DynamicData;
using WinTabber.Api.Media.ShellApplications.Models;

namespace WinTabber.Api.Media.ShellApplications.Repositories;

/// <summary>
/// The seam over <see cref="InstalledApplicationRepository"/>, extracted verbatim from its
/// public surface.
/// </summary>
/// <remarks>
/// <see cref="InstalledApplicationRepository.LoadingImage"/> is absent because it is
/// <c>static</c> — it belongs to the type, not to an instance, so it is reached through the
/// concrete class as before.
/// </remarks>
public interface IInstalledApplicationRepository : IDisposable
{
    IObservableCache<InstalledApplicationInfo, string> ApplicationsByAumid { get; }

    IObservableCache<InstalledApplicationInfo, string> ApplicationsByPath { get; }

    void Refresh();
}
