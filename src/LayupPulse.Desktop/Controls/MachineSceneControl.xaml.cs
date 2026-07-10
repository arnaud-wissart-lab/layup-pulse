using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using LayupPulse.Domain;

namespace LayupPulse.Desktop.Controls;

public partial class MachineSceneControl : UserControl
{
    private static readonly DiffuseMaterial NormalToolMaterial = CreateMaterial(57, 188, 227);
    private static readonly DiffuseMaterial PausedToolMaterial = CreateMaterial(241, 184, 75);
    private static readonly DiffuseMaterial FaultedToolMaterial = CreateMaterial(240, 106, 106);

    public static readonly DependencyProperty HeadXProperty = DependencyProperty.Register(
        nameof(HeadX),
        typeof(double),
        typeof(MachineSceneControl),
        new PropertyMetadata(100d, OnTelemetryChanged));

    public static readonly DependencyProperty HeadYProperty = DependencyProperty.Register(
        nameof(HeadY),
        typeof(double),
        typeof(MachineSceneControl),
        new PropertyMetadata(75d, OnTelemetryChanged));

    public static readonly DependencyProperty HeadZProperty = DependencyProperty.Register(
        nameof(HeadZ),
        typeof(double),
        typeof(MachineSceneControl),
        new PropertyMetadata(25d, OnTelemetryChanged));

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress),
        typeof(double),
        typeof(MachineSceneControl),
        new PropertyMetadata(0d, OnTelemetryChanged));

    public static readonly DependencyProperty MachineStateProperty = DependencyProperty.Register(
        nameof(MachineState),
        typeof(MachineState),
        typeof(MachineSceneControl),
        new PropertyMetadata(MachineState.Disconnected, OnTelemetryChanged));

    private readonly Point3DCollection _plannedPath = CreatePlannedPath();
    private readonly TranslateTransform3D _toolTransform = new();
    private HelixViewport3D? _viewport;
    private TubeVisual3D? _depositedPath;
    private TubeVisual3D? _remainingPath;
    private BoxVisual3D? _toolHead;
    private int _lastProgressBucket = -1;
    private MachineState _lastState = MachineState.Disconnected;

    public MachineSceneControl()
    {
        InitializeComponent();
        try
        {
            InitializeScene();
            UpdateScene();
        }
        catch (Exception exception)
        {
            ShowFallback(exception);
        }
    }

    public double HeadX
    {
        get => (double)GetValue(HeadXProperty);
        set => SetValue(HeadXProperty, value);
    }

    public double HeadY
    {
        get => (double)GetValue(HeadYProperty);
        set => SetValue(HeadYProperty, value);
    }

    public double HeadZ
    {
        get => (double)GetValue(HeadZProperty);
        set => SetValue(HeadZProperty, value);
    }

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public MachineState MachineState
    {
        get => (MachineState)GetValue(MachineStateProperty);
        set => SetValue(MachineStateProperty, value);
    }

    private static void OnTelemetryChanged(DependencyObject sender, DependencyPropertyChangedEventArgs eventArgs) =>
        ((MachineSceneControl)sender).UpdateScene();

    private void InitializeScene()
    {
        _viewport = new HelixViewport3D
        {
            Background = new SolidColorBrush(Color.FromRgb(11, 18, 24)),
            CameraRotationMode = CameraRotationMode.Turntable,
            IsHeadLightEnabled = true,
            LimitFPS = true,
            ModelUpDirection = new Vector3D(0, 0, 1),
            ShowCoordinateSystem = true,
            ShowViewCube = true,
            ZoomExtentsWhenLoaded = false,
        };
        SetDefaultCamera();

        _viewport.Children.Add(new GridLinesVisual3D
        {
            Center = new Point3D(0, 0, 0),
            Length = 12,
            Width = 7,
            MajorDistance = 1,
            MinorDistance = 0.5,
            Thickness = 0.008,
            Fill = CreateBrush(44, 65, 78),
        });
        _viewport.Children.Add(new PipeVisual3D
        {
            Point1 = new Point3D(-4.4, 0, 1.5),
            Point2 = new Point3D(4.4, 0, 1.5),
            Diameter = 1.6,
            InnerDiameter = 0,
            ThetaDiv = 24,
            Material = CreateMaterial(65, 83, 94),
        });
        _viewport.Children.Add(new TubeVisual3D
        {
            Path = _plannedPath,
            Diameter = 0.035,
            ThetaDiv = 6,
            Material = CreateMaterial(53, 79, 93),
        });

        _remainingPath = new TubeVisual3D
        {
            Diameter = 0.055,
            ThetaDiv = 6,
            Material = CreateMaterial(108, 135, 151),
        };
        _depositedPath = new TubeVisual3D
        {
            Diameter = 0.085,
            ThetaDiv = 8,
            Material = CreateMaterial(66, 200, 145),
        };
        _viewport.Children.Add(_remainingPath);
        _viewport.Children.Add(_depositedPath);

        _toolHead = new BoxVisual3D
        {
            Center = new Point3D(0, 0, 0),
            Length = 0.46,
            Width = 0.38,
            Height = 0.62,
            Material = NormalToolMaterial,
            Transform = _toolTransform,
        };
        _viewport.Children.Add(_toolHead);
        _viewport.Children.Add(new PipeVisual3D
        {
            Point1 = new Point3D(-4.7, -1.7, 0),
            Point2 = new Point3D(-4.7, -1.7, 2.2),
            Diameter = 0.08,
            ThetaDiv = 8,
            Material = CreateMaterial(95, 118, 130),
        });

        SceneHost.Children.Add(_viewport);
    }

    private void UpdateScene()
    {
        if (_viewport is null || _toolHead is null || _depositedPath is null || _remainingPath is null)
        {
            return;
        }

        Point3D toolPosition = MapTelemetryToScene(HeadX, HeadY, HeadZ);
        _toolTransform.OffsetX = toolPosition.X;
        _toolTransform.OffsetY = toolPosition.Y;
        _toolTransform.OffsetZ = toolPosition.Z + 0.42;

        int progressBucket = Math.Clamp((int)Math.Round(Progress), 0, 100);
        if (progressBucket != _lastProgressBucket
            || (MachineState == MachineState.Running && _lastState is MachineState.Completed or MachineState.Ready))
        {
            UpdatePathProgress(progressBucket);
            _lastProgressBucket = progressBucket;
        }

        if (MachineState != _lastState)
        {
            UpdateStateAppearance();
        }

        _lastState = MachineState;
    }

    private void UpdatePathProgress(int progressBucket)
    {
        int splitIndex = (int)Math.Round((_plannedPath.Count - 1) * progressBucket / 100d);
        _depositedPath!.Path = SlicePath(0, Math.Max(1, splitIndex));
        _remainingPath!.Path = SlicePath(Math.Min(splitIndex, _plannedPath.Count - 2), _plannedPath.Count - 1);
    }

    private Point3DCollection SlicePath(int startIndex, int endIndex)
    {
        Point3DCollection points = new(endIndex - startIndex + 1);
        for (int index = startIndex; index <= endIndex; index++)
        {
            points.Add(_plannedPath[index]);
        }

        points.Freeze();
        return points;
    }

    private void UpdateStateAppearance()
    {
        switch (MachineState)
        {
            case MachineState.Paused:
                _toolHead!.Material = PausedToolMaterial;
                ShowStateBanner("Ⅱ  CYCLE EN PAUSE", isFault: false);
                break;
            case MachineState.Faulted:
                _toolHead!.Material = FaultedToolMaterial;
                ShowStateBanner("!  DÉFAUT SIMULÉ ACTIF", isFault: true);
                break;
            default:
                _toolHead!.Material = NormalToolMaterial;
                StateBanner.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void ShowStateBanner(string text, bool isFault)
    {
        StateBannerText.Text = text;
        StateBanner.Background = new SolidColorBrush(
            isFault ? Color.FromArgb(230, 66, 33, 38) : Color.FromArgb(230, 61, 50, 26));
        StateBanner.BorderBrush = (Brush)FindResource(isFault ? "DangerBrush" : "WarningBrush");
        StateBanner.Visibility = Visibility.Visible;
    }

    private void SetDefaultCamera()
    {
        if (_viewport is null)
        {
            return;
        }

        _viewport.Camera = new PerspectiveCamera
        {
            Position = new Point3D(8.7, -9.5, 7.2),
            LookDirection = new Vector3D(-8.7, 9.5, -5.6),
            UpDirection = new Vector3D(0, 0, 1),
            FieldOfView = 42,
        };
    }

    private void OnResetCameraClicked(object sender, RoutedEventArgs eventArgs) => SetDefaultCamera();

    private void OnFitViewClicked(object sender, RoutedEventArgs eventArgs) => _viewport?.ZoomExtents(250);

    private void ShowFallback(Exception exception)
    {
        SceneHost.Children.Clear();
        SceneHost.Children.Add(new Border
        {
            Padding = new Thickness(24),
            Background = CreateBrush(15, 26, 35),
            Child = new TextBlock
            {
                Text = $"Visualisation 3D indisponible\n{exception.Message}\n\nLes commandes et la télémétrie restent actives.",
                Foreground = (Brush)FindResource("SecondaryTextBrush"),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
    }

    private static Point3D MapTelemetryToScene(double x, double y, double z)
    {
        double sceneX = Math.Clamp((x - 500) / 100, -4, 4);
        double angle = Math.Clamp((y - 232.5) / 157.5, -1, 1) * 1.1;
        double radialOffset = 0.84 + Math.Clamp((z - 25) / 100, -0.05, 0.15);
        return new Point3D(
            sceneX,
            radialOffset * Math.Sin(angle),
            1.5 + radialOffset * Math.Cos(angle));
    }

    private static Point3DCollection CreatePlannedPath()
    {
        Point3DCollection points = new(80);
        const int passCount = 8;
        const int pointsPerPass = 9;
        for (int pass = 0; pass < passCount; pass++)
        {
            double angle = -1.05 + (2.1 * pass / (passCount - 1));
            for (int point = 0; point < pointsPerPass; point++)
            {
                double fraction = point / (double)(pointsPerPass - 1);
                double x = -4 + (8 * (pass % 2 == 0 ? fraction : 1 - fraction));
                points.Add(new Point3D(x, 0.84 * Math.Sin(angle), 1.5 + (0.84 * Math.Cos(angle))));
            }
        }

        points.Freeze();
        return points;
    }

    private static SolidColorBrush CreateBrush(byte red, byte green, byte blue)
    {
        SolidColorBrush brush = new(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static DiffuseMaterial CreateMaterial(byte red, byte green, byte blue)
    {
        DiffuseMaterial material = new(CreateBrush(red, green, blue));
        material.Freeze();
        return material;
    }
}
