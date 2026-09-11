using System.Reactive.Linq;
using WinTabber.UI.Media.Services;

namespace WinTabber.UI.Media.Tests.Fakes;

public sealed class FakeMediaControlsStateService : IMediaControlsStateService
{
    public IObservable<bool> IsMediaControlsVisibleChanges => Observable.Never<bool>();

    public void HideView() { }
}
