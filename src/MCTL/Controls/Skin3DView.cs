using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace MCTL.Controls;

/// <summary>
/// 原生 WPF 3D 皮肤渲染：只显示头部或完整小人（可播放行走动画）。
/// 支持鼠标拖动旋转、滚轮缩放，背景透明，纹理使用最近邻采样保持像素清晰。
/// </summary>
public class Skin3DView : Grid
{
    private const double Fov = 32;
    private static readonly Vector3D LightDirection = new(0.45, -0.75, -0.7);

    private readonly Viewport3D _viewport = new() { ClipToBounds = true };
    private readonly PerspectiveCamera _camera = new() { FieldOfView = Fov, UpDirection = new Vector3D(0, 1, 0) };
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private Model3DGroup _scene;
    private RotateTransform3D _rightArmRotation, _leftArmRotation, _rightLegRotation, _leftLegRotation;
    private BitmapSource _skin;
    private bool _slim;
    private bool _headOnly = true;
    private bool _walking = true;
    private bool _frameHooked;
    private double _yaw = -0.42;
    private double _pitch = 0.12;
    private double _zoom = 1;
    private readonly Dictionary<(int, int, int, int), Material> _faceMaterials = new();
    private Point _dragOrigin;
    private bool _dragging;

    public Skin3DView()
    {
        Children.Add(_viewport);
        _viewport.Camera = _camera;
        ClipToBounds = true;
        RenderOptions.SetBitmapScalingMode(_viewport, BitmapScalingMode.NearestNeighbor);
        Cursor = Cursors.Hand;

        Loaded += (_, _) =>
        {
            Fit();
            Hook();
        };
        Unloaded += (_, _) => Unhook();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) Hook();
            else Unhook();
        };

        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        MouseWheel += OnWheel;
        SizeChanged += (_, _) => Fit();

        Rebuild();
    }

    /// <summary>仅渲染头部（账号区使用）。</summary>
    public bool HeadOnly
    {
        get => _headOnly;
        set
        {
            if (_headOnly == value) return;
            _headOnly = value;
            Rebuild();
        }
    }

    /// <summary>是否播放行走动画（完整小人时有效）。</summary>
    public bool WalkAnimation
    {
        get => _walking;
        set
        {
            _walking = value;
            if (value) Hook();
            else Unhook();
        }
    }

    /// <summary>拖动旋转的初始水平角度（弧度）。</summary>
    public double StartYaw
    {
        get => _yaw;
        set
        {
            _yaw = value;
            Fit();
        }
    }

    /// <summary>水平视角（弧度），主要用于自动化校验。</summary>
    public double ViewYaw
    {
        get => _yaw;
        set
        {
            _yaw = value;
            Fit();
        }
    }

    /// <summary>俯仰视角（弧度），主要用于自动化校验。</summary>
    public double ViewPitch
    {
        get => _pitch;
        set
        {
            _pitch = value;
            Fit();
        }
    }

    /// <summary>设置皮肤纹理；skin 为空时使用内置的默认皮肤。</summary>
    public void SetSkin(BitmapSource skin, bool slim)
    {
        _skin = skin;
        _slim = slim;
        Rebuild();
    }

    /// <summary>调试用：首个网格的顶点 / UV 数量。</summary>
    public string DebugMeshInfo()
    {
        if (_scene == null) return "null";
        return Find(_scene) ?? "none";

        static string Find(Model3DGroup group)
        {
            foreach (var child in group.Children)
            {
                if (child is GeometryModel3D model && model.Geometry is MeshGeometry3D mesh)
                    return $"pos={mesh.Positions.Count} uv={mesh.TextureCoordinates.Count} idx={mesh.TriangleIndices.Count} " +
                           $"uv0={(mesh.TextureCoordinates.Count > 0 ? mesh.TextureCoordinates[0].ToString() : "-")}";
                if (child is Model3DGroup nested && Find(nested) is { } nestedResult) return nestedResult;
            }
            return null;
        }
    }

    private void Rebuild()
    {
        var skin = _skin ?? CreateDefaultSkin();
        _scene = new Model3DGroup();
        _faceMaterials.Clear();

        // 光照：环境光保证暗面可见，方向光提供立体感
        _scene.Children.Add(new AmbientLight(Color.FromRgb(178, 178, 178)));
        _scene.Children.Add(new DirectionalLight(Color.FromRgb(120, 120, 120), LightDirection));

        _rightArmRotation = _leftArmRotation = null;
        _rightLegRotation = _leftLegRotation = null;

        if (_headOnly)
        {
            _scene.Children.Add(BuildHead(skin, true));
        }
        else
        {
            BuildBody(skin);
        }

        _viewport.Children.Clear();
        _viewport.Children.Add(new ModelVisual3D { Content = _scene });
        Fit();
    }


    private Model3DGroup BuildHead(BitmapSource skin, bool withHat)
    {
        var group = new Model3DGroup();
        AddBox(group, skin, 8, 8, 8, new Point3D(0, 28, 0), HeadUvs(0, 0));

        // 帽子层：旧版 64×32 皮肤没有帽子层，其区域是不透明黑，跳过
        if (withHat && skin.PixelHeight >= 64 && HasTransparentPixel(skin, 32, 0, 32, 16))
            AddBox(group, skin, 8.5, 8.5, 8.5, new Point3D(0, 28, 0), HeadUvs(32, 0));
        return group;
    }

    private void BuildBody(BitmapSource skin)
    {
        var armWidth = _slim ? 3.0 : 4.0;
        var armX = _slim ? 5.5 : 6.0;
        var modern = skin.PixelHeight >= 64;

        _scene.Children.Add(BuildHead(skin, modern));

        // 身体
        AddBox(_scene, skin, 8, 12, 4, new Point3D(0, 18, 0), BodyUvs(16, 16, 8, 12, 4));
        if (modern)
            AddBox(_scene, skin, 8.25, 12.25, 4.25, new Point3D(0, 18, 0), BodyUvs(16, 32, 8, 12, 4));

        // 右臂（玩家右侧，位于 -X）
        var rightArm = new Model3DGroup();
        AddBox(rightArm, skin, armWidth, 12, 4, new Point3D(-armX, 18, 0),
            LimbUvs(40, 16, armWidth, 12, 4));
        if (modern)
            AddBox(rightArm, skin, armWidth + 0.25, 12.25, 4.25, new Point3D(-armX, 18, 0),
                LimbUvs(40, 32, armWidth, 12, 4));
        _rightArmRotation = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0), -armX, 24, 0);
        rightArm.Transform = _rightArmRotation;
        _scene.Children.Add(rightArm);

        // 左臂
        var leftArm = new Model3DGroup();
        AddBox(leftArm, skin, armWidth, 12, 4, new Point3D(armX, 18, 0),
            LimbUvs(32, 48, armWidth, 12, 4));
        if (modern)
            AddBox(leftArm, skin, armWidth + 0.25, 12.25, 4.25, new Point3D(armX, 18, 0),
                LimbUvs(48, 48, armWidth, 12, 4));
        _leftArmRotation = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0), armX, 24, 0);
        leftArm.Transform = _leftArmRotation;
        _scene.Children.Add(leftArm);

        // 右腿
        var rightLeg = new Model3DGroup();
        AddBox(rightLeg, skin, 4, 12, 4, new Point3D(-2, 6, 0), LimbUvs(0, 16, 4, 12, 4));
        if (modern)
            AddBox(rightLeg, skin, 4.25, 12.25, 4.25, new Point3D(-2, 6, 0), LimbUvs(0, 32, 4, 12, 4));
        _rightLegRotation = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0), -2, 12, 0);
        rightLeg.Transform = _rightLegRotation;
        _scene.Children.Add(rightLeg);

        // 左腿
        var leftLeg = new Model3DGroup();
        AddBox(leftLeg, skin, 4, 12, 4, new Point3D(2, 6, 0), LimbUvs(16, 48, 4, 12, 4));
        if (modern)
            AddBox(leftLeg, skin, 4.25, 12.25, 4.25, new Point3D(2, 6, 0), LimbUvs(0, 48, 4, 12, 4));
        _leftLegRotation = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(1, 0, 0), 0), 2, 12, 0);
        leftLeg.Transform = _leftLegRotation;
        _scene.Children.Add(leftLeg);
    }

    /// <summary>头部 UV：base 为皮肤上的左上角像素（0,0 或 32,0）。</summary>
    private static BoxUv HeadUvs(double x, double y) => new(
        Right: new Rect(x, y + 8, 8, 8),
        Front: new Rect(x + 8, y + 8, 8, 8),
        Left: new Rect(x + 16, y + 8, 8, 8),
        Back: new Rect(x + 24, y + 8, 8, 8),
        Top: new Rect(x + 8, y, 8, 8),
        Bottom: new Rect(x + 16, y, 8, 8));

    private static BoxUv BodyUvs(double x, double y, double w, double h, double d) => LayoutUvs(x, y, w, h, d);

    private static BoxUv LimbUvs(double x, double y, double w, double h, double d) => LayoutUvs(x, y, w, h, d);

    /// <summary>标准 64×64 皮肤的箱式 UV 布局。</summary>
    private static BoxUv LayoutUvs(double x, double y, double w, double h, double d) => new(
        Right: new Rect(x, y + d, d, h),
        Front: new Rect(x + d, y + d, w, h),
        Left: new Rect(x + d + w, y + d, d, h),
        Back: new Rect(x + d + w + d, y + d, w, h),
        Top: new Rect(x + d, y, w, d),
        Bottom: new Rect(x + d + w, y, w, d));

    private readonly record struct BoxUv(Rect Right, Rect Front, Rect Left, Rect Back, Rect Top, Rect Bottom);

    /// <summary>按“从外部看”的顺序生成 6 个面，每个面使用自己的裁剪贴图。</summary>
    private void AddBox(Model3DGroup group, BitmapSource skin, double w, double h, double d,
        Point3D center, BoxUv uv)
    {
        var hx = w / 2;
        var hy = h / 2;
        var hz = d / 2;

        AddQuad(group, FaceMaterial(skin, uv.Front), center,
            new Point3D(-hx, -hy, hz), new Point3D(hx, -hy, hz), new Point3D(hx, hy, hz), new Point3D(-hx, hy, hz));
        AddQuad(group, FaceMaterial(skin, uv.Back), center,
            new Point3D(hx, -hy, -hz), new Point3D(-hx, -hy, -hz), new Point3D(-hx, hy, -hz), new Point3D(hx, hy, -hz));
        AddQuad(group, FaceMaterial(skin, uv.Left), center,
            new Point3D(hx, -hy, hz), new Point3D(hx, -hy, -hz), new Point3D(hx, hy, -hz), new Point3D(hx, hy, hz));
        AddQuad(group, FaceMaterial(skin, uv.Right), center,
            new Point3D(-hx, -hy, -hz), new Point3D(-hx, -hy, hz), new Point3D(-hx, hy, hz), new Point3D(-hx, hy, -hz));
        AddQuad(group, FaceMaterial(skin, uv.Top), center,
            new Point3D(-hx, hy, -hz), new Point3D(hx, hy, -hz), new Point3D(hx, hy, hz), new Point3D(-hx, hy, hz));
        AddQuad(group, FaceMaterial(skin, uv.Bottom), center,
            new Point3D(-hx, -hy, hz), new Point3D(hx, -hy, hz), new Point3D(hx, -hy, -hz), new Point3D(-hx, -hy, -hz));
    }

    private static void AddQuad(Model3DGroup group, Material material, Point3D center,
        Point3D bl, Point3D br, Point3D tr, Point3D tl)
    {
        var mesh = new MeshGeometry3D();
        mesh.Positions.Add(new Point3D(center.X + bl.X, center.Y + bl.Y, center.Z + bl.Z));
        mesh.Positions.Add(new Point3D(center.X + br.X, center.Y + br.Y, center.Z + br.Z));
        mesh.Positions.Add(new Point3D(center.X + tr.X, center.Y + tr.Y, center.Z + tr.Z));
        mesh.Positions.Add(new Point3D(center.X + tl.X, center.Y + tl.Y, center.Z + tl.Z));

        // 顶点顺序：底左、底右、顶右、顶左；对应裁剪贴图的四个角
        mesh.TextureCoordinates.Add(new Point(0, 1));
        mesh.TextureCoordinates.Add(new Point(1, 1));
        mesh.TextureCoordinates.Add(new Point(1, 0));
        mesh.TextureCoordinates.Add(new Point(0, 0));

        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(2);
        mesh.TriangleIndices.Add(1);
        mesh.TriangleIndices.Add(0);
        mesh.TriangleIndices.Add(3);
        mesh.TriangleIndices.Add(2);

        group.Children.Add(new GeometryModel3D
        {
            Geometry = mesh,
            Material = material,
            BackMaterial = material
        });
    }

    /// <summary>每个面单独裁切皮肤，并放大为清晰像素块，避免 WPF 3D 线性过滤把 8×8 贴图糊掉。</summary>
    private Material FaceMaterial(BitmapSource skin, Rect rect)
    {
        var key = ((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
        if (_faceMaterials.TryGetValue(key, out var cached)) return cached;

        var width = Math.Max(1, Math.Min(key.Item3, skin.PixelWidth - key.Item1));
        var height = Math.Max(1, Math.Min(key.Item4, skin.PixelHeight - key.Item2));
        var cropped = new CroppedBitmap(skin, new Int32Rect(key.Item1, key.Item2, width, height));
        cropped.Freeze();

        // 关键：先用最近邻把 1 个皮肤像素放大成 12×12 的实心色块，
        // WPF 3D 的线性采样只会影响块与块之间的 1px 过渡，整体保持像素风。
        var sharp = UpscaleNearest(cropped, 12);

        var brush = new ImageBrush(sharp)
        {
            Stretch = Stretch.Fill,
            ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewport = new Rect(0, 0, 1, 1),
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewbox = new Rect(0, 0, 1, 1),
            TileMode = TileMode.None
        };
        brush.Freeze();

        var material = new DiffuseMaterial(brush);
        material.Freeze();
        _faceMaterials[key] = material;
        return material;
    }

    /// <summary>把裁切出的贴图按最近邻放成实心色块，彻底避免 3D 采样把像素糊掉。</summary>
    private static BitmapSource UpscaleNearest(BitmapSource source, int scale)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var sw = converted.PixelWidth;
        var sh = converted.PixelHeight;
        var srcStride = sw * 4;
        var srcPixels = new byte[srcStride * sh];
        converted.CopyPixels(srcPixels, srcStride, 0);

        var dw = sw * scale;
        var dh = sh * scale;
        var dstStride = dw * 4;
        var dstPixels = new byte[dstStride * dh];

        for (var y = 0; y < dh; y++)
        {
            var sy = y / scale;
            for (var x = 0; x < dw; x++)
            {
                var si = sy * srcStride + (x / scale) * 4;
                var di = y * dstStride + x * 4;
                dstPixels[di] = srcPixels[si];
                dstPixels[di + 1] = srcPixels[si + 1];
                dstPixels[di + 2] = srcPixels[si + 2];
                dstPixels[di + 3] = srcPixels[si + 3];
            }
        }

        var bitmap = BitmapSource.Create(dw, dh, 96, 96, PixelFormats.Bgra32, null, dstPixels, dstStride);
        bitmap.Freeze();
        return bitmap;
    }

    private static bool HasTransparentPixel(BitmapSource skin, int x, int y, int w, int h)
    {
        try
        {
            var converted = new FormatConvertedBitmap(skin, PixelFormats.Bgra32, null, 0);
            var stride = converted.PixelWidth * 4;
            var buffer = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(new Int32Rect(0, 0, converted.PixelWidth, converted.PixelHeight), buffer, stride, 0);
            for (var py = y; py < y + h && py < converted.PixelHeight; py++)
            for (var px = x; px < x + w && px < converted.PixelWidth; px++)
                if (buffer[py * stride + px * 4 + 3] < 16) return true;
        }
        catch { }
        return false;
    }


    private void Fit()
    {
        var center = _headOnly ? new Point3D(0, 28, 0) : new Point3D(0, 16, 0);

        // WPF 的 FieldOfView 作用在较长的一边：按控件尺寸换算，保证模型始终完整入镜
        var aspect = ActualWidth > 1 && ActualHeight > 1 ? ActualWidth / ActualHeight : 1;
        var tanHalf = Math.Tan(Fov * Math.PI / 360);
        var halfHeight = _headOnly ? 6.4 : 18.6;
        var distance = (aspect >= 1 ? halfHeight * aspect : halfHeight) / tanHalf * _zoom;

        var dir = new Vector3D(
            Math.Sin(_yaw) * Math.Cos(_pitch),
            Math.Sin(_pitch),
            Math.Cos(_yaw) * Math.Cos(_pitch));
        _camera.Position = center + dir * distance;
        _camera.LookDirection = -dir * distance;
        _camera.UpDirection = new Vector3D(0, 1, 0);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _dragOrigin = e.GetPosition(this);
        CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        var point = e.GetPosition(this);
        _yaw -= (point.X - _dragOrigin.X) * 0.008;
        _pitch = Math.Clamp(_pitch + (point.Y - _dragOrigin.Y) * 0.006, -0.55, 0.85);
        _dragOrigin = point;
        Fit();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _dragging = false;
        ReleaseMouseCapture();
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        _zoom = Math.Clamp(_zoom * (e.Delta > 0 ? 0.9 : 1.1), 0.55, 1.8);
        Fit();
        e.Handled = true;
    }


    private void Hook()
    {
        if (_frameHooked || !IsVisible) return;
        if (_headOnly || !_walking) return;
        CompositionTarget.Rendering += OnFrame;
        _frameHooked = true;
    }

    private void Unhook()
    {
        if (!_frameHooked) return;
        CompositionTarget.Rendering -= OnFrame;
        _frameHooked = false;
    }

    private void OnFrame(object sender, EventArgs e)
    {
        if (_rightLegRotation == null) return;
        var phase = Math.Sin(_clock.Elapsed.TotalSeconds * 5.2) * 0.55;
        SetAngle(_rightArmRotation, -phase);
        SetAngle(_leftArmRotation, phase);
        SetAngle(_rightLegRotation, phase);
        SetAngle(_leftLegRotation, -phase);
    }

    private static void SetAngle(RotateTransform3D transform, double radians)
    {
        if (transform.Rotation is AxisAngleRotation3D axis)
        {
            var degrees = radians * 180 / Math.PI;
            if (Math.Abs(axis.Angle - degrees) > 0.05) axis.Angle = degrees;
        }
    }


    /// <summary>未登录 / 加载失败时的默认皮肤（简易 Steve 配色）。</summary>
    public static BitmapSource CreateDefaultSkin()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 64, 64));

            var skin = new SolidColorBrush(Color.FromRgb(0xC7, 0x9A, 0x6B));
            var hair = new SolidColorBrush(Color.FromRgb(0x54, 0x33, 0x21));
            var shirt = new SolidColorBrush(Color.FromRgb(0x1F, 0x9C, 0x9C));
            var pants = new SolidColorBrush(Color.FromRgb(0x3B, 0x3B, 0x8C));
            var shoes = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));

            // 头部
            dc.DrawRectangle(hair, null, new Rect(0, 8, 32, 8));   // 两侧与后脑
            dc.DrawRectangle(hair, null, new Rect(8, 0, 16, 8));   // 顶
            dc.DrawRectangle(skin, null, new Rect(8, 8, 8, 8));    // 正面
            dc.DrawRectangle(skin, null, new Rect(16, 0, 8, 8));   // 底
            dc.DrawRectangle(Brushes.White, null, new Rect(9, 11, 2, 2));
            dc.DrawRectangle(Brushes.White, null, new Rect(13, 11, 2, 2));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x3A, 0x2A, 0x1E)), null, new Rect(10, 15, 4, 1));

            // 身体
            dc.DrawRectangle(shirt, null, new Rect(16, 16, 24, 16));
            // 手臂
            dc.DrawRectangle(skin, null, new Rect(40, 16, 16, 16));
            dc.DrawRectangle(skin, null, new Rect(32, 48, 16, 16));
            // 腿
            dc.DrawRectangle(pants, null, new Rect(0, 16, 16, 16));
            dc.DrawRectangle(pants, null, new Rect(16, 48, 16, 16));
            dc.DrawRectangle(shoes, null, new Rect(0, 28, 16, 4));
            dc.DrawRectangle(shoes, null, new Rect(16, 60, 16, 4));
        }

        var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
