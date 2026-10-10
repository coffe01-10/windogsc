using System.Windows.Media.Media3D;
using DogSC.Core;

namespace DogSC;

public partial class EditorWindow
{
    GeometryModel3D? _screenPlane;
    GeometryModel3D? _chromePlane;
    (double Width, double Height, double X, double Y, double? Perspective, double AnchorX, double AnchorY)? _projectionKey;
    ProjectionCorner[]? _screenProjectionQuad;

    void UpdatePerspective(double tiltX, double tiltY, double? perspective = null, double anchorX = .5, double anchorY = .5)
    {
        var width = FrameHost.Width;
        var height = FrameHost.Height;
        if (width < 2 || height < 2) return;
        var tilted = Math.Abs(tiltX) > 0.01 || Math.Abs(tiltY) > 0.01;
        ScreenPerspective.Visibility = tilted ? Visibility.Visible : Visibility.Collapsed;
        FrameChrome.Visibility = !tilted && FrameChrome.Source is not null ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(ScreenTexture, tilted ? -100000 : 0);
        FrameHost.ClipToBounds = !tilted && _project.Canvas.ScreenFrame == "none";
        var key = (width, height, tiltX, tiltY, perspective, anchorX, anchorY);
        if (_projectionKey == key) return;
        _projectionKey = key;
        _screenProjectionQuad = perspective is { } depth
            ? PerspectiveGeometry.MotionCorners(width, height, tiltX, tiltY, depth, anchorX, anchorY)
            : PerspectiveGeometry.Corners(width, height, tiltX, tiltY);
        if (!tilted) return;
        if (_screenPlane is null)
        {
            var material = new DiffuseMaterial(new VisualBrush(ScreenTexture) { Stretch = Stretch.Fill });
            _screenPlane = new GeometryModel3D { Material = material, BackMaterial = material };
            var scene = new Model3DGroup();
            scene.Children.Add(new AmbientLight(Colors.White));
            scene.Children.Add(_screenPlane);
            _chromePlane = new GeometryModel3D();
            scene.Children.Add(_chromePlane);
            ScreenPerspective.Children.Add(new ModelVisual3D { Content = scene });
        }
        ProjectionCorner[] chromeQuad = [];
        if (FrameChrome.Source is not null && _project.Canvas.ScreenFrame != "none")
        {
            var left = Canvas.GetLeft(FrameChrome); var top = Canvas.GetTop(FrameChrome);
            chromeQuad = Enumerable.Range(0, 4).Select(i =>
            {
                var u = (left + i % 2 * FrameChrome.Width) / width;
                var v = (top + i / 2 * FrameChrome.Height) / height;
                var (x, y) = PerspectiveGeometry.DestinationPoint(_screenProjectionQuad, u, v);
                var weight = _screenProjectionQuad[0].Weight + (_screenProjectionQuad[1].Weight - _screenProjectionQuad[0].Weight) * u
                    + (_screenProjectionQuad[2].Weight - _screenProjectionQuad[0].Weight) * v;
                return new ProjectionCorner(x, y, weight);
            }).ToArray();
            var material = new DiffuseMaterial(new ImageBrush(FrameChrome.Source));
            _chromePlane!.Material = _chromePlane.BackMaterial = material;
        }
        else if (_chromePlane is not null) _chromePlane.Geometry = null;
        var padding = _screenProjectionQuad.Concat(chromeQuad).Max(p => Math.Max(Math.Max(-p.X, p.X - width), Math.Max(-p.Y, p.Y - height)));
        padding = Math.Max(0, padding) + 2;
        var distance = (width + padding * 2) / 2;
        var mesh = _screenPlane.Geometry as MeshGeometry3D ?? new MeshGeometry3D
        {
            TextureCoordinates = new PointCollection { new(0, 0), new(1, 0), new(0, 1), new(1, 1) },
            TriangleIndices = new Int32Collection { 0, 2, 1, 1, 2, 3 }
        };
        mesh.Positions.Clear();
        foreach (var p in _screenProjectionQuad)
            mesh.Positions.Add(new Point3D((p.X - width / 2) * p.Weight, (height / 2 - p.Y) * p.Weight, distance * (1 - p.Weight)));
        _screenPlane.Geometry = mesh;
        if (chromeQuad.Length > 0)
        {
            _chromePlane!.Geometry = new MeshGeometry3D
            {
                TextureCoordinates = mesh.TextureCoordinates, TriangleIndices = mesh.TriangleIndices,
                Positions = new Point3DCollection(chromeQuad.Select(p => new Point3D(
                    (p.X - width / 2) * p.Weight, (height / 2 - p.Y) * p.Weight, distance * (1 - p.Weight) + .001)))
            };
        }
        ScreenPerspective.Width = width + padding * 2;
        ScreenPerspective.Height = height + padding * 2;
        Canvas.SetLeft(ScreenPerspective, -padding); Canvas.SetTop(ScreenPerspective, -padding);
        if (ScreenPerspective.Camera is PerspectiveCamera camera && !camera.IsFrozen) camera.Position = new Point3D(0, 0, distance);
        else ScreenPerspective.Camera = new PerspectiveCamera(new Point3D(0, 0, distance), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), 90);
    }
}
