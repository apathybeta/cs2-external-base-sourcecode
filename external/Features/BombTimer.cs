using CS2Cheat.Utils;
using CS2Cheat.Graphics;
using SharpDX;
using Color = SharpDX.Color;

namespace CS2Cheat.Features;

internal class BombTimer : ThreadedServiceBase
{
    private readonly Graphics.Graphics _graphics;

    private static string _bombPlanted = string.Empty;
    private static string _bombSite = string.Empty;
    private static bool _isBombPlanted;
    private static float _defuseLeft;
    private static float _timeLeft;
    private static float _defuseCountDown;
    private static float _c4Blow;
    private static bool _beingDefused;
    private float _currentTime;
    private IntPtr _globalVars;
    private IntPtr _plantedC4;
    private IntPtr _tempC4;
    // removed world-origin tracking to avoid unreliable coordinate projection

    public BombTimer(Graphics.Graphics graphics)
    {
        _graphics = graphics;
    }

    protected override void FrameAction()
    {
        _globalVars = _graphics.GameProcess.ModuleClient.Read<IntPtr>(Offsets.dwGlobalVars);
        // Read current game time from gpGlobals (offset 0x30)
        if (_globalVars != IntPtr.Zero)
        {
            try
            {
                _currentTime = _graphics.GameProcess.Process.Read<float>(_globalVars + 0x30);
            }
            catch
            {
                _currentTime = 0f;
            }
        }
        else
        {
            _currentTime = 0f;
        }

        // Read planted C4 pointer safely
        _tempC4 = _graphics.GameProcess.ModuleClient.Read<IntPtr>(Offsets.dwPlantedC4);
        _plantedC4 = IntPtr.Zero;
        _isBombPlanted = false;

        try
        {
            if (_tempC4 != IntPtr.Zero)
            {
                _plantedC4 = _graphics.GameProcess.Process.Read<IntPtr>(_tempC4);
                _isBombPlanted = _graphics.GameProcess.ModuleClient.Read<bool>(Offsets.dwPlantedC4 - 0x8);
            }
        }
        catch
        {
            _plantedC4 = IntPtr.Zero;
            _isBombPlanted = false;
        }

        // World-origin resolution removed: bomb origin reads and candidate offset probing
        // were unreliable and have been intentionally disabled.

        // Read timers and defuse state
        if (_plantedC4 != IntPtr.Zero)
        {
            try
            {
                _defuseCountDown = _graphics.GameProcess.Process.Read<float>(_plantedC4 + Offsets.m_flDefuseCountDown);
                _c4Blow = _graphics.GameProcess.Process.Read<float>(_plantedC4 + Offsets.m_flC4Blow);
                _beingDefused = _graphics.GameProcess.Process.Read<bool>(_plantedC4 + Offsets.m_bBeingDefused);
            }
            catch
            {
                _defuseCountDown = 0f;
                _c4Blow = 0f;
                _beingDefused = false;
            }

            _timeLeft = _c4Blow - _currentTime;
            _defuseLeft = _defuseCountDown - _currentTime;
        }
        else
        {
            _timeLeft = 0f;
            _defuseLeft = 0f;
            _beingDefused = false;
        }

        _timeLeft = Math.Max(_timeLeft, 0);
        _defuseLeft = Math.Max(_defuseLeft, 0);

        if (!_beingDefused)
            _defuseLeft = 0;

        if (_isBombPlanted && _plantedC4 != IntPtr.Zero)
            _bombSite = _graphics.GameProcess.Process.Read<int>(_plantedC4 + Offsets.m_nBombSite) == 1 ? "B" : "A";

        _bombPlanted = _isBombPlanted ? $"Bomb is planted on site: {_bombSite}" : string.Empty;

        try
        {
            if (_plantedC4 != IntPtr.Zero)
                _graphics.GameProcess.Process.Read<bool>(_plantedC4 + Offsets.m_bBombDefused);
        }
        catch
        {
            // ignore
        }
    }

    // No TryGetBombScreenInfo: world coordinate tracking removed due to unreliable projection.

    public static void Draw(Graphics.Graphics graphics)
    {
        // Only draw when bomb planted
        if (!_isBombPlanted) return;

        // Get screen size from game process window rectangle (fallback to constants if not available)
        var rect = graphics.GameProcess.WindowRectangleClient;
        var screenW = rect.Width > 0 ? rect.Width : 1920;
        var screenH = rect.Height > 0 ? rect.Height : 1080;

        // Vertical bar settings (left side, centered vertically)
        const float barWidth = 18f;
        const float barHeight = 300f;
        var barLeft = 20f; // moved to left side
        var barTop = (screenH / 2f) - (barHeight / 2f);

        // Maximum bomb time for normalization (CS2 default ~40s)
        const float maxBombTime = 40f;
        var normalized = Math.Clamp(_timeLeft / maxBombTime, 0f, 1f);

        // Color mapping: green (full) -> yellow (mid) -> red (low)
        Color timerColor;
        if (normalized >= 0.5f)
        {
            var t = (normalized - 0.5f) / 0.5f; // 0..1
            var r = (byte)(255 * t);
            timerColor = new Color(r, (byte)255, (byte)0, (byte)220);
        }
        else
        {
            var t = normalized / 0.5f; // 0..1
            var g = (byte)(255 * t);
            timerColor = new Color((byte)255, g, (byte)0, (byte)220);
        }

        // Draw vertical background and border
        var bg = new Color((byte)20, (byte)20, (byte)20, (byte)200);
        graphics.DrawFilledRectangle(bg, new SharpDX.Vector2(barLeft, barTop),
            new SharpDX.Vector2(barLeft + barWidth, barTop + barHeight));
        graphics.DrawRectangle(Color.White, new SharpDX.Vector2(barLeft, barTop),
            new SharpDX.Vector2(barLeft + barWidth, barTop + barHeight));

        // Filled portion (fill from bottom to top)
        var filledH = barHeight * normalized;
        var filledTop = barTop + (barHeight - filledH);
        graphics.DrawFilledRectangle(timerColor, new SharpDX.Vector2(barLeft, filledTop),
            new SharpDX.Vector2(barLeft + barWidth, barTop + barHeight));

        // Draw remaining time to the right of the bar
        var timeText = $"{_timeLeft:0.00}s";
        var smallFont = graphics.FontConsolas32;
        if (smallFont != null)
        {
            var tx = (int)(barLeft + barWidth + 8);
            var ty = (int)(barTop + (barHeight / 2f) - 8);
            smallFont.DrawText(null, timeText, tx, ty, Color.White);
        }

        // Show defuse status and remaining defuse time (larger)
        var defuseText = _beingDefused ? $"Defusing: YES ({_defuseLeft:0.00}s)" : "Defusing: NO";
        var bigFont = graphics.FontAzonix64;
        if (bigFont != null)
        {
            var dx = (int)(barLeft + barWidth + 8);
            var dy = (int)(barTop + barHeight + 6);
            bigFont.DrawText(default, defuseText, dx, dy, Color.WhiteSmoke);
        }

        // World drawing removed: BombTimer no longer attempts to transform world origins for ESP
    }
}
