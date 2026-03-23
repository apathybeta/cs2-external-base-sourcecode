using CS2Cheat.Features;
using CS2Cheat.Graphics;
using CS2Cheat.Utils;
using Process.NET.Native.Types;
using SharpDX;
using Keys = Process.NET.Native.Types.Keys;
using Color = SharpDX.Color;

namespace CS2Cheat.Utils;

public static class MenuManager
{
    // input state
    private static bool _prevInsertDown;
    private static bool _prevD1Down;
    private static bool _prevD2Down;
    private static bool _prevD3Down;
    private static bool _prevD4Down;
    private static bool _prevMouseLeftDown;

    // menu position/size
    private static float _menuLeft = 50f;
    private static float _menuTop = 50f;
    private static float _menuWidth = 360f;
    private static float _menuHeight = 300f;
    private static float _dragOffsetX;
    private static float _dragOffsetY;
    private static bool _isDragging;

    private static int _activeTab = 0;
    private static readonly string[] _tabs = { "AIM", "VISUALS", "MISC" };

    public static bool IsVisible { get; private set; }

    // features
    public static bool BoxEnabled { get; set; } = true;
    public static bool SkeletonEnabled { get; set; } = false;

    private static readonly SharpDX.Color[] Palette =
    {
        SharpDX.Color.Red, SharpDX.Color.Green, SharpDX.Color.Blue, SharpDX.Color.Yellow,
        SharpDX.Color.White, SharpDX.Color.Cyan, SharpDX.Color.Magenta, SharpDX.Color.Orange
    };

    private static int _boxColorIndex = 0;
    private static int _skeletonColorIndex = 1;

    private static bool _initialized;

    // key capture
    private static bool _capturingKey;
    private static string _capturingFor = "";

    public static SharpDX.Color BoxColor => Palette[_boxColorIndex % Palette.Length];
    public static SharpDX.Color SkeletonColor => Palette[_skeletonColorIndex % Palette.Length];

    // transient UI-config values (persisted)
    private static float MenuOpacity = 0.92f;
    private static float BoxThickness = 1.0f;

    public static void UpdateInput()
    {
        var insertDown = Utility.IsKeyDown(Keys.Insert);
        if (insertDown && !_prevInsertDown)
            IsVisible = !IsVisible;

        if (!IsVisible)
        {
            _prevInsertDown = insertDown;
            _prevD1Down = Utility.IsKeyDown(Keys.D1);
            _prevD2Down = Utility.IsKeyDown(Keys.D2);
            _prevD3Down = Utility.IsKeyDown(Keys.D3);
            _prevD4Down = Utility.IsKeyDown(Keys.D4);
            return;
        }

        var d1 = Utility.IsKeyDown(Keys.D1);
        if (d1 && !_prevD1Down)
        {
            BoxEnabled = !BoxEnabled;
            SaveConfig();
        }

        var d2 = Utility.IsKeyDown(Keys.D2);
        if (d2 && !_prevD2Down)
        {
            SkeletonEnabled = !SkeletonEnabled;
            SaveConfig();
        }

        var d3 = Utility.IsKeyDown(Keys.D3);
        if (d3 && !_prevD3Down)
        {
            _boxColorIndex = (_boxColorIndex + 1) % Palette.Length;
            SaveConfig();
        }

        var d4 = Utility.IsKeyDown(Keys.D4);
        if (d4 && !_prevD4Down)
        {
            _skeletonColorIndex = (_skeletonColorIndex + 1) % Palette.Length;
            SaveConfig();
        }

        _prevInsertDown = insertDown;
        _prevD1Down = d1;
        _prevD2Down = d2;
        _prevD3Down = d3;
        _prevD4Down = d4;
    }

    public static void Draw(Graphics.Graphics graphics)
    {
        if (!IsVisible) return;

        var left = _menuLeft;
        var top = _menuTop;
        var width = _menuWidth;
        var height = _menuHeight;

        // try get cursor local coordinates
        var localCursor = TryGetLocalCursor(graphics, out var localX, out var localY);

        // title drag and basic clicks
        if (localCursor)
        {
            var mouseDown = Utility.IsKeyDown(Keys.LButton);
            var inTitle = localX >= left && localX <= left + width && localY >= top && localY <= top + 22f;

            var clicked = mouseDown && !_prevMouseLeftDown;
            if (clicked && inTitle)
            {
                _isDragging = true;
                _dragOffsetX = localX - left;
                _dragOffsetY = localY - top;
            }

            if (_isDragging && mouseDown)
            {
                _menuLeft = Math.Clamp(localX - _dragOffsetX, 0f, Math.Max(0, graphics.GameProcess.WindowRectangleClient.Width - (int)width));
                _menuTop = Math.Clamp(localY - _dragOffsetY, 0f, Math.Max(0, graphics.GameProcess.WindowRectangleClient.Height - (int)height));
                left = _menuLeft; top = _menuTop;
            }

            if (!mouseDown && _isDragging) _isDragging = false;

            _prevMouseLeftDown = mouseDown;
        }

        // load config once
        if (!_initialized)
        {
            var cfg = ConfigManager.Load();
            BoxEnabled = cfg.EspBox;
            SkeletonEnabled = cfg.SkeletonEsp;
            _boxColorIndex = Math.Clamp(cfg.BoxColorIndex, 0, Palette.Length - 1);
            _skeletonColorIndex = Math.Clamp(cfg.SkeletonColorIndex, 0, Palette.Length - 1);
            MenuOpacity = cfg.MenuOpacity;
            BoxThickness = cfg.BoxThickness;
            _initialized = true;
        }

        // background
        var bgColor = new Color((byte)22, (byte)22, (byte)22, (byte)(255 * MenuOpacity));
        graphics.DrawFilledRectangle(bgColor, new SharpDX.Vector2(left, top), new SharpDX.Vector2(left + width, top + height));
        graphics.DrawRectangle(Color.White, new SharpDX.Vector2(left, top), new SharpDX.Vector2(left + width, top + height));

        var font = graphics.FontConsolas32!;
        font.DrawText(null, "apathy.pub menu", (int)left + 12, (int)top + 6, Color.White);

        // tabs
        var tabWidth = 100f;
        for (var i = 0; i < _tabs.Length; i++)
        {
            var tTop = top + 28f + i * 40f;
            var tTL = new SharpDX.Vector2(left, tTop);
            var tBR = new SharpDX.Vector2(left + tabWidth, tTop + 36f);
            var active = i == _activeTab;
            var tabBg = active ? new Color(60, 60, 60, 200) : new Color(30, 30, 30, 160);
            graphics.DrawFilledRectangle(tabBg, tTL, tBR);
            graphics.DrawRectangle(Color.Gray, tTL, tBR);
            font.DrawText(null, _tabs[i], (int)left + 12, (int)tTop + 8, Color.White);
        }

        var contentX = left + tabWidth;
        var contentY = top + 28f;
        var contentW = width - tabWidth - 12f;
        font.DrawText(null, _tabs[_activeTab], (int)contentX + 8, (int)contentY + 6, Color.White);

        if (_activeTab == 1) // visuals
        {
            var togX = contentX + 12;
            var togY = contentY + 32f;

            // box toggle
            graphics.DrawRectangle(Color.Gray, new SharpDX.Vector2(togX, togY), new SharpDX.Vector2(togX + 18, togY + 18));
            if (BoxEnabled) graphics.DrawFilledRectangle(BoxColor, new SharpDX.Vector2(togX + 2, togY + 2), new SharpDX.Vector2(togX + 16, togY + 16));
            font.DrawText(null, "Box", (int)togX + 28, (int)togY + 2, Color.White);

            // skeleton toggle
            var tog2Y = contentY + 60f;
            graphics.DrawRectangle(Color.Gray, new SharpDX.Vector2(togX, tog2Y), new SharpDX.Vector2(togX + 18, tog2Y + 18));
            if (SkeletonEnabled) graphics.DrawFilledRectangle(SkeletonColor, new SharpDX.Vector2(togX + 2, tog2Y + 2), new SharpDX.Vector2(togX + 16, tog2Y + 16));
            font.DrawText(null, "Skeleton", (int)togX + 28, (int)tog2Y + 2, Color.White);

            // color preview
            var colorY = contentY + 96f;
            font.DrawText(null, "Box color:", (int)togX, (int)colorY, Color.White);
            var previewX = togX + 90;
            var previewY = colorY - 4;
            graphics.DrawFilledRectangle(BoxColor, new SharpDX.Vector2(previewX, previewY), new SharpDX.Vector2(previewX + 60, previewY + 20));

            // ON/OFF buttons
            var btnX = togX + contentW - 70;
            var btnY = togY - 4;
            DrawButton(graphics, font, "ON", btnX, btnY, BoxEnabled, () => { BoxEnabled = true; SaveConfig(); });
            DrawButton(graphics, font, "OFF", btnX + 32, btnY, !BoxEnabled, () => { BoxEnabled = false; SaveConfig(); });

            var btn2Y = tog2Y - 4;
            DrawButton(graphics, font, "ON", btnX, btn2Y, SkeletonEnabled, () => { SkeletonEnabled = true; SaveConfig(); });
            DrawButton(graphics, font, "OFF", btnX + 32, btn2Y, !SkeletonEnabled, () => { SkeletonEnabled = false; SaveConfig(); });

            // sliders
            var sliderX = togX;
            var sliderY = contentY + 140f;
            font.DrawText(null, "Menu opacity", (int)sliderX, (int)sliderY, Color.White);
            DrawSlider(graphics, new SharpDX.Vector2(sliderX + 110, sliderY), 140f, 0.2f, 1.0f, ref MenuOpacity, (v) => { MenuOpacity = v; SaveConfig(); });

            var slider2Y = sliderY + 32f;
            font.DrawText(null, "Box thickness", (int)sliderX, (int)slider2Y, Color.White);
            DrawSlider(graphics, new SharpDX.Vector2(sliderX + 110, slider2Y), 140f, 0.5f, 4.0f, ref BoxThickness, (v) => { BoxThickness = v; SaveConfig(); });

            // keybinds
            var kbY = slider2Y + 36f;
            font.DrawText(null, "Aim key:", (int)sliderX, (int)kbY, Color.White);
            var kbX = sliderX + 90;
            var cfg = ConfigManager.Load();
            DrawKeybindButton(graphics, font, kbX, kbY - 4, 100, 20, cfg.AimBotKey.ToString(), "AimKey", () => StartKeyCapture("AimKey"));

            var kb2Y = kbY + 28f;
            font.DrawText(null, "Trigger key:", (int)sliderX, (int)kb2Y, Color.White);
            DrawKeybindButton(graphics, font, kbX, kb2Y - 4, 100, 20, cfg.TriggerBotKey.ToString(), "TriggerKey", () => StartKeyCapture("TriggerKey"));

            // palette grid
            var palX = previewX;
            var palY = previewY + 30;
            var cell = 18f;
            for (var p = 0; p < Palette.Length; p++)
            {
                var px = palX + (p % 4) * (cell + 6);
                var py = palY + (p / 4) * (cell + 6);
                graphics.DrawFilledRectangle(Palette[p], new SharpDX.Vector2(px, py), new SharpDX.Vector2(px + cell, py + cell));
                graphics.DrawRectangle(Color.Gray, new SharpDX.Vector2(px, py), new SharpDX.Vector2(px + cell, py + cell));
            }
        }
        else
        {
            font.DrawText(null, "No options here (visual demo)", (int)contentX + 8, (int)contentY + 36, Color.LightGray);
        }
    }

    private static bool TryGetLocalCursor(Graphics.Graphics graphics, out float localX, out float localY)
    {
        localX = 0; localY = 0;
        try
        {
            if (graphics?.GameProcess == null) return false;
            if (!Core.User32.GetCursorPos(out var pt)) return false;
            var rect = graphics.GameProcess.WindowRectangleClient;
            localX = pt.X - rect.X;
            localY = pt.Y - rect.Y;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void DrawSlider(Graphics.Graphics graphics, SharpDX.Vector2 pos, float width, float min, float max, ref float value, Action<float> onChange)
    {
        var bg = new Color(50, 50, 50, 200);
        var fg = new Color(100, 180, 255, 220);
        graphics.DrawFilledRectangle(bg, pos, new SharpDX.Vector2(pos.X + width, pos.Y + 12));
        var frac = (value - min) / (max - min);
        graphics.DrawFilledRectangle(fg, pos, new SharpDX.Vector2(pos.X + width * frac, pos.Y + 12));

        if (graphics.GameProcess != null && Core.User32.GetCursorPos(out var pt))
        {
            var rect = graphics.GameProcess.WindowRectangleClient;
            var localX = pt.X - rect.X;
            var localY = pt.Y - rect.Y;
            var mouseDown = Utility.IsKeyDown(Keys.LButton);
            var sliderRect = new System.Drawing.Rectangle((int)pos.X, (int)pos.Y, (int)width, 12);
            if (mouseDown && sliderRect.Contains((int)localX, (int)localY))
            {
                var rel = Math.Clamp((localX - pos.X) / width, 0f, 1f);
                var newVal = min + rel * (max - min);
                value = newVal;
                onChange?.Invoke(newVal);
            }
        }
    }

    private static void DrawButton(Graphics.Graphics graphics, SharpDX.Direct3D9.Font font, string text, float x, float y, bool active, Action onClick)
    {
        var bg = active ? new Color(30, 120, 40, 220) : new Color(50, 50, 50, 200);
        graphics.DrawFilledRectangle(bg, new SharpDX.Vector2(x, y), new SharpDX.Vector2(x + 28, y + 20));
        graphics.DrawRectangle(Color.Gray, new SharpDX.Vector2(x, y), new SharpDX.Vector2(x + 28, y + 20));
        font.DrawText(null, text, (int)x + 6, (int)y + 2, Color.White);

        if (graphics.GameProcess != null && Core.User32.GetCursorPos(out var pt))
        {
            var rect = graphics.GameProcess.WindowRectangleClient;
            var localX = pt.X - rect.X;
            var localY = pt.Y - rect.Y;
            var mouseDown = Utility.IsKeyDown(Keys.LButton);
            var clicked = mouseDown && !_prevMouseLeftDown;
            var btnRect = new System.Drawing.Rectangle((int)x, (int)y, 28, 20);
            if (clicked && btnRect.Contains((int)localX, (int)localY)) onClick?.Invoke();
        }
    }

    private static void DrawKeybindButton(Graphics.Graphics graphics, SharpDX.Direct3D9.Font font, float x, float y, int w, int h, string label, string id, Action onClick)
    {
        var bg = new Color(40, 40, 40, 220);
        graphics.DrawFilledRectangle(bg, new SharpDX.Vector2(x, y), new SharpDX.Vector2(x + w, y + h));
        graphics.DrawRectangle(Color.Gray, new SharpDX.Vector2(x, y), new SharpDX.Vector2(x + w, y + h));
        font.DrawText(null, label, (int)x + 6, (int)y + 2, Color.White);

        if (graphics.GameProcess != null && Core.User32.GetCursorPos(out var pt))
        {
            var rect = graphics.GameProcess.WindowRectangleClient;
            var localX = pt.X - rect.X;
            var localY = pt.Y - rect.Y;
            var mouseDown = Utility.IsKeyDown(Keys.LButton);
            var clicked = mouseDown && !_prevMouseLeftDown;
            var btnRect = new System.Drawing.Rectangle((int)x, (int)y, w, h);
            if (clicked && btnRect.Contains((int)localX, (int)localY)) onClick?.Invoke();
        }

        if (_capturingKey && _capturingFor == id)
        {
            font.DrawText(null, "Press a key...", (int)x + 6, (int)y + 2, Color.Yellow);
            foreach (Keys k in Enum.GetValues(typeof(Keys)))
            {
                if (Utility.IsKeyDown(k))
                {
                    var cfg = ConfigManager.Load();
                    if (id == "AimKey") cfg.AimBotKey = k;
                    if (id == "TriggerKey") cfg.TriggerBotKey = k;
                    ConfigManager.Save(cfg);
                    _capturingKey = false;
                    _capturingFor = "";
                    break;
                }
            }
        }
    }

    private static void StartKeyCapture(string id)
    {
        _capturingKey = true;
        _capturingFor = id;
    }

    private static void SaveConfig()
    {
        try
        {
            var cfg = ConfigManager.Load();
            cfg.EspBox = BoxEnabled;
            cfg.SkeletonEsp = SkeletonEnabled;
            cfg.BoxColorIndex = _boxColorIndex;
            cfg.SkeletonColorIndex = _skeletonColorIndex;
            cfg.MenuOpacity = MenuOpacity;
            cfg.BoxThickness = BoxThickness;
            ConfigManager.Save(cfg);
        }
        catch
        {
            // swallow save errors
        }
    }
}
