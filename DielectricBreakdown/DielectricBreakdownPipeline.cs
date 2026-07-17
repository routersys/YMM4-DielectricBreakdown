using System.Runtime.InteropServices;
using ComputeSharp;

namespace DielectricBreakdown;

internal sealed class DielectricBreakdownPipeline : IDisposable
{
    private readonly GraphicsDevice _device;
    private ReadWriteBuffer<int>? _mask;
    private ReadWriteBuffer<int>? _state;
    private ReadWriteBuffer<int>? _birth;
    private ReadWriteBuffer<int>? _parent;
    private ReadWriteBuffer<int>? _mainFlag;
    private ReadWriteBuffer<int>? _charges;
    private ReadWriteBuffer<int>? _rowCounts;
    private ReadWriteBuffer<int>? _rowOffsets;
    private ReadWriteBuffer<float>? _potential;
    private ReadWriteBuffer<float>? _intensity;
    private readonly ReadWriteBuffer<int> _scratch;
    private int _gridWidth;
    private int _gridHeight;
    private ReadWriteBuffer<int>? _jumpFloodA;
    private ReadWriteBuffer<int>? _jumpFloodB;
    private int _jumpFloodCapacity;
    private ReadWriteBuffer<int>? _glowAccum;
    private ReadWriteBuffer<float>? _glowTemp;
    private ReadWriteBuffer<float>? _glowMap;
    private int _glowCapacity;
    private ReadWriteTexture2D<Bgra32, Float4>? _packedSource;
    private ReadWriteTexture2D<Bgra32, Float4>? _packedOutput;
    private int _packedWidth;
    private int _packedHeight;

    private DielectricBreakdownPipeline(GraphicsDevice device)
    {
        _device = device;
        _scratch = device.AllocateReadWriteBuffer<int>(DielectricBreakdownSettings.ScratchLength);
    }

    public static DielectricBreakdownPipeline? TryCreate()
    {
        try
        {
            return new DielectricBreakdownPipeline(GraphicsDevice.GetDefault());
        }
        catch
        {
            return null;
        }
    }

    public static DielectricBreakdownPipeline? TryCreate(GraphicsDevice device)
    {
        try
        {
            return new DielectricBreakdownPipeline(device);
        }
        catch
        {
            return null;
        }
    }

    internal void WaitForCompletion()
    {
        _device.For(1, new FillIntShader(_scratch, 0, 0));
    }

    public void Process(ReadOnlySpan<int> source, Span<int> destination, int width, int height, in Parameters parameters)
    {
        var pixelCount = checked(width * height);
        EnsureResources(width, height, parameters.Quality);
        EnsurePackedTextures(width, height);
        var sourceTexture = _packedSource!;
        var outputTexture = _packedOutput!;
        sourceTexture.CopyFrom(MemoryMarshal.Cast<int, Bgra32>(source[..pixelCount]));
        using (ComputeContext context = _device.CreateComputeContext())
            RecordPipeline(in context, sourceTexture, outputTexture, width, height, in parameters);
        outputTexture.CopyTo(MemoryMarshal.Cast<int, Bgra32>(destination[..pixelCount]));
    }

    public void Process(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> destination,
        int width,
        int height,
        in Parameters parameters)
    {
        EnsureResources(width, height, parameters.Quality);
        using ComputeContext context = _device.CreateComputeContext();
        RecordPipeline(in context, source, destination, width, height, in parameters);
        context.Submit();
    }

    internal void ProcessSharedAndWait(
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> destination,
        int width,
        int height,
        in Parameters parameters)
    {
        EnsureResources(width, height, parameters.Quality);
        using ComputeContext context = _device.CreateComputeContext();
        RecordPipeline(in context, source, destination, width, height, in parameters);
    }

    private void RecordPipeline(
        in ComputeContext context,
        ReadWriteTexture2D<Bgra32, Float4> source,
        ReadWriteTexture2D<Bgra32, Float4> output,
        int width,
        int height,
        in Parameters parameters)
    {
        var settings = DielectricBreakdownSettings.GetQuality(parameters.Quality);
        var gridWidth = _gridWidth;
        var gridHeight = _gridHeight;
        var gridLength = gridWidth * gridHeight;
        var pixelCount = width * height;
        var (_, _, cellSize) = DielectricBreakdownSettings.GetGridSize(width, height, settings.GridResolution);
        var reachCellCount = Math.Max(parameters.ReachPixels, 1f) / cellSize;
        var maxSteps = DielectricBreakdownSettings.GetStepCount(reachCellCount, settings.MaxSteps);
        var sentinel = maxSteps + 1;
        var maxWalk = maxSteps + 4;

        var radians = parameters.AngleDegrees * (MathF.PI / 180f);
        var directionX = MathF.Sin(radians);
        var directionY = MathF.Cos(radians);
        var projectionMin = MathF.Min(0f, gridWidth * directionX) + MathF.Min(0f, gridHeight * directionY);
        var projectionMax = MathF.Max(0f, gridWidth * directionX) + MathF.Max(0f, gridHeight * directionY);
        var projectionInverseRange = 1f / MathF.Max(projectionMax - projectionMin, 1e-4f);
        var eta = DielectricBreakdownSettings.GetEta(parameters.Branching);
        var reachQuantized = (int)(reachCellCount * DielectricBreakdownSettings.ProjectionQuantScale);
        var thickness = Math.Max(parameters.Thickness, 0.05f);
        var glowWidth = (width + 3) / 4;
        var glowHeight = (height + 3) / 4;
        var glowRadius = Math.Clamp((int)((2f + parameters.Glow * 58f) * 0.25f), 1, 16);
        var glowStrength = parameters.Glow * 0.27f * (glowRadius + 1);
        var flashTau = MathF.Max(DielectricBreakdownSettings.FlashTauFraction * maxSteps, 1f);
        var tipTau = MathF.Max(0.03f * maxSteps, 1f);

        var mask = _mask!;
        var state = _state!;
        var birth = _birth!;
        var parent = _parent!;
        var mainFlag = _mainFlag!;
        var charges = _charges!;
        var rowCounts = _rowCounts!;
        var rowOffsets = _rowOffsets!;
        var potential = _potential!;
        var intensity = _intensity!;
        var jumpFloodA = _jumpFloodA!;
        var jumpFloodB = _jumpFloodB!;
        var glowAccum = _glowAccum!;
        var glowTemp = _glowTemp!;
        var glowMap = _glowMap!;

        context.For(1, new InitScratchShader(_scratch, sentinel));
        context.For(gridLength, new FillIntShader(mainFlag, gridLength, 0));
        context.For(gridWidth, gridHeight, new SilhouetteShader(source, mask, width, height, gridWidth, gridHeight, cellSize, 0.05f));
        context.Barrier(mask);
        context.Barrier(_scratch);
        context.For(gridWidth, gridHeight, new ElectrodeInitShader(mask, state, birth, parent, _scratch, gridWidth, gridHeight, directionX, directionY, DielectricBreakdownSettings.ProjectionQuantScale));
        context.Barrier(state);
        context.For(gridHeight, new ChargeRowCountShader(state, rowCounts, gridWidth, gridHeight));
        context.Barrier(rowCounts);
        context.For(1, new ChargeRowOffsetShader(rowCounts, rowOffsets, _scratch, gridHeight));
        context.Barrier(rowOffsets);
        context.Barrier(_scratch);
        context.For(gridHeight, new ChargeFillShader(state, rowOffsets, charges, gridWidth, gridHeight));
        context.Barrier(charges);
        context.For(gridWidth, gridHeight, new InitialPotentialShader(charges, _scratch, potential, gridWidth, gridHeight, DielectricBreakdownSettings.ChargeRadius));
        context.Barrier(potential);

        for (var step = 0; step < maxSteps; step++)
        {
            context.For(gridWidth, gridHeight, new PotentialRangeShader(
                state, potential, _scratch, gridWidth, gridHeight, step, sentinel,
                DielectricBreakdownSettings.FieldBias, directionX, directionY, projectionMin, projectionInverseRange));
            context.Barrier(_scratch);
            context.For(gridWidth, gridHeight, new ScoreShader(
                state, potential, _scratch, gridWidth, gridHeight, step, sentinel, parameters.Seed, eta,
                DielectricBreakdownSettings.FieldBias, directionX, directionY, projectionMin, projectionInverseRange));
            context.Barrier(_scratch);
            context.For(gridWidth, gridHeight, new ResolveShader(
                state, potential, _scratch, gridWidth, gridHeight, step, sentinel, parameters.Seed, eta,
                DielectricBreakdownSettings.FieldBias, directionX, directionY, projectionMin, projectionInverseRange));
            context.Barrier(_scratch);
            context.For(gridWidth, gridHeight, new CommitShader(
                state, birth, parent, potential, _scratch, gridWidth, gridHeight, step, sentinel, reachQuantized,
                DielectricBreakdownSettings.ChargeRadius, directionX, directionY, DielectricBreakdownSettings.ProjectionQuantScale));
            context.Barrier(state);
            context.Barrier(potential);
            context.Barrier(_scratch);
        }

        context.For(1, new MainChannelShader(parent, birth, mainFlag, _scratch, sentinel, maxWalk));
        context.Barrier(mainFlag);
        context.For(gridWidth, gridHeight, new IntensityShader(
            state, birth, parent, mainFlag, intensity, gridWidth, gridHeight, maxWalk,
            DielectricBreakdownSettings.LeaderIntensity, DielectricBreakdownSettings.SideIntensity, DielectricBreakdownSettings.SideDecayPerHop));
        context.Barrier(intensity);

        var glowLength = glowWidth * glowHeight;
        context.For(glowLength, new FillIntShader(glowAccum, glowLength, 0));
        context.Barrier(glowAccum);
        context.For(gridWidth, gridHeight, new GlowDepositShader(
            state, birth, parent, mainFlag, intensity, _scratch, glowAccum,
            gridWidth, gridHeight, glowWidth, glowHeight, sentinel, cellSize, parameters.Growth,
            DielectricBreakdownSettings.TipBoost, 1f / tipTau,
            DielectricBreakdownSettings.FlashAmplitude, 1f / flashTau));
        context.Barrier(glowAccum);
        context.For(glowWidth, glowHeight, new GlowBlurHorizontalShader(glowAccum, glowTemp, glowWidth, glowHeight, glowRadius));
        context.Barrier(glowTemp);
        context.For(glowWidth, glowHeight, new GlowBlurVerticalShader(glowTemp, glowMap, glowWidth, glowHeight, glowRadius));
        context.Barrier(glowMap);

        context.For(pixelCount, new FillIntShader(jumpFloodA, pixelCount, -1));
        context.Barrier(jumpFloodA);
        context.For(gridWidth, gridHeight, new JumpFloodSeedShader(
            state, birth, _scratch, jumpFloodA, gridWidth, gridHeight, width, height, sentinel, cellSize, parameters.Growth));
        context.Barrier(jumpFloodA);

        var reading = jumpFloodA;
        var writing = jumpFloodB;
        var stepSize = 1;
        var maxSide = Math.Max(width, height);
        while (stepSize < maxSide)
            stepSize <<= 1;
        stepSize >>= 1;
        while (stepSize >= 1)
        {
            context.For(width, height, new JumpFloodPassShader(reading, writing, width, height, gridWidth, stepSize, cellSize));
            context.Barrier(writing);
            (reading, writing) = (writing, reading);
            stepSize >>= 1;
        }

        context.For(width, height, new RenderShader(
            reading, state, birth, parent, mainFlag, intensity, _scratch, glowMap, output,
            width, height, gridWidth, gridHeight, glowWidth, glowHeight, sentinel, cellSize, parameters.Growth,
            thickness, glowStrength,
            DielectricBreakdownSettings.TipBoost, 1f / tipTau,
            DielectricBreakdownSettings.FlashAmplitude, 1f / flashTau,
            parameters.ColorR, parameters.ColorG, parameters.ColorB));
    }

    private void EnsureResources(int width, int height, DielectricBreakdownQuality quality)
    {
        var settings = DielectricBreakdownSettings.GetQuality(quality);
        var (gridWidth, gridHeight, _) = DielectricBreakdownSettings.GetGridSize(width, height, settings.GridResolution);
        EnsureGrid(gridWidth, gridHeight);
        EnsureJumpFlood(checked(width * height));
    }

    private void EnsureGrid(int gridWidth, int gridHeight)
    {
        if (_gridWidth == gridWidth && _gridHeight == gridHeight)
            return;

        DisposeGridBuffers();
        var gridLength = gridWidth * gridHeight;
        _mask = _device.AllocateReadWriteBuffer<int>(gridLength);
        _state = _device.AllocateReadWriteBuffer<int>(gridLength);
        _birth = _device.AllocateReadWriteBuffer<int>(gridLength);
        _parent = _device.AllocateReadWriteBuffer<int>(gridLength);
        _mainFlag = _device.AllocateReadWriteBuffer<int>(gridLength);
        _charges = _device.AllocateReadWriteBuffer<int>(gridLength);
        _rowCounts = _device.AllocateReadWriteBuffer<int>(gridHeight);
        _rowOffsets = _device.AllocateReadWriteBuffer<int>(gridHeight);
        _potential = _device.AllocateReadWriteBuffer<float>(gridLength);
        _intensity = _device.AllocateReadWriteBuffer<float>(gridLength);
        _gridWidth = gridWidth;
        _gridHeight = gridHeight;
    }

    private void EnsureJumpFlood(int pixelCount)
    {
        if (_jumpFloodCapacity < pixelCount)
        {
            _jumpFloodA?.Dispose();
            _jumpFloodB?.Dispose();
            _jumpFloodA = _device.AllocateReadWriteBuffer<int>(pixelCount);
            _jumpFloodB = _device.AllocateReadWriteBuffer<int>(pixelCount);
            _jumpFloodCapacity = pixelCount;
        }

        var glowCapacity = (pixelCount + 15) / 16 + 64;
        if (_glowCapacity < glowCapacity)
        {
            _glowAccum?.Dispose();
            _glowTemp?.Dispose();
            _glowMap?.Dispose();
            _glowAccum = _device.AllocateReadWriteBuffer<int>(glowCapacity);
            _glowTemp = _device.AllocateReadWriteBuffer<float>(glowCapacity);
            _glowMap = _device.AllocateReadWriteBuffer<float>(glowCapacity);
            _glowCapacity = glowCapacity;
        }
    }

    private void EnsurePackedTextures(int width, int height)
    {
        if (_packedWidth == width && _packedHeight == height)
            return;

        _packedSource?.Dispose();
        _packedOutput?.Dispose();
        _packedSource = _device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        _packedOutput = _device.AllocateReadWriteTexture2D<Bgra32, Float4>(width, height);
        _packedWidth = width;
        _packedHeight = height;
    }

    private void DisposeGridBuffers()
    {
        _mask?.Dispose();
        _state?.Dispose();
        _birth?.Dispose();
        _parent?.Dispose();
        _mainFlag?.Dispose();
        _charges?.Dispose();
        _rowCounts?.Dispose();
        _rowOffsets?.Dispose();
        _potential?.Dispose();
        _intensity?.Dispose();
        _mask = null;
        _state = null;
        _birth = null;
        _parent = null;
        _mainFlag = null;
        _charges = null;
        _rowCounts = null;
        _rowOffsets = null;
        _potential = null;
        _intensity = null;
        _gridWidth = 0;
        _gridHeight = 0;
    }

    public void Dispose()
    {
        DisposeGridBuffers();
        _jumpFloodA?.Dispose();
        _jumpFloodB?.Dispose();
        _jumpFloodA = null;
        _jumpFloodB = null;
        _jumpFloodCapacity = 0;
        _glowAccum?.Dispose();
        _glowTemp?.Dispose();
        _glowMap?.Dispose();
        _glowAccum = null;
        _glowTemp = null;
        _glowMap = null;
        _glowCapacity = 0;
        _packedSource?.Dispose();
        _packedOutput?.Dispose();
        _packedSource = null;
        _packedOutput = null;
        _packedWidth = 0;
        _packedHeight = 0;
        _scratch.Dispose();
    }

    internal readonly record struct Parameters(
        DielectricBreakdownQuality Quality,
        float Growth,
        float Branching,
        float AngleDegrees,
        float ReachPixels,
        float Thickness,
        float Glow,
        float ColorR,
        float ColorG,
        float ColorB,
        int Seed);
}
