using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class BarDockLayoutTests
{
    [Theory]
    [InlineData(5, 250, BarDockEdge.Left)]
    [InlineData(715, 250, BarDockEdge.Right)]
    [InlineData(300, 5, BarDockEdge.Top)]
    [InlineData(300, 715, BarDockEdge.Bottom)]
    [InlineData(300, 250, BarDockEdge.None)]
    public void DragRelease_SnapsOnlyNearAnEdge(double left, double top, BarDockEdge expected)
    {
        Assert.Equal(expected, BarDockLayout.SnapEdge(new(left, top, 280, 80), new(0, 0, 1000, 800)));
    }

    [Theory]
    [InlineData(BarDockEdge.Left)]
    [InlineData(BarDockEdge.Right)]
    [InlineData(BarDockEdge.Top)]
    [InlineData(BarDockEdge.Bottom)]
    public void EveryAnimationSize_StaysInsideWorkAreaAndKeepsDockedEdge(BarDockEdge edge)
    {
        // A secondary monitor to the left, with a taskbar reducing usable height.
        var work = new BarBounds(-1280, 40, 1280, 720);
        var anchor = BarAnchor.Capture(new(-250, 650, 200, 70), work, edge);
        var vertical = edge is BarDockEdge.Left or BarDockEdge.Right;
        for (var length = 200; length <= 1000; length += 10)
        {
            var placed = anchor.Place(work, vertical ? 112 : length, vertical ? length : 104);
            Assert.InRange(placed.Left, work.Left, work.Right - placed.Width);
            Assert.InRange(placed.Top, work.Top, work.Bottom - placed.Height);
            if (edge == BarDockEdge.Left) Assert.Equal(work.Left, placed.Left);
            if (edge == BarDockEdge.Right) Assert.Equal(work.Right, placed.Right);
            if (edge == BarDockEdge.Top) Assert.Equal(work.Top, placed.Top);
            if (edge == BarDockEdge.Bottom) Assert.Equal(work.Bottom, placed.Bottom);
        }
    }

    [Fact]
    public void NearBottomRight_ExpansionUsesTheExistingRightAndBottomAnchor()
    {
        var work = new BarBounds(0, 0, 1920, 1040);
        var original = new BarBounds(1600, 950, 280, 80);
        var anchor = BarAnchor.Capture(original, work, BarDockEdge.None);
        var expanded = anchor.Place(work, 860, 124);
        Assert.Equal(original.Right, expanded.Right);
        Assert.Equal(original.Bottom, expanded.Bottom);
        Assert.Equal(original, anchor.Place(work, original.Width, original.Height));
    }

    [Theory]
    [InlineData(BarDockEdge.Left)]
    [InlineData(BarDockEdge.Right)]
    [InlineData(BarDockEdge.Top)]
    [InlineData(BarDockEdge.Bottom)]
    public void DockTransition_ChangesOrientationWithoutLeavingWorkArea(BarDockEdge edge)
    {
        var work = new BarBounds(-1280, 40, 1280, 720);
        var origin = new BarBounds(-500, 640, 280, 80);
        var anchor = BarAnchor.Capture(origin, work, edge);
        var vertical = edge is BarDockEdge.Left or BarDockEdge.Right;
        Assert.Equal(origin, anchor.Place(work, origin.Width, origin.Height, origin, 0));
        for (var frame = 0; frame <= 20; frame++)
        {
            var progress = frame / 20d;
            var width = origin.Width + ((vertical ? 112 : 860) - origin.Width) * progress;
            var height = origin.Height + ((vertical ? 700 : 104) - origin.Height) * progress;
            var placed = anchor.Place(work, width, height, origin, progress);
            Assert.InRange(placed.Left, work.Left, work.Right - placed.Width);
            Assert.InRange(placed.Top, work.Top, work.Bottom - placed.Height);
            if (frame == 20) Assert.Equal(anchor.Place(work, width, height), placed);
        }
    }

    [Fact]
    public void RemovedMonitor_OldAnchorMovesIntoRemainingWorkArea()
    {
        var anchor = new BarAnchor(BarDockEdge.Right, true, true, 3800, 2000);
        var placed = anchor.Place(new(0, 0, 1024, 720), 132, 700);
        Assert.Equal(new BarBounds(892, 20, 132, 700), placed);
    }
}
