using Windows.Foundation;
using Windows.Graphics;
using WinTabber.UI.Common.AccessKeys;

namespace WinTabber.UI.Common.Tests.AccessKeys;

public class AccessKeyOverlayWindowTests
{
    private const double Tolerance = 1e-5;

    [Test]
    public async Task ComputeOverlayLocalPosition_offsets_by_the_difference_between_owner_and_overlay_origin()
    {
        // Owner window sits at physical (2560, 0) -- a second monitor to the right of the primary,
        // which the overlay (anchored to that same monitor) sits at (2560, 0) too in this case, so
        // the offset is zero and the element's own local position passes through unchanged.
        var elementLocal = new Point(24, 8);
        var ownerPosition = new PointInt32(2560, 0);
        var overlayPosition = new PointInt32(2560, 0);
        var scale = 1.5;

        var result = AccessKeyOverlayWindow.ComputeOverlayLocalPosition(elementLocal, ownerPosition, overlayPosition, scale);

        // Offsets are both 0, so result should equal input
        var expectedX = elementLocal.X;
        var expectedY = elementLocal.Y;

        await Assert.That(Math.Abs(result.X - expectedX) < Tolerance).IsTrue();
        await Assert.That(Math.Abs(result.Y - expectedY) < Tolerance).IsTrue();
    }

    [Test]
    public async Task ComputeOverlayLocalPosition_adds_the_scaled_owner_overlay_offset()
    {
        // Owner window is offset 300 physical pixels right and 100 down from the overlay's own
        // origin (e.g. overlay anchored to the monitor's top-left, owner window positioned partway
        // into it). At 1.5x scale, that is a 200,~67 DIP offset added to the element's own local
        // position.
        var elementLocal = new Point(10, 10);
        var ownerPosition = new PointInt32(300, 100);
        var overlayPosition = new PointInt32(0, 0);
        var scale = 1.5;

        var result = AccessKeyOverlayWindow.ComputeOverlayLocalPosition(elementLocal, ownerPosition, overlayPosition, scale);

        // Calculate expected values using the same formula
        var offsetX = (ownerPosition.X - overlayPosition.X) / scale;
        var offsetY = (ownerPosition.Y - overlayPosition.Y) / scale;
        var expectedX = elementLocal.X + offsetX;
        var expectedY = elementLocal.Y + offsetY;

        await Assert.That(Math.Abs(result.X - expectedX) < Tolerance).IsTrue();
        await Assert.That(Math.Abs(result.Y - expectedY) < Tolerance).IsTrue();
    }
}
