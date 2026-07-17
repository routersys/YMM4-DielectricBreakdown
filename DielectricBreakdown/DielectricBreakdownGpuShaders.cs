using ComputeSharp;

namespace DielectricBreakdown;

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct FillIntShader(
    ReadWriteBuffer<int> values,
    int length,
    int value) : IComputeShader
{
    private readonly ReadWriteBuffer<int> values = values;
    private readonly int length = length;
    private readonly int value = value;

    public void Execute()
    {
        var index = ThreadIds.X;
        if (index >= length)
            return;
        values[index] = value;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct FillIntPlaneShader(
    ReadWriteBuffer<int> values,
    int width,
    int height,
    int value) : IComputeShader
{
    private readonly ReadWriteBuffer<int> values = values;
    private readonly int width = width;
    private readonly int height = height;
    private readonly int value = value;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
            return;
        values[y * width + x] = value;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct InitScratchShader(
    ReadWriteBuffer<int> scratch,
    int sentinel) : IComputeShader
{
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int sentinel = sentinel;

    public void Execute()
    {
        if (ThreadIds.X != 0)
            return;
        scratch[0] = 0;
        scratch[1] = 0;
        scratch[2] = sentinel;
        scratch[3] = -1;
        scratch[4] = -1;
        scratch[5] = -1;
        scratch[6] = 2147483647;
        scratch[7] = 2147483647;
        scratch[8] = -1073741824;
        scratch[9] = 0;
        scratch[10] = 2147483647;
        scratch[11] = 2147483647;
        scratch[12] = -1;
        scratch[13] = -1;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct PotentialRangeShader(
    ReadWriteBuffer<int> state,
    ReadWriteBuffer<float> potential,
    ReadWriteBuffer<int> scratch,
    int gridWidth,
    int gridHeight,
    int step,
    int sentinel,
    float fieldBias,
    float directionX,
    float directionY,
    float projectionOffset,
    float projectionInverseRange) : IComputeShader
{
    private readonly ReadWriteBuffer<int> state = state;
    private readonly ReadWriteBuffer<float> potential = potential;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly int step = step;
    private readonly int sentinel = sentinel;
    private readonly float fieldBias = fieldBias;
    private readonly float directionX = directionX;
    private readonly float directionY = directionY;
    private readonly float projectionOffset = projectionOffset;
    private readonly float projectionInverseRange = projectionInverseRange;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;
        if (scratch[2] != sentinel)
            return;

        var index = gy * gridWidth + gx;
        if (state[index] != 0)
            return;
        if (!HasStructureNeighbor(gx, gy))
            return;

        var quantized = DielectricBreakdownShaderMath.QuantizedPotential(
            potential[index],
            scratch[1],
            gx,
            gy,
            fieldBias,
            directionX,
            directionY,
            projectionOffset,
            projectionInverseRange);
        Hlsl.InterlockedMin(ref scratch[10 + (step & 1)], quantized);
        Hlsl.InterlockedMax(ref scratch[12 + (step & 1)], quantized);
    }

    private bool HasStructureNeighbor(int gx, int gy)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;
                var nx = gx + dx;
                var ny = gy + dy;
                if (nx < 0 || nx >= gridWidth || ny < 0 || ny >= gridHeight)
                    continue;
                if (state[ny * gridWidth + nx] == 1)
                    return true;
            }
        }
        return false;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct SilhouetteShader(
    ReadWriteTexture2D<Bgra32, Float4> source,
    ReadWriteBuffer<int> mask,
    int width,
    int height,
    int gridWidth,
    int gridHeight,
    float cellSize,
    float alphaThreshold) : IComputeShader
{
    private readonly ReadWriteTexture2D<Bgra32, Float4> source = source;
    private readonly ReadWriteBuffer<int> mask = mask;
    private readonly int width = width;
    private readonly int height = height;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly float cellSize = cellSize;
    private readonly float alphaThreshold = alphaThreshold;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var x0 = Hlsl.Max((int)(gx * cellSize), 0);
        var x1 = Hlsl.Min((int)Hlsl.Ceil((gx + 1) * cellSize), width);
        var y0 = Hlsl.Max((int)(gy * cellSize), 0);
        var y1 = Hlsl.Min((int)Hlsl.Ceil((gy + 1) * cellSize), height);

        var found = 0;
        for (var y = y0; y < y1 && found == 0; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                if (source[new Int2(x, y)].W > alphaThreshold)
                {
                    found = 1;
                    break;
                }
            }
        }
        mask[gy * gridWidth + gx] = found;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ElectrodeInitShader(
    ReadWriteBuffer<int> mask,
    ReadWriteBuffer<int> state,
    ReadWriteBuffer<int> birth,
    ReadWriteBuffer<int> parent,
    ReadWriteBuffer<int> scratch,
    int gridWidth,
    int gridHeight,
    float directionX,
    float directionY,
    float projectionQuantScale) : IComputeShader
{
    private readonly ReadWriteBuffer<int> mask = mask;
    private readonly ReadWriteBuffer<int> state = state;
    private readonly ReadWriteBuffer<int> birth = birth;
    private readonly ReadWriteBuffer<int> parent = parent;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly float directionX = directionX;
    private readonly float directionY = directionY;
    private readonly float projectionQuantScale = projectionQuantScale;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        if (mask[index] == 0)
        {
            state[index] = 0;
            birth[index] = 268435456;
            parent[index] = index;
            return;
        }

        var left = gx > 0 ? mask[index - 1] : 0;
        var right = gx < gridWidth - 1 ? mask[index + 1] : 0;
        var up = gy > 0 ? mask[index - gridWidth] : 0;
        var down = gy < gridHeight - 1 ? mask[index + gridWidth] : 0;
        if (left == 1 && right == 1 && up == 1 && down == 1)
        {
            state[index] = 2;
            birth[index] = 268435456;
            parent[index] = index;
            return;
        }

        state[index] = 1;
        birth[index] = 0;
        parent[index] = index;
        var projection = (gx + 0.5f) * directionX + (gy + 0.5f) * directionY;
        Hlsl.InterlockedMax(ref scratch[8], (int)(projection * projectionQuantScale));
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ChargeRowCountShader(
    ReadWriteBuffer<int> state,
    ReadWriteBuffer<int> rowCounts,
    int gridWidth,
    int gridHeight) : IComputeShader
{
    private readonly ReadWriteBuffer<int> state = state;
    private readonly ReadWriteBuffer<int> rowCounts = rowCounts;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;

    public void Execute()
    {
        var row = ThreadIds.X;
        if (row >= gridHeight)
            return;

        var count = 0;
        var offset = row * gridWidth;
        for (var x = 0; x < gridWidth; x++)
        {
            if (state[offset + x] == 1)
                count++;
        }
        rowCounts[row] = count;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ChargeRowOffsetShader(
    ReadWriteBuffer<int> rowCounts,
    ReadWriteBuffer<int> rowOffsets,
    ReadWriteBuffer<int> scratch,
    int gridHeight) : IComputeShader
{
    private readonly ReadWriteBuffer<int> rowCounts = rowCounts;
    private readonly ReadWriteBuffer<int> rowOffsets = rowOffsets;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int gridHeight = gridHeight;

    public void Execute()
    {
        if (ThreadIds.X != 0)
            return;

        var total = 0;
        for (var row = 0; row < gridHeight; row++)
        {
            rowOffsets[row] = total;
            total += rowCounts[row];
        }
        scratch[0] = total;
        scratch[1] = total;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ChargeFillShader(
    ReadWriteBuffer<int> state,
    ReadWriteBuffer<int> rowOffsets,
    ReadWriteBuffer<int> charges,
    int gridWidth,
    int gridHeight) : IComputeShader
{
    private readonly ReadWriteBuffer<int> state = state;
    private readonly ReadWriteBuffer<int> rowOffsets = rowOffsets;
    private readonly ReadWriteBuffer<int> charges = charges;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;

    public void Execute()
    {
        var row = ThreadIds.X;
        if (row >= gridHeight)
            return;

        var slot = rowOffsets[row];
        var offset = row * gridWidth;
        for (var x = 0; x < gridWidth; x++)
        {
            if (state[offset + x] == 1)
            {
                charges[slot] = offset + x;
                slot++;
            }
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct InitialPotentialShader(
    ReadWriteBuffer<int> charges,
    ReadWriteBuffer<int> scratch,
    ReadWriteBuffer<float> potential,
    int gridWidth,
    int gridHeight,
    float chargeRadius) : IComputeShader
{
    private readonly ReadWriteBuffer<int> charges = charges;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly ReadWriteBuffer<float> potential = potential;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly float chargeRadius = chargeRadius;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var count = scratch[1];
        var sum = 0f;
        for (var j = 0; j < count; j++)
        {
            var charge = charges[j];
            var dx = gx - charge % gridWidth;
            var dy = gy - charge / gridWidth;
            var distance = Hlsl.Sqrt((float)(dx * dx + dy * dy));
            sum += 1f - chargeRadius / Hlsl.Max(distance, chargeRadius);
        }
        potential[gy * gridWidth + gx] = sum;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ScoreShader(
    ReadWriteBuffer<int> state,
    ReadWriteBuffer<float> potential,
    ReadWriteBuffer<int> scratch,
    int gridWidth,
    int gridHeight,
    int step,
    int sentinel,
    int seed,
    float eta,
    float fieldBias,
    float directionX,
    float directionY,
    float projectionOffset,
    float projectionInverseRange) : IComputeShader
{
    private readonly ReadWriteBuffer<int> state = state;
    private readonly ReadWriteBuffer<float> potential = potential;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly int step = step;
    private readonly int sentinel = sentinel;
    private readonly int seed = seed;
    private readonly float eta = eta;
    private readonly float fieldBias = fieldBias;
    private readonly float directionX = directionX;
    private readonly float directionY = directionY;
    private readonly float projectionOffset = projectionOffset;
    private readonly float projectionInverseRange = projectionInverseRange;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;
        if (scratch[2] != sentinel)
            return;

        var index = gy * gridWidth + gx;
        if (state[index] != 0)
            return;
        if (!HasStructureNeighbor(gx, gy))
            return;

        var quantized = DielectricBreakdownShaderMath.QuantizedScore(
            potential[index],
            scratch[1],
            scratch[10 + (step & 1)],
            scratch[12 + (step & 1)],
            gx,
            gy,
            index,
            step,
            seed,
            eta,
            fieldBias,
            directionX,
            directionY,
            projectionOffset,
            projectionInverseRange);
        Hlsl.InterlockedMax(ref scratch[4 + (step & 1)], quantized);
    }

    private bool HasStructureNeighbor(int gx, int gy)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;
                var nx = gx + dx;
                var ny = gy + dy;
                if (nx < 0 || nx >= gridWidth || ny < 0 || ny >= gridHeight)
                    continue;
                if (state[ny * gridWidth + nx] == 1)
                    return true;
            }
        }
        return false;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct ResolveShader(
    ReadWriteBuffer<int> state,
    ReadWriteBuffer<float> potential,
    ReadWriteBuffer<int> scratch,
    int gridWidth,
    int gridHeight,
    int step,
    int sentinel,
    int seed,
    float eta,
    float fieldBias,
    float directionX,
    float directionY,
    float projectionOffset,
    float projectionInverseRange) : IComputeShader
{
    private readonly ReadWriteBuffer<int> state = state;
    private readonly ReadWriteBuffer<float> potential = potential;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly int step = step;
    private readonly int sentinel = sentinel;
    private readonly int seed = seed;
    private readonly float eta = eta;
    private readonly float fieldBias = fieldBias;
    private readonly float directionX = directionX;
    private readonly float directionY = directionY;
    private readonly float projectionOffset = projectionOffset;
    private readonly float projectionInverseRange = projectionInverseRange;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;
        if (scratch[2] != sentinel)
            return;

        var index = gy * gridWidth + gx;
        if (state[index] != 0)
            return;
        if (!HasStructureNeighbor(gx, gy))
            return;

        var quantized = DielectricBreakdownShaderMath.QuantizedScore(
            potential[index],
            scratch[1],
            scratch[10 + (step & 1)],
            scratch[12 + (step & 1)],
            gx,
            gy,
            index,
            step,
            seed,
            eta,
            fieldBias,
            directionX,
            directionY,
            projectionOffset,
            projectionInverseRange);
        if (quantized == scratch[4 + (step & 1)])
            Hlsl.InterlockedMin(ref scratch[6 + (step & 1)], index);
    }

    private bool HasStructureNeighbor(int gx, int gy)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;
                var nx = gx + dx;
                var ny = gy + dy;
                if (nx < 0 || nx >= gridWidth || ny < 0 || ny >= gridHeight)
                    continue;
                if (state[ny * gridWidth + nx] == 1)
                    return true;
            }
        }
        return false;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct CommitShader(
    ReadWriteBuffer<int> state,
    ReadWriteBuffer<int> birth,
    ReadWriteBuffer<int> parent,
    ReadWriteBuffer<float> potential,
    ReadWriteBuffer<int> scratch,
    int gridWidth,
    int gridHeight,
    int step,
    int sentinel,
    int reachQuantized,
    float chargeRadius,
    float directionX,
    float directionY,
    float projectionQuantScale) : IComputeShader
{
    private readonly ReadWriteBuffer<int> state = state;
    private readonly ReadWriteBuffer<int> birth = birth;
    private readonly ReadWriteBuffer<int> parent = parent;
    private readonly ReadWriteBuffer<float> potential = potential;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly int step = step;
    private readonly int sentinel = sentinel;
    private readonly int reachQuantized = reachQuantized;
    private readonly float chargeRadius = chargeRadius;
    private readonly float directionX = directionX;
    private readonly float directionY = directionY;
    private readonly float projectionQuantScale = projectionQuantScale;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        var nextParity = (step + 1) & 1;
        if (index == 0)
        {
            scratch[4 + nextParity] = -1;
            scratch[6 + nextParity] = 2147483647;
            scratch[10 + nextParity] = 2147483647;
            scratch[12 + nextParity] = -1;
        }

        var chosen = scratch[6 + (step & 1)];
        if (chosen == 2147483647)
            return;

        var chosenX = chosen % gridWidth;
        var chosenY = chosen / gridWidth;
        var dx = gx - chosenX;
        var dy = gy - chosenY;
        var distance = Hlsl.Sqrt((float)(dx * dx + dy * dy));
        potential[index] += 1f - chargeRadius / Hlsl.Max(distance, chargeRadius);

        if (index != chosen)
            return;

        state[index] = 1;
        birth[index] = step + 1;
        parent[index] = SelectParent(gx, gy);
        scratch[1] = scratch[1] + 1;
        scratch[9] = step + 1;
        var projection = (gx + 0.5f) * directionX + (gy + 0.5f) * directionY;
        if ((int)(projection * projectionQuantScale) >= scratch[8] + reachQuantized && scratch[2] == sentinel)
        {
            scratch[2] = step + 1;
            scratch[3] = index;
        }
    }

    private int SelectParent(int gx, int gy)
    {
        var bestIndex = gy * gridWidth + gx;
        var bestBirth = -1;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0)
                    continue;
                var nx = gx + dx;
                var ny = gy + dy;
                if (nx < 0 || nx >= gridWidth || ny < 0 || ny >= gridHeight)
                    continue;
                var neighbor = ny * gridWidth + nx;
                if (state[neighbor] != 1)
                    continue;
                var neighborBirth = birth[neighbor];
                if (neighborBirth > bestBirth || (neighborBirth == bestBirth && neighbor < bestIndex))
                {
                    bestBirth = neighborBirth;
                    bestIndex = neighbor;
                }
            }
        }
        return bestIndex;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.X)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct MainChannelShader(
    ReadWriteBuffer<int> parent,
    ReadWriteBuffer<int> birth,
    ReadWriteBuffer<int> mainFlag,
    ReadWriteBuffer<int> scratch,
    int sentinel,
    int maxWalk) : IComputeShader
{
    private readonly ReadWriteBuffer<int> parent = parent;
    private readonly ReadWriteBuffer<int> birth = birth;
    private readonly ReadWriteBuffer<int> mainFlag = mainFlag;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly int sentinel = sentinel;
    private readonly int maxWalk = maxWalk;

    public void Execute()
    {
        if (ThreadIds.X != 0)
            return;
        if (scratch[2] == sentinel)
            return;

        var cell = scratch[3];
        for (var i = 0; i < maxWalk; i++)
        {
            if (cell < 0)
                return;
            mainFlag[cell] = 1;
            if (birth[cell] == 0)
                return;
            var next = parent[cell];
            if (next == cell)
                return;
            cell = next;
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct IntensityShader(
    ReadWriteBuffer<int> state,
    ReadWriteBuffer<int> birth,
    ReadWriteBuffer<int> parent,
    ReadWriteBuffer<int> mainFlag,
    ReadWriteBuffer<float> intensity,
    int gridWidth,
    int gridHeight,
    int maxWalk,
    float leaderIntensity,
    float sideIntensity,
    float sideDecayPerHop) : IComputeShader
{
    private readonly ReadWriteBuffer<int> state = state;
    private readonly ReadWriteBuffer<int> birth = birth;
    private readonly ReadWriteBuffer<int> parent = parent;
    private readonly ReadWriteBuffer<int> mainFlag = mainFlag;
    private readonly ReadWriteBuffer<float> intensity = intensity;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly int maxWalk = maxWalk;
    private readonly float leaderIntensity = leaderIntensity;
    private readonly float sideIntensity = sideIntensity;
    private readonly float sideDecayPerHop = sideDecayPerHop;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        if (state[index] != 1 || birth[index] < 1)
            return;

        if (mainFlag[index] == 1)
        {
            intensity[index] = 1f;
            return;
        }

        var cell = index;
        var hops = 0;
        var foundMain = false;
        for (var i = 0; i < maxWalk; i++)
        {
            var next = parent[cell];
            if (next == cell || birth[cell] == 0)
                break;
            cell = next;
            hops++;
            if (mainFlag[cell] == 1)
            {
                foundMain = true;
                break;
            }
        }

        var baseIntensity = foundMain ? sideIntensity : leaderIntensity;
        intensity[index] = baseIntensity * Hlsl.Pow(sideDecayPerHop, (float)hops);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct JumpFloodSeedShader(
    ReadWriteBuffer<int> state,
    ReadWriteBuffer<int> birth,
    ReadWriteBuffer<int> scratch,
    ReadWriteBuffer<int> jumpFlood,
    int gridWidth,
    int gridHeight,
    int width,
    int height,
    int sentinel,
    float cellSize,
    float growth) : IComputeShader
{
    private readonly ReadWriteBuffer<int> state = state;
    private readonly ReadWriteBuffer<int> birth = birth;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly ReadWriteBuffer<int> jumpFlood = jumpFlood;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly int width = width;
    private readonly int height = height;
    private readonly int sentinel = sentinel;
    private readonly float cellSize = cellSize;
    private readonly float growth = growth;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        if (state[index] != 1 || birth[index] < 1)
            return;

        var contact = scratch[2];
        var total = contact != sentinel ? contact : scratch[9];
        var visible = growth * total;
        if (birth[index] - 1 >= visible)
            return;

        var px = (int)((gx + 0.5f) * cellSize);
        var py = (int)((gy + 0.5f) * cellSize);
        if (px < 0 || px >= width || py < 0 || py >= height)
            return;
        jumpFlood[py * width + px] = index;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct JumpFloodPassShader(
    ReadWriteBuffer<int> input,
    ReadWriteBuffer<int> output,
    int width,
    int height,
    int gridWidth,
    int stepSize,
    float cellSize) : IComputeShader
{
    private readonly ReadWriteBuffer<int> input = input;
    private readonly ReadWriteBuffer<int> output = output;
    private readonly int width = width;
    private readonly int height = height;
    private readonly int gridWidth = gridWidth;
    private readonly int stepSize = stepSize;
    private readonly float cellSize = cellSize;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
            return;

        var best = -1;
        var bestDistance = 3.402823e+38f;
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dx = -1; dx <= 1; dx++)
            {
                var sx = x + dx * stepSize;
                var sy = y + dy * stepSize;
                if (sx < 0 || sx >= width || sy < 0 || sy >= height)
                    continue;
                var candidate = input[sy * width + sx];
                if (candidate < 0)
                    continue;
                var cx = (candidate % gridWidth + 0.5f) * cellSize;
                var cy = (candidate / gridWidth + 0.5f) * cellSize;
                var ddx = x + 0.5f - cx;
                var ddy = y + 0.5f - cy;
                var distance = ddx * ddx + ddy * ddy;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }
        }
        output[y * width + x] = best;
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct GlowDepositShader(
    ReadWriteBuffer<int> state,
    ReadWriteBuffer<int> birth,
    ReadWriteBuffer<int> parent,
    ReadWriteBuffer<int> mainFlag,
    ReadWriteBuffer<float> intensity,
    ReadWriteBuffer<int> scratch,
    ReadWriteBuffer<int> glowAccum,
    int gridWidth,
    int gridHeight,
    int glowWidth,
    int glowHeight,
    int sentinel,
    float cellSize,
    float growth,
    float tipBoost,
    float inverseTipTau,
    float flashAmplitude,
    float inverseFlashTau) : IComputeShader
{
    private readonly ReadWriteBuffer<int> state = state;
    private readonly ReadWriteBuffer<int> birth = birth;
    private readonly ReadWriteBuffer<int> parent = parent;
    private readonly ReadWriteBuffer<int> mainFlag = mainFlag;
    private readonly ReadWriteBuffer<float> intensity = intensity;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly ReadWriteBuffer<int> glowAccum = glowAccum;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly int glowWidth = glowWidth;
    private readonly int glowHeight = glowHeight;
    private readonly int sentinel = sentinel;
    private readonly float cellSize = cellSize;
    private readonly float growth = growth;
    private readonly float tipBoost = tipBoost;
    private readonly float inverseTipTau = inverseTipTau;
    private readonly float flashAmplitude = flashAmplitude;
    private readonly float inverseFlashTau = inverseFlashTau;

    public void Execute()
    {
        var gx = ThreadIds.X;
        var gy = ThreadIds.Y;
        if (gx >= gridWidth || gy >= gridHeight)
            return;

        var index = gy * gridWidth + gx;
        if (state[index] != 1)
            return;
        var cellBirth = birth[index];
        if (cellBirth < 1)
            return;

        var contact = scratch[2];
        var total = contact != sentinel ? contact : scratch[9];
        var visible = growth * total;
        if (cellBirth - 1 >= visible)
            return;

        var parentCell = parent[index];
        var cx = (gx + 0.5f) * cellSize;
        var cy = (gy + 0.5f) * cellSize;
        var px = (parentCell % gridWidth + 0.5f) * cellSize;
        var py = (parentCell / gridWidth + 0.5f) * cellSize;
        var t = Hlsl.Saturate(visible - (cellBirth - 1));
        var ex = Hlsl.Lerp(px, cx, t);
        var ey = Hlsl.Lerp(py, cy, t);

        var value = intensity[index];
        value *= 1f + tipBoost * Hlsl.Exp(-(visible - cellBirth) * inverseTipTau);
        if (mainFlag[index] == 1 && contact != sentinel && visible >= contact)
            value *= 1f + flashAmplitude * Hlsl.Exp(-(visible - contact) * inverseFlashTau);

        var segmentX = ex - px;
        var segmentY = ey - py;
        var length = Hlsl.Sqrt(segmentX * segmentX + segmentY * segmentY);
        var samples = Hlsl.Clamp((int)(length * 0.5f) + 1, 1, 8);
        var deposit = (int)(value * length / samples * 256f);
        if (deposit <= 0)
            return;
        for (var i = 0; i < samples; i++)
        {
            var fraction = (i + 0.5f) / samples;
            var sx = (int)((px + segmentX * fraction) * 0.25f);
            var sy = (int)((py + segmentY * fraction) * 0.25f);
            if (sx < 0 || sx >= glowWidth || sy < 0 || sy >= glowHeight)
                continue;
            Hlsl.InterlockedAdd(ref glowAccum[sy * glowWidth + sx], deposit);
        }
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct GlowBlurHorizontalShader(
    ReadWriteBuffer<int> glowAccum,
    ReadWriteBuffer<float> glowTemp,
    int glowWidth,
    int glowHeight,
    int radius) : IComputeShader
{
    private readonly ReadWriteBuffer<int> glowAccum = glowAccum;
    private readonly ReadWriteBuffer<float> glowTemp = glowTemp;
    private readonly int glowWidth = glowWidth;
    private readonly int glowHeight = glowHeight;
    private readonly int radius = radius;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= glowWidth || y >= glowHeight)
            return;

        var sum = 0f;
        var offset = y * glowWidth;
        for (var i = -radius; i <= radius; i++)
        {
            var sx = x + i;
            if (sx < 0 || sx >= glowWidth)
                continue;
            var weight = 1f - Hlsl.Abs((float)i) / (radius + 1);
            sum += glowAccum[offset + sx] * weight;
        }
        glowTemp[offset + x] = sum / (256f * (radius + 1));
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct GlowBlurVerticalShader(
    ReadWriteBuffer<float> glowTemp,
    ReadWriteBuffer<float> glowMap,
    int glowWidth,
    int glowHeight,
    int radius) : IComputeShader
{
    private readonly ReadWriteBuffer<float> glowTemp = glowTemp;
    private readonly ReadWriteBuffer<float> glowMap = glowMap;
    private readonly int glowWidth = glowWidth;
    private readonly int glowHeight = glowHeight;
    private readonly int radius = radius;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= glowWidth || y >= glowHeight)
            return;

        var sum = 0f;
        for (var i = -radius; i <= radius; i++)
        {
            var sy = y + i;
            if (sy < 0 || sy >= glowHeight)
                continue;
            var weight = 1f - Hlsl.Abs((float)i) / (radius + 1);
            sum += glowTemp[sy * glowWidth + x] * weight;
        }
        glowMap[y * glowWidth + x] = sum / (radius + 1);
    }
}

[ThreadGroupSize(DefaultThreadGroupSizes.XY)]
[GeneratedComputeShaderDescriptor]
internal readonly partial struct RenderShader(
    ReadWriteBuffer<int> jumpFlood,
    ReadWriteBuffer<int> state,
    ReadWriteBuffer<int> birth,
    ReadWriteBuffer<int> parent,
    ReadWriteBuffer<int> mainFlag,
    ReadWriteBuffer<float> intensity,
    ReadWriteBuffer<int> scratch,
    ReadWriteBuffer<float> glowMap,
    ReadWriteTexture2D<Bgra32, Float4> output,
    int width,
    int height,
    int gridWidth,
    int gridHeight,
    int glowWidth,
    int glowHeight,
    int sentinel,
    float cellSize,
    float growth,
    float thickness,
    float glowStrength,
    float tipBoost,
    float inverseTipTau,
    float flashAmplitude,
    float inverseFlashTau,
    float colorR,
    float colorG,
    float colorB) : IComputeShader
{
    private readonly ReadWriteBuffer<int> jumpFlood = jumpFlood;
    private readonly ReadWriteBuffer<int> state = state;
    private readonly ReadWriteBuffer<int> birth = birth;
    private readonly ReadWriteBuffer<int> parent = parent;
    private readonly ReadWriteBuffer<int> mainFlag = mainFlag;
    private readonly ReadWriteBuffer<float> intensity = intensity;
    private readonly ReadWriteBuffer<int> scratch = scratch;
    private readonly ReadWriteBuffer<float> glowMap = glowMap;
    private readonly ReadWriteTexture2D<Bgra32, Float4> output = output;
    private readonly int width = width;
    private readonly int height = height;
    private readonly int gridWidth = gridWidth;
    private readonly int gridHeight = gridHeight;
    private readonly int glowWidth = glowWidth;
    private readonly int glowHeight = glowHeight;
    private readonly int sentinel = sentinel;
    private readonly float cellSize = cellSize;
    private readonly float growth = growth;
    private readonly float thickness = thickness;
    private readonly float glowStrength = glowStrength;
    private readonly float tipBoost = tipBoost;
    private readonly float inverseTipTau = inverseTipTau;
    private readonly float flashAmplitude = flashAmplitude;
    private readonly float inverseFlashTau = inverseFlashTau;
    private readonly float colorR = colorR;
    private readonly float colorG = colorG;
    private readonly float colorB = colorB;

    public void Execute()
    {
        var x = ThreadIds.X;
        var y = ThreadIds.Y;
        if (x >= width || y >= height)
            return;

        var cell = jumpFlood[y * width + x];
        var bestEnergy = 0f;
        var bestCore = 0f;
        if (cell >= 0)
        {
            var contact = scratch[2];
            var total = contact != sentinel ? contact : scratch[9];
            var visible = growth * total;
            var position = new Float2(x + 0.5f, y + 0.5f);

            Accumulate(cell, position, visible, contact, ref bestEnergy, ref bestCore);
            var parentCell = parent[cell];
            if (parentCell != cell)
                Accumulate(parentCell, position, visible, contact, ref bestEnergy, ref bestCore);

            var cx = cell % gridWidth;
            var cy = cell / gridWidth;
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0)
                        continue;
                    var nx = cx + dx;
                    var ny = cy + dy;
                    if (nx < 0 || nx >= gridWidth || ny < 0 || ny >= gridHeight)
                        continue;
                    var neighbor = ny * gridWidth + nx;
                    if (state[neighbor] == 1 && parent[neighbor] == cell)
                        Accumulate(neighbor, position, visible, contact, ref bestEnergy, ref bestCore);
                }
            }
        }

        var energy = bestEnergy + glowStrength * SampleGlow(x, y);
        if (energy <= 0f)
        {
            output[ThreadIds.XY] = new Float4(0f, 0f, 0f, 0f);
            return;
        }

        var alpha = Hlsl.Saturate(energy);
        var whiteness = Hlsl.Saturate(bestCore);
        var r = Hlsl.Lerp(colorR, 1f, whiteness) * alpha;
        var g = Hlsl.Lerp(colorG, 1f, whiteness) * alpha;
        var b = Hlsl.Lerp(colorB, 1f, whiteness) * alpha;
        output[ThreadIds.XY] = new Float4(r, g, b, alpha);
    }

    private void Accumulate(int cell, Float2 position, float visible, int contact, ref float bestEnergy, ref float bestCore)
    {
        var cellBirth = birth[cell];
        if (cellBirth < 1 || cellBirth - 1 >= visible)
            return;

        var parentCell = parent[cell];
        var cx = (cell % gridWidth + 0.5f) * cellSize;
        var cy = (cell / gridWidth + 0.5f) * cellSize;
        var px = (parentCell % gridWidth + 0.5f) * cellSize;
        var py = (parentCell / gridWidth + 0.5f) * cellSize;
        var t = Hlsl.Saturate(visible - (cellBirth - 1));
        var ex = Hlsl.Lerp(px, cx, t);
        var ey = Hlsl.Lerp(py, cy, t);

        var abx = ex - px;
        var aby = ey - py;
        var apx = position.X - px;
        var apy = position.Y - py;
        var lengthSquared = abx * abx + aby * aby;
        var projection = lengthSquared > 1e-6f ? Hlsl.Saturate((apx * abx + apy * aby) / lengthSquared) : 0f;
        var dx = apx - abx * projection;
        var dy = apy - aby * projection;
        var distance = Hlsl.Sqrt(dx * dx + dy * dy);

        var value = intensity[cell];
        value *= 1f + tipBoost * Hlsl.Exp(-(visible - cellBirth) * inverseTipTau);
        if (mainFlag[cell] == 1 && contact != sentinel && visible >= contact)
            value *= 1f + flashAmplitude * Hlsl.Exp(-(visible - contact) * inverseFlashTau);

        var core = 1f - Hlsl.SmoothStep(thickness * 0.5f, thickness, distance);
        var energy = value * core;
        if (energy > bestEnergy)
        {
            bestEnergy = energy;
            bestCore = core * value;
        }
    }

    private float SampleGlow(int x, int y)
    {
        var fx = Hlsl.Clamp(x * 0.25f - 0.5f, 0f, glowWidth - 1f);
        var fy = Hlsl.Clamp(y * 0.25f - 0.5f, 0f, glowHeight - 1f);
        var ix0 = (int)fx;
        var iy0 = (int)fy;
        var wx = fx - ix0;
        var wy = fy - iy0;
        var ix1 = Hlsl.Min(ix0 + 1, glowWidth - 1);
        var iy1 = Hlsl.Min(iy0 + 1, glowHeight - 1);
        var top = Hlsl.Lerp(glowMap[iy0 * glowWidth + ix0], glowMap[iy0 * glowWidth + ix1], wx);
        var bottom = Hlsl.Lerp(glowMap[iy1 * glowWidth + ix0], glowMap[iy1 * glowWidth + ix1], wx);
        return Hlsl.Lerp(top, bottom, wy);
    }
}

internal static class DielectricBreakdownShaderMath
{
    public static int QuantizedPotential(
        float potentialSum,
        int chargeCount,
        int gx,
        int gy,
        float fieldBias,
        float directionX,
        float directionY,
        float projectionOffset,
        float projectionInverseRange)
    {
        var chargePotential = potentialSum / Hlsl.Max(chargeCount, 1);
        var projection = (gx + 0.5f) * directionX + (gy + 0.5f) * directionY;
        var fieldPotential = Hlsl.Saturate((projection - projectionOffset) * projectionInverseRange);
        var combined = Hlsl.Saturate((1f - fieldBias) * chargePotential + fieldBias * fieldPotential);
        return (int)(combined * 16777215f);
    }

    public static int QuantizedScore(
        float potentialSum,
        int chargeCount,
        int minimumQuantized,
        int maximumQuantized,
        int gx,
        int gy,
        int index,
        int step,
        int seed,
        float eta,
        float fieldBias,
        float directionX,
        float directionY,
        float projectionOffset,
        float projectionInverseRange)
    {
        var quantized = QuantizedPotential(potentialSum, chargeCount, gx, gy, fieldBias, directionX, directionY, projectionOffset, projectionInverseRange);
        var range = Hlsl.Max(maximumQuantized - minimumQuantized, 1);
        var normalized = Hlsl.Saturate((quantized - minimumQuantized) / (float)range);
        var hash = (uint)index * 0x9E3779B9u ^ (uint)step * 0x85EBCA6Bu ^ (uint)seed * 0xC2B2AE35u;
        var uniform = Hlsl.Clamp(Hash01(hash), 1e-7f, 0.9999999f);
        var gumbel = -Hlsl.Log(-Hlsl.Log(uniform));
        var score = eta * Hlsl.Log(Hlsl.Max(normalized, 1e-6f)) + gumbel;
        var clamped = Hlsl.Clamp((score + 64f) / 96f, 0f, 1f);
        return (int)(clamped * 16777215f);
    }

    private static float Hash01(uint value)
    {
        value ^= value >> 16;
        value *= 0x7FEB352Du;
        value ^= value >> 15;
        value *= 0x846CA68Bu;
        value ^= value >> 16;
        return value * 2.3283064e-10f;
    }
}
