using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class ParameterNumberTests
{
    [Theory]
    [InlineData("84", true, .84)]
    [InlineData("84％", true, .84)]
    [InlineData("1,6×", false, 1.6)]
    [InlineData("1，6倍", false, 1.6)]
    [InlineData("0.7秒", false, .7)]
    [InlineData("0.7s", false, .7)]
    [InlineData("-18°", false, -18)]
    [InlineData("18度", false, 18)]
    [InlineData("150%", true, 1.5)]
    public void UsesTheDisplayedUnitForBareAndPastedNumbers(string text, bool percent, double expected)
    {
        Assert.True(ParameterNumber.TryParse(text, percent, -100, 100, out var value));
        Assert.Equal(expected, value, 7);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1.2.3")]
    public void IncompleteAndNonNumericDraftsDoNotBecomeProjectValues(string text)
        => Assert.False(ParameterNumber.TryParse(text, false, 0, 1, out _));

    [Fact]
    public void NumericEntryClampsToTheSameRangeAsItsSlider()
    {
        Assert.True(ParameterNumber.TryParse("200%", true, 0, 1, out var value)); Assert.Equal(1, value);
        Assert.True(ParameterNumber.TryParse("-90°", false, -28, 28, out value)); Assert.Equal(-28, value);
    }
}
