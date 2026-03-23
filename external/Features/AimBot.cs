using System.Runtime.InteropServices;
using CS2Cheat.Core;
using CS2Cheat.Core.Data;
using CS2Cheat.Data.Entity;
using CS2Cheat.Data.Game;
using CS2Cheat.Graphics;
using CS2Cheat.Utils;
using Process.NET.Native.Types;
using SharpDX;
using Keys = Process.NET.Native.Types.Keys;
using Point = System.Drawing.Point;

namespace CS2Cheat.Features;

public class AimBot : ThreadedServiceBase
{
    private const float AimBotSmoothing = 3f;
    private const double HumanReactThreshold = 30.0;
    private const int SuppressMs = 200;
    private const int UserMouseDeltaResetMs = 50;
    private const int AimUpdateIntervalMs = 500;
    private const int AimEventWindowMs = 1000;
    private static double _anglePerPixelX;
    private static double _anglePerPixelY;

    private static ConfigManager? _config;

    private static readonly string[] AimBonePriority = { "head", "neck", "chest", "pelvis" };

    private readonly object _stateLock = new();

    private readonly Keys AimBotHotKey;
    private double _aiAggressiveness = 2;

    private int _aimSuccessCount;
    private int _aimTotalCount;
    private double _dynamicFov = 15f.DegreeToRadian();
    private double _dynamicSmoothing = AimBotSmoothing;
    private DateTime _lastAimEvent = DateTime.MinValue;
    private DateTime _lastAiUpdate = DateTime.MinValue;
    private DateTime _lastMouseMoveTime = DateTime.MinValue;

    private DateTime _lastCalibration = DateTime.MinValue;

    private int _lastMouseX;
    private int _lastMouseY;

    private DateTime _lastSuppressed = DateTime.MinValue;
    private int _lastTargetId = -1;


    private Vector3 _lastTargetPos = Vector3.Zero;
    private DateTime _lastTargetUpdate = DateTime.MinValue;
    private Vector3 _lastTargetVel = Vector3.Zero;


    private int _userMouseDeltaX;
    private int _userMouseDeltaY;
    private double _userMoveAvg;
    private int _userMoveCount;


    private double _userMoveSum;

    public AimBot(GameProcess gameProcess, GameData gameData)
    {
        GameProcess = gameProcess;
        GameData = gameData;
        MouseHook = new GlobalHook(HookType.WH_MOUSE_LL, MouseHookCallback);
        AimBotHotKey = Config.AimBotKey;
    }

    private static ConfigManager Config => _config ??= ConfigManager.Load();

    private static MouseMoveMethod MouseMoveMethod =>
        MouseMoveMethod.TryMouseMoveNew;


    private bool IsCalibrated { get; set; }

    protected override string ThreadName => nameof(AimBot);

    private GameProcess? GameProcess { get; set; }
    private GameData? GameData { get; set; }
    private GlobalHook? MouseHook { get; set; }
    private AimBotState State { get; set; }
    private float CurrentSmoothing { get; set; } = AimBotSmoothing;

    public override void Dispose()
    {
        base.Dispose();

        if (MouseHook != null)
        {
            MouseHook.Dispose();
            MouseHook = null;
        }

        GameData = null;
        GameProcess = null;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (MouseMessages)wParam == MouseMessages.WmMouseMove)
        {
            var mouseInput = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var dx = mouseInput.Point.X - _lastMouseX;
            var dy = mouseInput.Point.Y - _lastMouseY;
            _userMouseDeltaX = dx;
            _userMouseDeltaY = dy;
            _lastMouseMoveTime = DateTime.Now;
            _lastMouseX = mouseInput.Point.X;
            _lastMouseY = mouseInput.Point.Y;
            var moveLen = Math.Sqrt(dx * dx + dy * dy);
            _userMoveSum += moveLen;
            _userMoveCount++;
        }

        return nCode < 0 || ProcessMouseMessage((MouseMessages)wParam)
            ? User32.CallNextHookEx(MouseHook != null ? MouseHook.HookHandle : IntPtr.Zero, nCode, wParam, lParam)
            : new IntPtr(1);
    }

    private bool ProcessMouseMessage(MouseMessages mouseMessage)
    {
        if (mouseMessage == MouseMessages.WmLButtonUp)
        {
            lock (_stateLock)
            {
                State = AimBotState.Up;
            }

            return true;
        }

        if (mouseMessage != MouseMessages.WmLButtonDown) return true;

        if (GameProcess == null || !GameProcess.IsValid ||
            GameData == null || GameData.Player == null || !GameData.Player.IsAlive() ||
            TriggerBot.IsHotKeyDown() ||
            GameData.Player.IsGrenade())
            return true;

        lock (_stateLock)
        {
            if (State == AimBotState.Up) State = AimBotState.DownSuppressed;
        }

        return true;
    }

    protected override void FrameAction()
    {
        try
        {
            if (GameProcess == null || !GameProcess.IsValid || GameData?.Player == null ||
                !GameData.Player.IsAlive()) return;


            var userMoveLen = Math.Sqrt(_userMouseDeltaX * _userMouseDeltaX + _userMouseDeltaY * _userMouseDeltaY);
            if (userMoveLen > HumanReactThreshold) _lastSuppressed = DateTime.Now;

            if ((DateTime.Now - _lastSuppressed).TotalMilliseconds < SuppressMs) return;

            if (!IsCalibrated)
            {
                Calibrate();
                IsCalibrated = true;
            }

            // periodic recalibration to reduce desync (handles FOV/aspect changes)
            if ((DateTime.Now - _lastCalibration).TotalMilliseconds > 3000)
            {
                Calibrate();
            }

            lock (_stateLock)
            {
                if (State == AimBotState.Up) return;
            }

            if ((DateTime.Now - _lastAiUpdate).TotalMilliseconds > AimUpdateIntervalMs && _userMoveCount > 0)
            {
                _userMoveAvg = _userMoveSum / _userMoveCount;
                _aiAggressiveness = 1.0 - Math.Min(_userMoveAvg / 20.0, 0.7);
                _userMoveSum = 0;
                _userMoveCount = 0;
                _lastAiUpdate = DateTime.Now;
            }

            if (_aimTotalCount > 0 && (DateTime.Now - _lastAimEvent).TotalMilliseconds > AimEventWindowMs)
            {
                var successRate = _aimSuccessCount / (double)_aimTotalCount;
                if (successRate < 0.5)
                {
                    _dynamicFov = Math.Max(5f.DegreeToRadian(), _dynamicFov - 0.5f.DegreeToRadian());
                    _dynamicSmoothing = Math.Min(_dynamicSmoothing + 0.5, 10.0);
                }
                else if (successRate > 0.8)
                {
                    _dynamicFov = Math.Min(30f.DegreeToRadian(), _dynamicFov + 0.5f.DegreeToRadian());
                    _dynamicSmoothing = Math.Max(_dynamicSmoothing - 0.5, 1.0);
                }

                _aimSuccessCount = 0;
                _aimTotalCount = 0;
                _lastAimEvent = DateTime.Now;
            }

            var aimPixels = Point.Empty;
            Vector2 aimAngles;
            var aimResult = GetAimTargetWithPrediction(out aimAngles, _dynamicFov);
            if (aimResult)
                if (!float.IsNaN(aimAngles.X) && !float.IsNaN(aimAngles.Y))
                    GetAimPixels(aimAngles, out aimPixels);

            aimPixels.X = Math.Max(Math.Min(aimPixels.X, 50), -50);
            aimPixels.Y = Math.Max(Math.Min(aimPixels.Y, 50), -50);

            var adapt = _aiAggressiveness;
            if ((DateTime.Now - _lastMouseMoveTime).TotalMilliseconds < UserMouseDeltaResetMs) adapt *= 0.5;
            aimPixels.X = (int)(aimPixels.X * adapt);
            aimPixels.Y = (int)(aimPixels.Y * adapt);

            var shouldWait = TryMouseDown();
            if (MouseMoveMethod == MouseMoveMethod.TryMouseMoveOld)
                shouldWait |= TryMouseMoveOld(aimPixels);
            else
                shouldWait |= TryMouseMoveNew(aimPixels);
            if (shouldWait) Thread.Sleep(20);

            if (aimResult) _aimSuccessCount++;

            _aimTotalCount++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AimBot ERROR] {ex.Message}\n{ex.StackTrace}");
        }
    }


    private bool GetAimTargetWithPrediction(out Vector2 aimAngles, double customFov)
    {
        var minAngleSize = float.MaxValue;
        aimAngles = new Vector2((float)Math.PI, (float)Math.PI);
        var targetFound = false;
        var aimPosition = Vector3.Zero;
        var targetVel = Vector3.Zero;

        if (GameData != null && GameData.Entities != null)
            foreach (var entity in GameData.Entities.Where(entity =>
                         GameData.Player != null &&
                         entity.IsAlive() && entity.AddressBase != GameData.Player.AddressBase &&
                         entity.Team != GameData.Player.Team && entity.IsSpotted))
            {
                Vector3? bestBonePos = null;
                var bestAngles = Vector2.Zero;
                var bestAngleSize = float.MaxValue;

                foreach (var bone in AimBonePriority)
                {
                    if (!entity.BonePos.TryGetValue(bone, out var bonePos)) continue;

                    var dt = (float)(DateTime.Now - _lastTargetUpdate).TotalSeconds;

                    if (entity.Id != _lastTargetId)
                    {
                        _lastTargetPos = bonePos;
                        _lastTargetVel = Vector3.Zero;
                    }
                    else if (dt > 0.001f && dt < 0.5f)
                    {
                        _lastTargetVel = (bonePos - _lastTargetPos) / dt;
                        _lastTargetPos = bonePos;
                    }

                    _lastTargetId = entity.Id;
                    _lastTargetUpdate = DateTime.Now;
                    targetVel = _lastTargetVel;

                    var distanceToTarget = Vector3.Distance(GameData.Player.EyePosition, bonePos);
                    var dynamicPredictionTime = 0.05f + Math.Min(distanceToTarget / 1000f, 1f) * 0.15f;
                    var predictedPos = bonePos + targetVel * dynamicPredictionTime;

                    GetAimAngles(predictedPos, out var angleToBoneSize, out var anglesToBone);
                    if (angleToBoneSize > customFov) continue;

                    if (angleToBoneSize < bestAngleSize)
                    {
                        bestAngleSize = angleToBoneSize;
                        bestAngles = anglesToBone;
                        bestBonePos = predictedPos;
                    }
                }

                if (bestBonePos != null && bestAngleSize < minAngleSize)
                {
                    minAngleSize = bestAngleSize;
                    aimAngles = bestAngles;
                    aimPosition = bestBonePos.Value;
                    targetFound = true;
                }
            }

        CurrentSmoothing = AimBotSmoothing;
        if (targetFound)
        {
            var distanceToTarget = Vector3.Distance(GameData.Player.EyePosition, aimPosition);
            var smoothingAcceleration = Math.Max(1.0f, distanceToTarget / 100.0f);
            CurrentSmoothing *= smoothingAcceleration;
            CurrentSmoothing = Math.Min(CurrentSmoothing, 50.0f);
            aimAngles *= 1 / Math.Max(CurrentSmoothing, 1);
        }

        return targetFound;
    }


    private void GetAimAngles(Vector3 pointWorld, out float angleSize, out Vector2 aimAngles)
    {
        aimAngles = Vector2.Zero;
        angleSize = 0f;

        if (GameData == null || GameData.Player == null) return;

        var aimDirection = GameData.Player.AimDirection;
        var aimDirectionDesired = (pointWorld - GameData.Player.EyePosition).GetNormalized();

        var horizontalAngle = aimDirectionDesired.GetSignedAngleTo(aimDirection, new Vector3(0, 0, 1));
        var verticalAngle = aimDirectionDesired.GetSignedAngleTo(aimDirection,
            Vector3.Cross(aimDirectionDesired, new Vector3(0, 0, 1)).GetNormalized());

        aimAngles = new Vector2(horizontalAngle, verticalAngle);


        angleSize = aimDirection.GetAngleTo(aimDirectionDesired);
    }


    private static void GetAimPixels(Vector2 aimAngles, out Point aimPixels)
    {
        // Use calibrated angle-per-pixel values for X and Y to map angles to screen pixels.
        var px = _anglePerPixelX > 0 ? aimAngles.X / _anglePerPixelX : 0.0;
        var py = _anglePerPixelY > 0 ? aimAngles.Y / _anglePerPixelY : 0.0;
        aimPixels = new Point((int)Math.Round(px), (int)Math.Round(py));
    }

    private static bool TryMouseMoveOld(Point aimPixels)
    {
        if (aimPixels.X == 0 && aimPixels.Y == 0) return false;
        if (Math.Abs(aimPixels.X) > 100 || Math.Abs(aimPixels.Y) > 100) return false;
        Utility.MouseMove(aimPixels.X, aimPixels.Y);
        return true;
    }

    private static bool TryMouseMoveNew(Point aimPixels)
    {
        if (aimPixels.X == 0 && aimPixels.Y == 0) return false;

        if (Math.Abs(aimPixels.X) > 100 || Math.Abs(aimPixels.Y) > 100) return false;
        Utility.WindMouseMove(0, 0, aimPixels.X, aimPixels.Y, 9.0, 3.0, 15.0, 12.0);
        return true;
    }


    private bool TryMouseDown()
    {
        var mouseDown = false;
        lock (_stateLock)
        {
            if (State == AimBotState.DownSuppressed)
            {
                mouseDown = true;
                State = AimBotState.Down;
            }
        }

        if (mouseDown) Utility.MouseLeftDown();
        return mouseDown;
    }

    private void Calibrate()
    {
        // calibrate horizontal (X) using several horizontal mouse moves
        var samplesX = new[] { 100, -200, 300, -400, 200 }
            .Select(d => CalibrationMeasureAnglePerPixel(d, 'x')).Where(v => v > 0).ToArray();
        if (samplesX.Length > 0) _anglePerPixelX = samplesX.Average();

        // calibrate vertical (Y) using several vertical mouse moves
        var samplesY = new[] { 100, -200, 300, -400, 200 }
            .Select(d => CalibrationMeasureAnglePerPixel(d, 'y')).Where(v => v > 0).ToArray();
        if (samplesY.Length > 0) _anglePerPixelY = samplesY.Average();

        _lastCalibration = DateTime.Now;
    }

    private double CalibrationMeasureAnglePerPixel(int deltaPixels, char axis)
    {
        Thread.Sleep(40);

        if (GameProcess == null || GameProcess.ModuleClient == null) return 0.0;

        // read view angles directly (degrees)
        var startAngles = GameProcess.ModuleClient.Read<Vector3>(Offsets.dwViewAngles);

        // move mouse along requested axis
        if (axis == 'x') Utility.MouseMove(deltaPixels, 0);
        else Utility.MouseMove(0, deltaPixels);

        Thread.Sleep(40);

        var endAngles = GameProcess.ModuleClient.Read<Vector3>(Offsets.dwViewAngles);

        double deltaDegrees;
        if (axis == 'x') // yaw
            deltaDegrees = endAngles.Y - startAngles.Y;
        else // pitch
            deltaDegrees = endAngles.X - startAngles.X;

        // normalize angle change to [-180,180]
        if (deltaDegrees > 180) deltaDegrees -= 360;
        if (deltaDegrees < -180) deltaDegrees += 360;

        return Math.Abs(deltaDegrees.DegreeToRadian() / deltaPixels);
    }
}