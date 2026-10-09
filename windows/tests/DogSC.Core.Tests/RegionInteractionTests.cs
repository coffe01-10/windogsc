using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class RegionInteractionTests
{
    [Fact]
    public void ReverseDrawingAndOffscreenMovementKeepTheTargetInBounds()
    {
        var bounds = RegionBounds.Draw(.8, .7, .3, .4);
        Assert.Equal(.3, bounds.X); Assert.Equal(.4, bounds.Y);
        Assert.Equal(.5, bounds.Width, 8); Assert.Equal(.3, bounds.Height, 8);
        var moved = bounds.Drag(RegionEdge.Move, 2, -2);
        Assert.Equal(.5, moved.X, 8); Assert.Equal(0, moved.Y);
        Assert.Equal(bounds.Width, moved.Width); Assert.Equal(bounds.Height, moved.Height);
    }

    [Fact]
    public void ResizingKeepsOppositeCornerFixedEvenPastTheEdge()
    {
        var original = new RegionBounds(.3, .4, .2, .1);
        var resized = original.Drag(RegionEdge.Left | RegionEdge.Top, -.8, -.8);
        Assert.Equal(0, resized.X); Assert.Equal(0, resized.Y);
        Assert.Equal(.5, resized.Width, 8); Assert.Equal(.5, resized.Height, 8);
        var crossed = original.Drag(RegionEdge.Left | RegionEdge.Top, 1, 1);
        Assert.Equal(.5, crossed.X + crossed.Width, 8);
        Assert.Equal(.5, crossed.Y + crossed.Height, 8);
        Assert.True(crossed.Width > 0 && crossed.Height > 0);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(18, -22)]
    [InlineData(-25, 20)]
    public void PickingWithCropZoomAndPerspectiveReturnsTheOriginalSourcePixel(double tiltX, double tiltY)
    {
        var quad = PerspectiveGeometry.Corners(640, 360, tiltX, tiltY);
        var crop = new SourceCrop { X = .15, Y = .1, Width = .7, Height = .8 };
        const double sourceX = .43, sourceY = .52, focusX = .4, focusY = .6, zoom = 1.7;
        var u = focusX + ((sourceX - crop.X) / crop.Width - focusX) * zoom;
        var v = focusY + ((sourceY - crop.Y) / crop.Height - focusY) * zoom;
        var projected = PerspectiveGeometry.DestinationPoint(quad, u, v);
        var picked = PerspectiveGeometry.SourcePoint(quad, projected.X, projected.Y);
        Assert.Equal(sourceX, crop.X + (focusX + (picked.X - focusX) / zoom) * crop.Width, 9);
        Assert.Equal(sourceY, crop.Y + (focusY + (picked.Y - focusY) / zoom) * crop.Height, 9);
    }

    [Fact]
    public void NormalizingDoesNotEnlargeAThinTextMaskToTwoPercentOfTheScreen()
    {
        var region = new SoftRegion { Enabled = true, X = .4, Y = .5, Width = 140d / 3840, Height = 22d / 2160 };
        var project = new ProjectDocument { Width = 3840, Height = 2160 };
        project.Canvas.SoftRegions.Add(region);
        ProjectFile.Normalize(project);
        Assert.Equal(22d / 2160, region.Height, 9);
    }
}
