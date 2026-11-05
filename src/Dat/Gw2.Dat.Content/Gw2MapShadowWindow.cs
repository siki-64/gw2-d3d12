using System.Numerics;

namespace Gw2.Dat.Content;

public readonly record struct Gw2MapShadowPage(int PhysicalColumn, int PhysicalRow, int Column, int Row);

public readonly record struct Gw2MapShadowConstants(Vector4 WorldToShadowA, Vector4 WorldToShadowB, Vector4 WorldToShadowC, Vector4 WorldToShadowD, float Coverage);

public sealed class Gw2MapShadowWindow
{
    public const int PageSize = Gw2MapShadow.TileSize;
    private const float CoverageTolerance = 1f / 4096f;
    private const float NearPlaneDivisor = 1024f;
    private const int UnsetOrigin = -1;
    private const float DepthScaleNumerator = 100f;
    private const float DepthScaleBias = -99f;

    private readonly Gw2MapShadow _shadow;
    private readonly float _depthRange;
    private int _ringColumn;
    private int _ringRow;

    public Gw2MapShadowWindow(Gw2MapShadow shadow, int resolution, float depthRange, bool combinesShadowSamples = false)
    {
        _shadow = shadow ?? throw new ArgumentNullException(nameof(shadow));
        _depthRange = depthRange;
        Resolution = resolution;
        PagesPerSide = resolution / PageSize;
        CombinesShadowSamples = combinesShadowSamples;
    }

    public bool CombinesShadowSamples { get; }
    public Vector2 RingOffset => Gw2FarShadowCombine.RingOffset(_ringColumn, _ringRow, PagesPerSide);
    public int Resolution { get; }
    public int PagesPerSide { get; }
    public int OriginColumn { get; private set; } = UnsetOrigin;
    public int OriginRow { get; private set; } = UnsetOrigin;

    public Gw2MapShadowConstants Update(Vector3 eye, ReadOnlySpan<Vector3> farCorners, List<Gw2MapShadowPage> pagesToLoad)
    {
        ArgumentNullException.ThrowIfNull(pagesToLoad);
        var width = (int)_shadow.Width;
        var height = (int)_shadow.Height;
        var upper = 1f;
        var lower = 0f;
        (int MinimumX, int MinimumY, int MaximumX, int MaximumY) bounds;
        do
        {
            var middle = (upper + lower) * 0.5f;
            bounds = TexelBounds(eye, farCorners, middle, width, height);
            if (bounds.MaximumX - bounds.MinimumX > Resolution - PageSize || bounds.MaximumY - bounds.MinimumY > Resolution - PageSize)
                upper = middle;
            else
                lower = middle;
        }
        while (upper - lower > CoverageTolerance);

        if (!WindowContains(bounds))
            MoveOrigin(
                Math.Min(Math.Max(bounds.MinimumX / PageSize, 0), LastOrigin(width)),
                Math.Min(Math.Max(bounds.MinimumY / PageSize, 0), LastOrigin(height)),
                pagesToLoad);

        return Constants(upper);
    }

    private int LastOrigin(int size) => size >= Resolution ? (size - Resolution) / PageSize : int.MaxValue;

    private (int MinimumX, int MinimumY, int MaximumX, int MaximumY) TexelBounds(Vector3 eye, ReadOnlySpan<Vector3> farCorners, float coverage, int width, int height)
    {
        var minimumX = width;
        var minimumY = height;
        var maximumX = -width;
        var maximumY = -height;
        for (var index = 0; index <= farCorners.Length; index++)
        {
            var point = index == 0 ? eye : eye + (farCorners[index - 1] - eye) * coverage;
            var projected = Vector4.Transform(new Vector4(point, 1f), _shadow.WorldToShadow);
            var x = (int)MathF.Round((projected.X * 0.5f + 0.5f) * width);
            var y = (int)MathF.Round((0.5f - projected.Y * 0.5f) * height);
            minimumX = Math.Min(minimumX, x);
            minimumY = Math.Min(minimumY, y);
            maximumX = Math.Max(maximumX, x);
            maximumY = Math.Max(maximumY, y);
        }

        return (minimumX, minimumY, maximumX, maximumY);
    }

    private bool WindowContains((int MinimumX, int MinimumY, int MaximumX, int MaximumY) bounds)
    {
        if (OriginColumn == UnsetOrigin && OriginRow == UnsetOrigin) return false;
        var left = OriginColumn * PageSize;
        var top = OriginRow * PageSize;
        bool Inside(int x, int y) => x >= left && y >= top && x < left + Resolution && y < top + Resolution;
        return Inside(bounds.MinimumX, bounds.MinimumY) && Inside(bounds.MaximumX, bounds.MaximumY);
    }

    private void MoveOrigin(int column, int row, List<Gw2MapShadowPage> pagesToLoad)
    {
        var first = OriginColumn == UnsetOrigin && OriginRow == UnsetOrigin;
        var previousColumn = OriginColumn;
        var previousRow = OriginRow;
        var previousRingColumn = _ringColumn;
        var previousRingRow = _ringRow;
        if (first)
        {
            _ringColumn = 0;
            _ringRow = 0;
        }
        else
        {
            _ringColumn = Wrap(column - previousColumn + _ringColumn);
            _ringRow = Wrap(row - previousRow + _ringRow);
        }

        OriginColumn = column;
        OriginRow = row;
        for (var physicalRow = 0; physicalRow < PagesPerSide; physicalRow++)
        for (var physicalColumn = 0; physicalColumn < PagesPerSide; physicalColumn++)
        {
            var pageColumn = Wrap(physicalColumn - _ringColumn) + column;
            var pageRow = Wrap(physicalRow - _ringRow) + row;
            var previousPageColumn = Wrap(physicalColumn - previousRingColumn) + previousColumn;
            var previousPageRow = Wrap(physicalRow - previousRingRow) + previousRow;
            if (first || pageColumn != previousPageColumn || pageRow != previousPageRow)
                pagesToLoad.Add(new Gw2MapShadowPage(physicalColumn, physicalRow, pageColumn, pageRow));
        }
    }

    private int Wrap(int page) => ((page % PagesPerSide) + PagesPerSide) % PagesPerSide;

    private Gw2MapShadowConstants Constants(float coverage)
    {
        var width = (float)_shadow.Width;
        var height = (float)_shadow.Height;
        var left = OriginColumn * PageSize;
        var top = OriginRow * PageSize;
        var right = left + Resolution;
        var bottom = top + Resolution;
        var horizontalRange = _shadow.S.Y - _shadow.S.X;
        var verticalRange = _shadow.T.Y - _shadow.T.X;
        var depth = _shadow.U.Y - _shadow.U.X;
        var view = Matrix4x4.CreateLookAtLeftHanded(_shadow.Eye, _shadow.Eye + _shadow.Direction, _shadow.Up);
        var projection = Matrix4x4.CreateOrthographicOffCenterLeftHanded(
            left / width * horizontalRange + _shadow.S.X,
            right / width * horizontalRange + _shadow.S.X,
            (height - bottom) / height * verticalRange + _shadow.T.X,
            (height - top) / height * verticalRange + _shadow.T.X,
            depth / NearPlaneDivisor,
            depth);
        var worldToShadow = view * projection;
        var constantsRingOffset = CombinesShadowSamples ? Vector2.Zero : RingOffset;
        var ringOffsetX = constantsRingOffset.X;
        var ringOffsetY = constantsRingOffset.Y;
        return new Gw2MapShadowConstants(
            new Vector4(worldToShadow.M11 * 0.5f, worldToShadow.M21 * 0.5f, worldToShadow.M31 * 0.5f, worldToShadow.M41 * 0.5f + ringOffsetX + 0.5f),
            new Vector4(-worldToShadow.M12 * 0.5f, -worldToShadow.M22 * 0.5f, -worldToShadow.M32 * 0.5f, ringOffsetY + 0.5f - worldToShadow.M42 * 0.5f),
            new Vector4(worldToShadow.M13, worldToShadow.M23, worldToShadow.M33, worldToShadow.M43),
            new Vector4(DepthScaleNumerator / (_depthRange * coverage), DepthScaleBias, 0f, 0f),
            coverage);
    }
}
