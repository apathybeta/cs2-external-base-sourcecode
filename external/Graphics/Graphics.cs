using System.Windows.Threading;
using System.Diagnostics;
using System.Threading;
using CS2Cheat.Core.Data;
using CS2Cheat.Data.Game;
using CS2Cheat.Features;
using CS2Cheat.Utils;
using SharpDX;
using SharpDX.Direct3D9;
using static System.Windows.Application;
using Color = SharpDX.Color;
using Font = SharpDX.Direct3D9.Font;
using FontWeight = SharpDX.Direct3D9.FontWeight;

namespace CS2Cheat.Graphics;

public class Graphics : ThreadedServiceBase
{
    private static readonly VertexElement[] VertexElements =
    {
        new(0, 0, DeclarationType.Float4, DeclarationMethod.Default, DeclarationUsage.PositionTransformed, 0),
        new(0, 16, DeclarationType.Color, DeclarationMethod.Default, DeclarationUsage.Color, 0),
        VertexElement.VertexDeclarationEnd
    };

    private readonly object _deviceLock = new();

    private readonly List<Vertex> _vertices = [];

    private Vector2 _currentResolution;
    private Device? _device;
    private bool _isDisposed;
    private readonly double _targetFrameMs;
    private readonly System.Diagnostics.Stopwatch _frameTimer;
    private double _lastFrameTimeMs;
    private bool _prioritySet;

    public Graphics(GameProcess gameProcess, GameData gameData, WindowOverlay windowOverlay)
    {
        WindowOverlay = windowOverlay ?? throw new ArgumentNullException(nameof(windowOverlay));
        GameProcess = gameProcess ?? throw new ArgumentNullException(nameof(gameProcess));
        GameData = gameData ?? throw new ArgumentNullException(nameof(gameData));

        _currentResolution = new Vector2(WindowOverlay.Window.Width, WindowOverlay.Window.Height);
        InitializeDevice();
        // target ~240Hz for smoother rendering on high-end systems
        ThreadFrameSleep = TimeSpan.Zero;
        _targetFrameMs = 1000.0 / 240.0; // target milliseconds per frame (~240Hz)
        _lastFrameTimeMs = 0.0;
        _frameTimer = System.Diagnostics.Stopwatch.StartNew();
    }

    protected override string ThreadName => nameof(Graphics);

    private WindowOverlay WindowOverlay { get; }
    public GameProcess GameProcess { get; }
    public GameData GameData { get; }
    public Font? FontAzonix64 { get; private set; }
    public Font? FontConsolas32 { get; private set; }
    public Font? Undefeated { get; private set; }

    public override void Dispose()
    {
        if (_isDisposed) return;

        base.Dispose();

        lock (_deviceLock)
        {
            DisposeResources();
            _isDisposed = true;
        }
    }

    private void InitializeDevice()
    {
        var parameters = CreatePresentParameters();
        _device = new Device(new Direct3D(), 0, DeviceType.Hardware, WindowOverlay.Window.Handle,
            CreateFlags.HardwareVertexProcessing, parameters);

        InitializeFonts();
    }

    private PresentParameters CreatePresentParameters()
    {
        return new PresentParameters
        {
            Windowed = true,
            SwapEffect = SwapEffect.Discard,
            DeviceWindowHandle = WindowOverlay.Window.Handle,
            MultiSampleQuality = 0,
            BackBufferFormat = Format.A8R8G8B8,
            BackBufferWidth = WindowOverlay.Window.Width,
            BackBufferHeight = WindowOverlay.Window.Height,
            EnableAutoDepthStencil = true,
            AutoDepthStencilFormat = Format.D16,
            PresentationInterval = PresentInterval.Immediate,
            MultiSampleType = MultisampleType.TwoSamples
        };
    }

    private void InitializeFonts()
    {
        FontAzonix64 = new Font(_device, CreateFontDescription("Tahoma", 32));
        FontConsolas32 = new Font(_device, CreateFontDescription("Verdana", 12));
        Undefeated = new Font(_device, CreateFontDescription("undefeated", 12, FontCharacterSet.Default));
    }

    private static FontDescription CreateFontDescription(string faceName, int height,
        FontCharacterSet characterSet = FontCharacterSet.Ansi)
    {
        return new FontDescription
        {
            Height = height,
            Italic = false,
            CharacterSet = characterSet,
            FaceName = faceName,
            MipLevels = 0,
            OutputPrecision = FontPrecision.TrueType,
            PitchAndFamily = FontPitchAndFamily.Default,
            Quality = FontQuality.ClearType,
            Weight = FontWeight.Regular
        };
    }

    protected override void FrameAction()
    {
        if (!GameProcess.IsValid) return;

        // set thread priority once to give rendering thread more CPU time
        if (!_prioritySet)
        {
            Thread.CurrentThread.Priority = ThreadPriority.Highest;
            _prioritySet = true;
        }

        // frame pacing: render only when target frame interval elapsed
        var nowMs = _frameTimer.Elapsed.TotalMilliseconds;
        var elapsedSinceLast = nowMs - _lastFrameTimeMs;
        if (elapsedSinceLast < _targetFrameMs) return; // skip this cycle
        _lastFrameTimeMs = nowMs;

        var newResolution = new Vector2(WindowOverlay.Window.Width, WindowOverlay.Window.Height);
        if (!_currentResolution.Equals(newResolution))
        {
            // Recreate device directly on graphics thread to avoid UI dispatcher bottleneck
            RecreateDevice();
            _currentResolution = newResolution;
        }

        // Render directly on the graphics thread for maximum throughput
        RenderFrame();
    }

    private void RecreateDevice()
    {
        lock (_deviceLock)
        {
            DisposeResources();
            _vertices.Clear();
            InitializeDevice();
        }
    }

    private void RenderFrame()
    {
        lock (_deviceLock)
        {
            if (_device == null) return;

            ConfigureRenderState();
            _device.Clear(ClearFlags.Target | ClearFlags.ZBuffer, Color.FromAbgr(0), 1, 0);
            _device.BeginScene();

            // update fps counter at render time
            WindowOverlay.FpsCounter?.Update();
            RenderScene();

            _device.EndScene();
            _device.Present();
        }
    }

    private void ConfigureRenderState()
    {
        if (_device == null) return;

        _device.SetRenderState(RenderState.AlphaBlendEnable, true);
        _device.SetRenderState(RenderState.AlphaTestEnable, false);
        _device.SetRenderState(RenderState.SourceBlend, Blend.SourceAlpha);
        _device.SetRenderState(RenderState.DestinationBlend, Blend.InverseSourceAlpha);
        _device.SetRenderState(RenderState.Lighting, false);
        _device.SetRenderState(RenderState.CullMode, Cull.None);
        _device.SetRenderState(RenderState.ZEnable, true);
        _device.SetRenderState(RenderState.ZFunc, Compare.Always);
    }

    private void RenderScene()
    {
        _vertices.Clear();
        DrawFeatures();
        RenderVertices();
    }

    private void DrawFeatures()
    {
        WindowOverlay.Draw(GameProcess, this);
        // update menu input
        MenuManager.UpdateInput();

        var features = ConfigManager.Load();
        if (features.EspAimCrosshair) EspAimCrosshair.Draw(this);
        if (MenuManager.BoxEnabled) EspBox.Draw(this);
        if (MenuManager.SkeletonEnabled) SkeletonEsp.Draw(this);
        if (features.BombTimer) BombTimer.Draw(this);
        if (features.TriggerTrainer) TriggerTrainer.Draw(this);

        // draw menu overlay if visible
        MenuManager.Draw(this);
    }

    private void RenderVertices()
    {
        if (_vertices.Count == 0) return;

        if (_device == null) return;

        using var vertices = new VertexBuffer(_device, _vertices.Count * 20, Usage.WriteOnly, VertexFormat.None,
            Pool.Managed);
        vertices.Lock(0, 0, LockFlags.None).WriteRange(_vertices.ToArray());
        vertices.Unlock();

        _device.SetStreamSource(0, vertices, 0, 20);
        using var vertexDecl = new VertexDeclaration(_device, VertexElements);
        _device.VertexDeclaration = vertexDecl;
        _device.DrawPrimitives(PrimitiveType.LineList, 0, _vertices.Count / 2);
    }

    private void DisposeResources()
    {
        FontAzonix64?.Dispose();
        FontConsolas32?.Dispose();
        Undefeated?.Dispose();
        _device?.Dispose();
    }

    public void DrawLine(Color color, params Vector2[] verts)
    {
        if (verts.Length < 2 || verts.Length % 2 != 0) return;

        foreach (var vertex in verts)
            _vertices.Add(new Vertex
            {
                Color = color,
                Position = new Vector4(vertex.X, vertex.Y, 0.5f, 1.0f)
            });
    }

    public void DrawLineWorld(Color color, params Vector3[] verticesWorld)
    {
        if (GameData.Player == null) return;

        var screenVertices = verticesWorld
            .Select(v => GameData.Player.MatrixViewProjectionViewport.Transform(v))
            .Where(v => v.Z < 1)
            .Select(v => new Vector2(v.X, v.Y))
            .ToArray();

        DrawLine(color, screenVertices);
    }

    public void DrawRectangle(Color color, Vector2 topLeft, Vector2 bottomRight)
    {
        var vertices = new[]
        {
            topLeft,
            new Vector2(bottomRight.X, topLeft.Y),
            bottomRight,
            new Vector2(topLeft.X, bottomRight.Y),
            topLeft
        };

        for (var i = 0; i < vertices.Length - 1; i++) DrawLine(color, vertices[i], vertices[i + 1]);
    }

    // Draw a simple filled rectangle by creating horizontal lines.
    public void DrawFilledRectangle(Color color, Vector2 topLeft, Vector2 bottomRight)
    {
        var height = (int)Math.Max(0, bottomRight.Y - topLeft.Y);
        if (height <= 0) return;

        // Draw as series of horizontal 1px lines to represent filled area
        for (var y = 0; y < height; y++)
        {
            var yPos = topLeft.Y + y;
            DrawLine(color, new Vector2(topLeft.X, yPos), new Vector2(bottomRight.X, yPos));
        }
    }
}