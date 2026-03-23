using CS2Cheat.Core.Data;
using CS2Cheat.Data.Entity;
using CS2Cheat.Utils;
using CS2Cheat.Graphics;
using SharpDX;
using Color = SharpDX.Color;

namespace CS2Cheat.Features;

public static class SkeletonEsp
{
    private static readonly (string Start, string End)[] BoneConnections =
    [
        // Spine chain
        ("head", "neck_0"),
        ("neck_0", "spine_1"),
        ("spine_1", "spine_2"),
        ("spine_2", "pelvis"),

        // Left arm chain
        ("spine_1", "arm_upper_L"),
        ("arm_upper_L", "arm_lower_L"),
        ("arm_lower_L", "hand_L"),

        // Right arm chain
        ("spine_1", "arm_upper_R"),
        ("arm_upper_R", "arm_lower_R"),
        ("arm_lower_R", "hand_R"),

        // Left leg chain
        ("pelvis", "leg_upper_L"),
        ("leg_upper_L", "leg_lower_L"),
        ("leg_lower_L", "ankle_L"),

        // Right leg chain
        ("pelvis", "leg_upper_R"),
        ("leg_upper_R", "leg_lower_R"),
        ("leg_lower_R", "ankle_R")
    ];

    public static void Draw(Graphics.Graphics graphics)
    {
        var player = graphics.GameData.Player;
        foreach (var entity in graphics.GameData.Entities)
        {
            if (!IsValidEntity(entity, player)) continue;

            var color = MenuManager.SkeletonColor;
            DrawSkeleton(graphics, entity, color);
        }
    }


    private static bool IsValidEntity(Entity entity, Player player)
    {
        return entity.IsAlive() &&
               entity.AddressBase != player.AddressBase;
    }

    private static Color GetTeamColor(Team team)
    {
        return team == Team.Terrorists ? Color.Yellow : Color.Blue;
    }

    private static void DrawSkeleton(Graphics.Graphics graphics, Entity entity, Color color)
    {
        var bonePositions = entity.BonePos;
        if (bonePositions == null) return;

        foreach (var (startBone, endBone) in BoneConnections)
        {
            if (!bonePositions.ContainsKey(startBone) || !bonePositions.ContainsKey(endBone))
                continue;

            graphics.DrawLineWorld(color, bonePositions[startBone], bonePositions[endBone]);
        }

        // Draw small cross / dot on head
        if (bonePositions.ContainsKey("head"))
        {
            var player = graphics.GameData.Player;
            if (player != null)
            {
                try
                {
                    var headWorld = bonePositions["head"];
                    var v = player.MatrixViewProjectionViewport.Transform(headWorld);
                    if (v.Z < 1)
                    {
                        var screen = new Vector2(v.X, v.Y);
                        const float half = 6f;

                        // cross
                        graphics.DrawLine(Color.White, new Vector2(screen.X - half, screen.Y), new Vector2(screen.X + half, screen.Y));
                        graphics.DrawLine(Color.White, new Vector2(screen.X, screen.Y - half), new Vector2(screen.X, screen.Y + half));

                        // small center dot
                        graphics.DrawFilledRectangle(Color.White, new Vector2(screen.X - 1, screen.Y - 1), new Vector2(screen.X + 1, screen.Y + 1));
                    }
                }
                catch
                {
                    // ignore projection errors
                }
            }
        }
    }
}