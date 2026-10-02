using Microsoft.UI.Xaml;
using WinTabber.UI.Common.ValueConverters;

namespace WinTabber.UI.Common.Tests.ValueConverters;

public class ValueConvertersTests
{
    [Test]
    public async Task BoolToThicknessConverter_True_ReturnsOneUnitThickness()
    {
        var converter = new BoolToThicknessConverter();

        var result = (Thickness)converter.Convert(true, typeof(Thickness), null!, "en-US");

        await Assert.That(result.Left).IsEqualTo(1d);
        await Assert.That(result.Top).IsEqualTo(1d);
    }

    [Test]
    public async Task BoolToThicknessConverter_False_ReturnsZeroThickness()
    {
        var converter = new BoolToThicknessConverter();

        var result = (Thickness)converter.Convert(false, typeof(Thickness), null!, "en-US");

        await Assert.That(result.Left).IsEqualTo(0d);
    }

    [Test]
    public async Task TimeSpanToFloatConverter_RoundTrips()
    {
        var converter = new TimeSpanToFloatConverter();
        var span = TimeSpan.FromSeconds(90);

        var seconds = converter.Convert(span, typeof(double), null!, "en-US");
        var back = converter.ConvertBack(seconds, typeof(TimeSpan), null!, "en-US");

        await Assert.That(seconds).IsEqualTo(90d);
        await Assert.That(back).IsEqualTo(span);
    }

    [Test]
    public async Task TimeSpanToStringConverter_UnderOneHour_FormatsAsMinutesSeconds()
    {
        var converter = new TimeSpanToStringConverter();

        var result = converter.Convert(TimeSpan.FromSeconds(65), typeof(string), null!, "en-US");

        await Assert.That(result).IsEqualTo("01:05");
    }

    [Test]
    public async Task FloatToPercentageConverter_ConvertsFractionToPercent()
    {
        var converter = new FloatToPercentageConverter();

        var result = converter.Convert(0.5f, typeof(double), null!, "en-US");

        await Assert.That(result).IsEqualTo(50f);
    }
}
