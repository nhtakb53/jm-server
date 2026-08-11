namespace JmServer.GameIntegration;

public enum D2RWindowMode
{
    Windowed = 0,
    Fullscreen = 1
}

public sealed record D2RDisplaySettings(
    D2RWindowMode WindowMode,
    int WindowWidth,
    int WindowHeight)
{
    public const int MinimumWidth = 1280;
    public const int MinimumHeight = 720;
    public const int MaximumWidth = 7680;
    public const int MaximumHeight = 4320;

    public string WindowResolution => $"{WindowWidth}x{WindowHeight}";

    public void Validate()
    {
        if (!Enum.IsDefined(WindowMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(WindowMode),
                WindowMode,
                "지원하지 않는 D2R 화면 모드입니다.");
        }

        if (WindowWidth is < MinimumWidth or > MaximumWidth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(WindowWidth),
                WindowWidth,
                $"창 너비는 {MinimumWidth}~{MaximumWidth} 픽셀이어야 합니다.");
        }

        if (WindowHeight is < MinimumHeight or > MaximumHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(WindowHeight),
                WindowHeight,
                $"창 높이는 {MinimumHeight}~{MaximumHeight} 픽셀이어야 합니다.");
        }
    }
}
