using System.Windows.Media.Media3D;
using DogSC.Core;

namespace DogSC;

public partial class EditorWindow
{
    GeometryModel3D? _screenPlane;
    (double Width, double Height, double X, double Y)? _projectionKey;

    void UpdatePerspective()
    {
        var width = FrameHost.Width;
        var height = FrameHost.Height;
        if (width < 2 || height < 2) return;
        var tilted = Math.Abs(_project.Canvas.TiltX) > 0.01 || Math.Abs(_project.Canvas.TiltY) > 0.01;
        ScreenPerspective.Visibility = tilted ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(ScreenTexture, tilted ? -100000 : 0);
        if (!tilted) return;
        var key = (width, height, _project.Canvas.TiltX, _project.Canvas.TiltY);
        if (_projectionKey == key) return;
        _projectionKey = key;
        if (_screenPlane is null)
        {
            var material = new DiffuseMaterial(new VisualBrush(ScreenTexture) { Stretch = Stretch.Fill });
            _screenPlane = new GeometryModel3D { Material = material, BackMaterial = material };
            var scene = new Model3DGroup();
            scene.Children.Add(new AmbientLight(Colors.White));
            scene.Children.Add(_screenPlane);
            ScreenPerspective.Children.Add(new ModelVisual3D { Content = scene });
        }
        var distance = width / 2;
        var mesh = new MeshGeometry3D
        {
            TextureCoordinates = new PointCollection { new(0, 0), new(1, 0), new(0, 1), new(1, 1) },
            TriangleIndices = new Int32Collection { 0, 2, 1, 1, 2, 3 }
        };
        foreach (var p in PerspectiveGeometry.Corners(width, height, key.Item3, key.Item4))
            mesh.Positions.Add(new Point3D((p.X - width / 2) * p.Weight, (height / 2 - p.Y) * p.Weight, distance * (1 - p.Weight)));
        _screenPlane.Geometry = mesh;
        ScreenPerspective.Width = width;
        ScreenPerspective.Height = height;
        ScreenPerspective.Camera = new PerspectiveCamera(new Point3D(0, 0, distance), new Vector3D(0, 0, -1), new Vector3D(0, 1, 0), 90);
    }
}
