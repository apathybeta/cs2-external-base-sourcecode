using CS2Cheat.Core.Data;
using CS2Cheat.Data.Entity;
using CS2Cheat.Graphics;
using CS2Cheat.Utils;
using SharpDX;
using SharpDX.Direct3D9;
using Color = SharpDX.Color;

namespace CS2Cheat.Features;

public static class EspBox
{
    // Reduce outline thickness to make health bar smaller
    private const int OutlineThickness = 1;

    private static readonly Dictionary<string, string> GunIcons = new()
    {
        ["knife_ct"] = "]", ["knife_t"] = "[", ["deagle"] = "A", ["elite"] = "B",
        ["fiveseven"] = "C", ["glock"] = "D", ["revolver"] = "J", ["hkp2000"] = "E",
        ["p250"] = "F", ["usp_silencer"] = "G", ["tec9"] = "H", ["cz75a"] = "I",
        ["mac10"] = "K", ["ump45"] = "L", ["bizon"] = "M", ["mp7"] = "N",
        ["mp9"] = "R", ["p90"] = "O", ["galilar"] = "Q", ["famas"] = "R",
        ["m4a1_silencer"] = "T", ["m4a1"] = "S", ["aug"] = "U", ["sg556"] = "V",
        ["ak47"] = "W", ["g3sg1"] = "X", ["scar20"] = "Y", ["awp"] = "Z",
        ["ssg08"] = "a", ["xm1014"] = "b", ["sawedoff"] = "c", ["mag7"] = "d",
        ["nova"] = "e", ["negev"] = "f", ["m249"] = "g", ["taser"] = "h",
        ["flashbang"] = "i", ["hegrenade"] = "j", ["smokegrenade"] = "k",
        ["molotov"] = "l", ["decoy"] = "m", ["incgrenade"] = "n", ["c4"] = "o"
    };

    private static ConfigManager? _config;
    private static ConfigManager Config => _config ??= ConfigManager.Load();

    public static void Draw(Graphics.Graphics graphics)
    {
        var player = graphics.GameData.Player;
        if (player == null || graphics.GameData.Entities == null) return;

        // World-coordinate bomb drawing removed. Bomb icon is shown inside the carrier's box only.

        // Note: previously a global bomb world marker was drawn here using BombTimer.TryGetBombScreenInfo.
        // That marker was sometimes placed incorrectly. Prefer drawing the bomb icon directly on the
        // carrier's box (below) so the indicator is always clearly attached to the player who holds it.

        foreach (var entity in graphics.GameData.Entities)
        {
            if (!entity.IsAlive() || entity.AddressBase == player.AddressBase) continue;
            if (Config.TeamCheck && entity.Team == player.Team) continue;


            var boundingBox = GetEntityBoundingBox(graphics, entity);
            if (boundingBox == null) continue;

            var colorBox = MenuManager.BoxColor;
            DrawEntityInfo(graphics, entity, colorBox, boundingBox.Value);
        }
    }

    private static void DrawEntityInfo(Graphics.Graphics graphics, Entity entity, Color color,
        (Vector2, Vector2) boundingBox)
    {
        var (topLeft, bottomRight) = boundingBox;
        if (topLeft.X > bottomRight.X || topLeft.Y > bottomRight.Y) return;

        var healthPercentage = Math.Clamp(entity.Health / 100f, 0f, 1f);

        graphics.DrawRectangle(color, topLeft, bottomRight);

        // Simple health bar: small filled rectangle that changes color from red (0) to green (100)
        var healthBarLeft = topLeft.X - 6f;
        var healthBarTopLeft = new Vector2(healthBarLeft, topLeft.Y);
        var healthBarBottomRight = new Vector2(healthBarLeft + 4f, bottomRight.Y);
        DrawSimpleHealthBar(graphics, healthBarTopLeft, healthBarBottomRight, healthPercentage);

        // Health number
        var healthText = entity.Health.ToString();
        var healthX = (int)(bottomRight.X + 2);
        var healthY = (int)(topLeft.Y + (bottomRight.Y - topLeft.Y -
                                         graphics.FontConsolas32.MeasureText(null, healthText, FontDrawFlags.Center)
                                             .Bottom) / 2);
        graphics.FontConsolas32.DrawText(default, healthText, healthX, healthY, Color.White);

        // Weapon
        var weaponIcon = GetWeaponIcon(entity.CurrentWeaponName);
        if (!string.IsNullOrEmpty(weaponIcon))
        {
            var textSize = graphics.Undefeated.MeasureText(null, weaponIcon, FontDrawFlags.Center);
            var weaponX = (int)((topLeft.X + bottomRight.X - textSize.Right) / 2);
            var weaponY = (int)(bottomRight.Y + 2.5f);
            graphics.Undefeated.DrawText(null, weaponIcon, weaponX, weaponY, Color.White);

            // draw small weapon icon/glyph under the box
            var underY = bottomRight.Y + 6f;
            var underSize = 12f;
            var underX = (topLeft.X + bottomRight.X) / 2f - underSize / 2f;
            graphics.DrawFilledRectangle(Color.Black, new SharpDX.Vector2(underX, underY), new SharpDX.Vector2(underX + underSize, underY + underSize));
            if (graphics.Undefeated != null)
            {
                graphics.Undefeated.DrawText(null, weaponIcon, (int)underX + 2, (int)underY + 1, Color.White);
            }
        }

        // Bomb carrier icon: show a bomb icon next to the top-right of the box
        try
        {
            var currentWeaponLower = (entity.CurrentWeaponName ?? string.Empty).ToLower();
            if (currentWeaponLower == "c4")
            {
                // Draw bomb indicator inside carrier's box (top-center) so it's visible from round start
                var iconSize = 16f;
                var cx = (topLeft.X + bottomRight.X) / 2f;
                var iconTop = topLeft.Y + 2f; // inside the box, near top edge
                var iconLeft = cx - (iconSize / 2f);

                var bombBg = Color.Gold;
                var bombBorder = Color.DarkGoldenrod;

                // background square (inside box)
                graphics.DrawFilledRectangle(bombBg, new SharpDX.Vector2(iconLeft, iconTop),
                    new SharpDX.Vector2(iconLeft + iconSize, iconTop + iconSize));
                // border
                graphics.DrawRectangle(bombBorder, new SharpDX.Vector2(iconLeft, iconTop),
                    new SharpDX.Vector2(iconLeft + iconSize, iconTop + iconSize));

                // glyph inside using Undefeated font if available
                var bombIcon = GetWeaponIcon("c4");
                if (!string.IsNullOrEmpty(bombIcon) && graphics.Undefeated != null)
                {
                    var glyphX = (int)(cx - 6);
                    var glyphY = (int)(iconTop + (iconSize / 2f) - 6);
                    graphics.Undefeated.DrawText(null, bombIcon, glyphX, glyphY, Color.Black);
                }
            }
        }
        catch
        {
            // ignore any drawing errors
        }

        // Enemy name
        if (graphics.GameData.Player.Team != entity.Team)
        {
            var name = entity.Name ?? "UNKNOWN";
            var textWidth = graphics.FontConsolas32.MeasureText(null, name, FontDrawFlags.Center).Right + 10f;
            var nameX = (int)((topLeft.X + bottomRight.X) / 2 - textWidth / 2);
            var nameY = (int)(topLeft.Y - 15f);
            graphics.FontConsolas32.DrawText(default, name, nameX, nameY, Color.White);
        }

        // Status flags
        var flagX = (int)(bottomRight.X + 5f);
        var flagY = (int)topLeft.Y;
        var spacing = 15;

        if (entity.IsInScope == 1)
            graphics.FontConsolas32.DrawText(default, "Scoped", flagX, flagY, Color.White);

        if (entity.FlashAlpha > 0)
        {
            // FlashAlpha is read as int; assume range 0..255 -> convert to percent
            var flashNormalized = Math.Clamp(entity.FlashAlpha / 255f, 0f, 1f);
            var flashPercent = flashNormalized * 100f;

            var flashText = $"{flashPercent:0}%";
            // draw small white text to the right of the box (synchronized with flash power)
            var smallFont = graphics.FontConsolas32;
            if (smallFont != null)
            {
                var fx = (int)(bottomRight.X + 6);
                var fy = (int)(topLeft.Y + 2);
                smallFont.DrawText(default, flashText, fx, fy, Color.White);
            }
        }

        if (entity.IsInScope == 256)
            graphics.FontConsolas32.DrawText(default, "Shifting", flagX, flagY + spacing * 2, Color.White);
        else if (entity.IsInScope == 257)
            graphics.FontConsolas32.DrawText(default, "Shifting in scope", flagX, flagY + spacing * 3, Color.White);
    }

    private static void DrawSimpleHealthBar(Graphics.Graphics graphics, Vector2 topLeft, Vector2 bottomRight,
        float healthPercentage)
    {
        var filledHeight = (bottomRight.Y - topLeft.Y) * healthPercentage;
        var filledTop = new Vector2(topLeft.X, Math.Max(bottomRight.Y - filledHeight, topLeft.Y));

        // interpolate color: 0 -> red, 1 -> green
        var color = Color.Lerp(Color.Red, Color.Green, healthPercentage);

        // draw filled rectangle for health portion
        graphics.DrawFilledRectangle(color, filledTop, bottomRight);
    }

    private static string GetWeaponIcon(string weapon)
    {
        return GunIcons.TryGetValue(weapon?.ToLower() ?? string.Empty, out var icon) ? icon : string.Empty;
    }

    private static (Vector2, Vector2)? GetEntityBoundingBox(Graphics.Graphics graphics, Entity entity)
    {
        const float padding = 5.0f;
        var minPos = new Vector2(float.MaxValue, float.MaxValue);
        var maxPos = new Vector2(float.MinValue, float.MinValue);

        var matrix = graphics.GameData.Player?.MatrixViewProjectionViewport;
        if (matrix == null || entity.BonePos == null || entity.BonePos.Count == 0) return null;

        var anyValid = false;
        foreach (var bone in entity.BonePos.Values)
        {
            var transformed = matrix.Value.Transform(bone);
            if (transformed.Z >= 1 || transformed.X < 0 || transformed.Y < 0) continue;

            anyValid = true;
            minPos.X = Math.Min(minPos.X, transformed.X);
            minPos.Y = Math.Min(minPos.Y, transformed.Y);
            maxPos.X = Math.Max(maxPos.X, transformed.X);
            maxPos.Y = Math.Max(maxPos.Y, transformed.Y);
        }


        if (!anyValid) return null;

        var sizeMultiplier = 2f - entity.Health / 100f;
        var paddingVector = new Vector2(padding * sizeMultiplier);
        return (minPos - paddingVector, maxPos + paddingVector);
    }
}