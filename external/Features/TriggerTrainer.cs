using System;
using System.Diagnostics;
using CS2Cheat.Utils;
using CS2Cheat.Graphics;
using SharpDX;
using Color = SharpDX.Color;

namespace CS2Cheat.Features;

public static class TriggerTrainer
{
    private static readonly Random Rng = new();
    private static readonly Stopwatch Stopwatch = Stopwatch.StartNew();
    private static bool _hasTarget;
    private static Vector2 _targetPos;
    private const float TargetRadius = 28f;
    private static long _spawnTimeMs;
    private static long _lastSpawnAtMs;
    private const int SpawnCooldownMs = 900;

    private static int _tries;
    private static double _lastReactionMs;
    private static double _bestReactionMs = double.MaxValue;
    private static bool _prevMouseDown;

    public static void Draw(CS2Cheat.Graphics.Graphics graphics)
    {
        if (graphics == null) return;

        var rect = graphics.GameProcess.WindowRectangleClient;
        var screenW = rect.Width > 0 ? rect.Width : 1920;
        var screenH = rect.Height > 0 ? rect.Height : 1080;

        var now = Stopwatch.ElapsedMilliseconds;
        if (!_hasTarget && now - _lastSpawnAtMs > SpawnCooldownMs)
        {
            SpawnTarget(screenW, screenH);
        }

        if (_hasTarget)
        {
            var tl = new Vector2(_targetPos.X - TargetRadius, _targetPos.Y - TargetRadius);
            var br = new Vector2(_targetPos.X + TargetRadius, _targetPos.Y + TargetRadius);
            graphics.DrawFilledRectangle(new Color(220, 40, 40, 200), tl, br);
            graphics.DrawRectangle(Color.White, tl, br);

            var font = graphics.FontConsolas32;
            if (font != null)
            {
                font.DrawText(null, "CLICK", (int)(_targetPos.X - 16), (int)(_targetPos.Y - 10), Color.White);
            }
        }

        var nowDown = Utility.IsKeyDown(Process.NET.Native.Types.Keys.LButton);
        if (nowDown && !_prevMouseDown && _hasTarget)
        {
            var reaction = Stopwatch.Elapsed.TotalMilliseconds - _spawnTimeMs;
            _lastReactionMs = reaction;
            _bestReactionMs = Math.Min(_bestReactionMs, reaction);
            _tries++;
            _hasTarget = false;
            _lastSpawnAtMs = Stopwatch.ElapsedMilliseconds;
        }
        _prevMouseDown = nowDown;

        var statsFont = graphics.FontConsolas32;
        if (statsFont != null)
        {
            var x = 12;
            var y = 12;
            statsFont.DrawText(null, "Trigger Trainer", x, y, Color.Lime);
            y += 20;
            statsFont.DrawText(null, $"Tries: {_tries}", x, y, Color.White);
            y += 18;
            statsFont.DrawText(null, $"Last: {(_lastReactionMs > 0 ? _lastReactionMs.ToString("0.00") : "-")} ms", x, y, Color.White);
            y += 18;
            statsFont.DrawText(null, $"Best: {(_bestReactionMs < double.MaxValue ? _bestReactionMs.ToString("0.00") : "-")} ms", x, y, Color.White);
        }
    }

    private static void SpawnTarget(int screenW, int screenH)
    {
        const int margin = 80;
        var x = Rng.Next(margin, Math.Max(margin + 1, screenW - margin));
        var y = Rng.Next(margin, Math.Max(margin + 1, screenH - margin));
        _targetPos = new Vector2(x, y);
        _hasTarget = true;
        _spawnTimeMs = (long)Stopwatch.Elapsed.TotalMilliseconds;
    }
}
