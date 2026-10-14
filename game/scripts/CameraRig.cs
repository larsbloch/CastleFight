using Godot;

namespace CastleFight.Game;

/// <summary>RTS-style camera: pan with WASD/arrows, zoom with the mouse wheel.</summary>
public partial class CameraRig : Node3D
{
    const float PanSpeed = 45f;
    const float MinDistance = 8f;
    const float MaxDistance = 160f;
    const float ZoomStep = 1.15f;

    // From the focus point towards the camera: above and towards +Z, looking across the lane.
    static readonly Vector3 ViewDirection = new Vector3(0f, 1f, 0.8f).Normalized();

    Vector3 _focus;
    Vector2 _bounds;
    float _distance = 90f;

    public Camera3D Camera { get; private set; } = null!;

    public override void _Ready()
    {
        Camera = new Camera3D { Current = true, Far = 2000f };
        AddChild(Camera);
    }

    public void Frame(Vector2 mapSize)
    {
        _bounds = mapSize;
        _focus = new Vector3(mapSize.X / 2f, 0f, mapSize.Y / 2f);
    }

    public override void _Process(double delta)
    {
        var move = Vector3.Zero;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) move.X -= 1f;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) move.X += 1f;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) move.Z -= 1f;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) move.Z += 1f;

        _focus += move * PanSpeed * (_distance / 90f) * (float)delta;
        _focus.X = Mathf.Clamp(_focus.X, 0f, _bounds.X);
        _focus.Z = Mathf.Clamp(_focus.Z, 0f, _bounds.Y);

        Camera.Position = _focus + ViewDirection * _distance;
        Camera.LookAt(_focus, Vector3.Up);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { Pressed: true } button)
            return;
        if (button.ButtonIndex == MouseButton.WheelUp)
            _distance = Mathf.Max(MinDistance, _distance / ZoomStep);
        else if (button.ButtonIndex == MouseButton.WheelDown)
            _distance = Mathf.Min(MaxDistance, _distance * ZoomStep);
    }

    /// <summary>Where the mouse ray hits the ground plane (y = 0), if it does.</summary>
    public Vector3? ScreenToGround(Vector2 screenPosition)
    {
        Vector3 origin = Camera.ProjectRayOrigin(screenPosition);
        Vector3 direction = Camera.ProjectRayNormal(screenPosition);
        if (Mathf.Abs(direction.Y) < 1e-4f)
            return null;
        float distance = -origin.Y / direction.Y;
        return distance < 0f ? null : origin + direction * distance;
    }
}
