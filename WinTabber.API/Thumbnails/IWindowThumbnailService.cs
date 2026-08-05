using DynamicData;

namespace WinTabber.API.Thumbnails;

public interface IWindowThumbnailService : IDisposable
{
    IObservable<IChangeSet<ThumbnailEntry, int>> Connect();

    bool IsThumbnailed(int handle);

    bool CanThumbnail(WindowRef window);

    /// <summary>Moves the window off-screen and records it as thumbnailed. Returns false if it already was.</summary>
    bool StartThumbnail(WindowRef window);

    /// <summary>Moves the window back to its original position and stops tracking it. Idempotent.</summary>
    bool StopThumbnail(int handle);

    /// <summary>
    /// Resizes a currently-thumbnailed window (its off-screen position is untouched). The new size is
    /// remembered, so a later <see cref="StopThumbnail"/> restores the window at this size, not its
    /// original one — resizing the live preview is meant to resize the real window. No-op if not thumbnailed.
    /// </summary>
    void Resize(int handle, int width, int height);

    /// <summary>Restores every currently-thumbnailed window. Called on app shutdown so nothing is left stranded off-screen.</summary>
    void RestoreAll();
}
