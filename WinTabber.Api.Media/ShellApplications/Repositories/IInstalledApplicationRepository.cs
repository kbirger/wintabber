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

    /// <summary>
    /// Emits an exception each time shell acquisition fails. <see cref="ApplicationsByAumid"/> and
    /// <see cref="ApplicationsByPath"/> never surface this themselves — DynamicData's
    /// <c>Or()</c> combinator they're built on silently drops an upstream <c>OnError</c> — so this
    /// is the only signal a failure reaches a consumer through.
    /// </summary>
    IObservable<Exception> AcquisitionErrors { get; }

    void Refresh();
}
