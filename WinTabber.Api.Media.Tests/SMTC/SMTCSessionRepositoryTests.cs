using System.Reactive.Linq;
using Windows.Media.Control;
using WinTabber.Api.Media.SMTC.Repositories;
using WinTabber.Api.Media.Tests.Fakes;

namespace WinTabber.Api.Media.Tests.SMTC;

public class SMTCSessionRepositoryTests
{
    [Test]
    public async Task ActiveMediaSessionChanges_PropagatesSourceFailure_WhenSessionManagerUnavailable()
    {
        var expected = new InvalidOperationException("SMTC unavailable");
        var source = new FakeSmtcSessionSource(
            () => Task.FromException<GlobalSystemMediaTransportControlsSessionManager>(expected)
        );
        var repository = new SMTCSessionRepository(source);

        await Assert
            .That(async () => await repository.ActiveMediaSessionChanges.FirstAsync())
            .Throws<InvalidOperationException>();
    }
}
