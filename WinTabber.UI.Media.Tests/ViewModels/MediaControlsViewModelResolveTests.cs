using WinTabber.UI.Media.ViewModels;

namespace WinTabber.UI.Media.Tests.ViewModels;

/// <summary>
/// Direct tests for <see cref="MediaControlsViewModel.Resolve{T}"/>, the pure function the derived
/// selection pipeline is built on (see the selection-model plan's "Known risk" note).
/// </summary>
/// <remarks>
/// Exercised against a trivial record rather than a real <see cref="SessionListItem"/>: that
/// type's own constructor takes an <c>AggregateSession</c>, which wraps a WinRT session type this
/// test project cannot build (the same constraint <c>Fakes/FakeMediaSessionService.cs</c>
/// documents). <c>Resolve</c> is generic over the AUMID projection for exactly this reason, so a
/// test can supply a stand-in instead.
/// </remarks>
public class MediaControlsViewModelResolveTests
{
    private sealed record Item(string Aumid);

    private static string GetAumid(Item item) => item.Aumid;

    [Test]
    public async Task Pick_WinsOverActive_WhenBothPresent()
    {
        var items = new[] { new Item("brave"), new Item("nora") };

        var result = MediaControlsViewModel.Resolve(items, GetAumid, "nora", "brave");

        await Assert.That(result).IsSameReferenceAs(items[1]);
    }

    [Test]
    public async Task NoPick_FallsBackToActive()
    {
        var items = new[] { new Item("brave"), new Item("nora") };

        var result = MediaControlsViewModel.Resolve(items, GetAumid, null, "brave");

        await Assert.That(result).IsSameReferenceAs(items[0]);
    }

    /// <summary>
    /// The central behavior this whole model exists for: a pick is not cleared just because its
    /// session is briefly absent from the cache (a track skip). It falls back to active only when
    /// the pick truly is not resolvable.
    /// </summary>
    [Test]
    public async Task Pick_AbsentFromItems_FallsBackToActive()
    {
        var items = new[] { new Item("brave") };

        var result = MediaControlsViewModel.Resolve(items, GetAumid, "nora", "brave");

        await Assert.That(result).IsSameReferenceAs(items[0]);
    }

    [Test]
    public async Task PickAndActive_BothAbsent_ReturnsNull()
    {
        var items = new[] { new Item("brave") };

        var result = MediaControlsViewModel.Resolve(items, GetAumid, "nora", "deezer");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task NoPick_NoActive_ReturnsNull()
    {
        var items = new[] { new Item("brave") };

        var result = MediaControlsViewModel.Resolve(items, GetAumid, null, null);

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task NoPick_ActivePresent_ReturnsActive()
    {
        var items = new[] { new Item("brave"), new Item("nora") };

        var result = MediaControlsViewModel.Resolve(items, GetAumid, null, "nora");

        await Assert.That(result).IsSameReferenceAs(items[1]);
    }

    [Test]
    public async Task EmptyCollection_ReturnsNull()
    {
        var result = MediaControlsViewModel.Resolve(Array.Empty<Item>(), GetAumid, "nora", "brave");

        await Assert.That(result).IsNull();
    }

    [Test]
    public async Task Matching_IsCaseInsensitive()
    {
        var items = new[] { new Item("Brave.Nora.App") };

        var result = MediaControlsViewModel.Resolve(items, GetAumid, "brave.nora.app", null);

        await Assert.That(result).IsSameReferenceAs(items[0]);
    }
}
