using System.Diagnostics;

namespace CS2Cheat.Utils;

public class FpsCounter
{
    #region

    public void Update()
    {
        var fpsTimerElapsed = FpsTimer.Elapsed;
        if (fpsTimerElapsed > TimeSpanFpsUpdate)
        {
            // calculate without artificial clamp so high-Hz systems can report >240
            var calculated = (int)(FpsFrameCount / fpsTimerElapsed.TotalSeconds);
            Fps = Math.Max(0, calculated);
            FpsTimer.Restart();
            FpsFrameCount = 0;
        }

        FpsFrameCount++;
    }

    #endregion

    #region

    // Update interval set to 8ms to report high Hz quickly on high-refresh systems.
    private static readonly TimeSpan TimeSpanFpsUpdate = TimeSpan.FromMilliseconds(8);


    private Stopwatch FpsTimer { get; } = Stopwatch.StartNew();


    private int FpsFrameCount { get; set; }


    public int Fps { get; private set; }

    #endregion
}