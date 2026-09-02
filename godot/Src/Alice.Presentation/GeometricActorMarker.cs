using Godot;

namespace Alice.Presentation;

public static class GeometricPresentationScale
{
    public const float MinimumCameraPixelsPerMeter = 4.0f;
    public const float InitialCameraPixelsPerMeter = 8.0f;
    public const float DefaultCameraPixelsPerMeter = 32.0f;
    public const float CameraZoomStepPixelsPerMeter = 4.0f;
    public const float MetersPerPixel = 1.0f / DefaultCameraPixelsPerMeter;
}

/// <summary>Shared geometry-only Player/NPC marker. It owns display state only.</summary>
public sealed partial class GeometricActorMarker : Node2D
{
    private Vector2 _facing = Vector2.Up;
    private bool _highlighted;
    private float _lastCanvasScale;
    private Vector2 _activityLabelPixelOffset;
    private Vector2 _cognitionLabelPixelOffset;

    [Export] public bool PlayerTriangle { get; set; }
    [Export] public float Radius { get; set; } = 14.0f;
    [Export] public Color FillColor { get; set; } = new("5aa9e6");
    [Export] public Label? ActivityLabel { get; set; }
    [Export] public Label? CognitionLabel { get; set; }

    public override void _Ready()
    {
        if (ActivityLabel is not null) _activityLabelPixelOffset = ActivityLabel.Position;
        if (CognitionLabel is not null) _cognitionLabelPixelOffset = CognitionLabel.Position;
    }

    public void Configure(Color fillColor)
    {
        FillColor = fillColor;
        QueueRedraw();
    }

    public void SetFacing(MotionVectorLike direction)
    {
        var value = new Vector2((float)direction.X, (float)direction.Y);
        if (value.LengthSquared() <= 0.0001f) return;
        _facing = value.Normalized();
        QueueRedraw();
    }

    public void SetActivity(string? activity)
    {
        if (ActivityLabel is null) return;
        ActivityLabel.Text = activity ?? string.Empty;
        ActivityLabel.Visible = false;
    }

    public void SetActivityVisible(bool visible)
    {
        if (ActivityLabel is not null) ActivityLabel.Visible = visible && ActivityLabel.Text.Length > 0;
    }

    public void SetCognition(string? route, Color color)
    {
        if (CognitionLabel is null) return;
        CognitionLabel.Text = route ?? string.Empty;
        CognitionLabel.Modulate = color;
        CognitionLabel.Visible = route is not null;
    }

    public void SetHighlighted(bool highlighted)
    {
        if (_highlighted == highlighted) return;
        _highlighted = highlighted;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _ = delta;
        float canvasScale = GetCanvasTransform().X.Length();
        if (Mathf.IsEqualApprox(canvasScale, _lastCanvasScale)) return;
        _lastCanvasScale = canvasScale;
        float inverseScale = 1.0f / canvasScale;
        if (ActivityLabel is not null)
        {
            ActivityLabel.Scale = Vector2.One * inverseScale;
            ActivityLabel.Position = _activityLabelPixelOffset * inverseScale;
        }
        if (CognitionLabel is not null)
        {
            CognitionLabel.Scale = Vector2.One * inverseScale;
            CognitionLabel.Position = _cognitionLabelPixelOffset * inverseScale;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        float radius = Mathf.Max(Radius, PixelWidth(4.0f));
        Color color = _highlighted ? FillColor.Lightened(0.3f) : FillColor;
        if (PlayerTriangle)
        {
            Vector2 side = new(-_facing.Y, _facing.X);
            Vector2[] points =
            [
                _facing * radius,
                -_facing * radius * 0.72f + side * radius * 0.72f,
                -_facing * radius * 0.72f - side * radius * 0.72f
            ];
            DrawColoredPolygon(points, color);
            return;
        }

        DrawCircle(Vector2.Zero, radius, color);
    }

    private float PixelWidth(float pixels)
    {
        float canvasScale = GetCanvasTransform().X.Length();
        return pixels / canvasScale;
    }
}

/// <summary>Small adapter value so movement code need not expose Godot vectors as domain state.</summary>
public readonly record struct MotionVectorLike(double X, double Y);
