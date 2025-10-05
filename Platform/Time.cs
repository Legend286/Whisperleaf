
using System.Diagnostics;

namespace Whisperleaf.Platform;

public static class Time
{
    private static readonly Stopwatch _stopwatch;
    private static double _lastTime;

    public static float DeltaTime { get; private set; }
    public static float TotalTime { get; private set; }

    static Time()
    {
        _stopwatch = Stopwatch.StartNew();
        _lastTime = 0;
    }

    public static void Update()
    {
        double currentTime = _stopwatch.Elapsed.TotalSeconds;
        DeltaTime = (float)(currentTime - _lastTime);
        TotalTime = (float)currentTime;
        _lastTime = currentTime;
    }
    
    public static int FPS => (DeltaTime > 0) ? (int)(1.0f / DeltaTime) : 0;
    public static float Milliseconds => (float)1000.0f / FPS;
}
