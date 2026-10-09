using DogSC.Core;
using Xunit;

namespace DogSC.Core.Tests;

public class OverlayInteractionTests
{
    [Fact]
    public void MacCameraImportPreservesPortraitMaskAndIndependentFraming()
    {
        var result = MacProjectImporter.Import("""
            {"media":{"screen":{"relativePath":"screen.mov"}},"camera":{"shape":"纵向",
            "roundness":0.8,"contentScale":2.3,"contentPosition":{"x":0.25,"y":0.7}}}
            """);
        Assert.Equal("vertical", result.Project.Camera.Shape);
        Assert.Equal(.4, result.Project.Camera.Corner);
        Assert.Equal(2.3, result.Project.Camera.ContentScale);
        Assert.Equal(.25, result.Project.Camera.ContentX); Assert.Equal(.7, result.Project.Camera.ContentY);
        Assert.DoesNotContain("camera.contentPosition", result.DroppedFields);
        Assert.DoesNotContain("camera.contentScale", result.DroppedFields);
    }

    [Theory]
    [InlineData(35)]
    [InlineData(90)]
    [InlineData(-140)]
    public void RotatedStickerAtScreenEdgeKeepsEveryCornerVisible(double angle)
    {
        var box = CanvasLayout.Sticker(1280, 720, new() { X = 1, Y = 0, Width = .8, Rotation = angle }, .5);
        foreach (var (u, v) in new[] { (0d, 0d), (1d, 0d), (1d, 1d), (0d, 1d) })
        {
            var p = box.Point(u, v);
            Assert.InRange(p.X, -.00001, 1280.00001); Assert.InRange(p.Y, -.00001, 720.00001);
            var local = box.Local(p.X, p.Y);
            Assert.Equal(u, local.U, 8); Assert.Equal(v, local.V, 8);
        }
    }

    [Fact]
    public void RotatedCornerResizeKeepsTheOppositeCornerAndAspect()
    {
        var box = new OverlayBox(300, 200, 160, 80, 32);
        var next = box.Resize(2, 45, 31, 10, 400);
        var anchor = box.Point(0, 0); var movedAnchor = next.Point(0, 0);
        Assert.Equal(anchor.X, movedAnchor.X, 8); Assert.Equal(anchor.Y, movedAnchor.Y, 8);
        Assert.Equal(2, next.Width / next.Height, 8);
        Assert.True(next.Width > box.Width);
        Assert.True(box.Resize(2, -1000, -1000, 10, 400).Width >= 10);
    }

    [Theory]
    [InlineData("horizontal", 1.5)]
    [InlineData("vertical", .72)]
    [InlineData("original", 2)]
    public void CameraRatioAndOffscreenPlacementStayInsidePortraitCanvas(string shape, double ratio)
    {
        var bubble = CanvasLayout.Bubble(360, 640, new() { Shape = shape, CenterX = 1, CenterY = 0, Size = .4 }, 2);
        Assert.Equal(ratio, bubble.Width / (double)bubble.Height, 1);
        Assert.InRange(bubble.X, 0, 360 - bubble.Width); Assert.InRange(bubble.Y, 0, 640 - bubble.Height);
    }
}
