using Gw2.Formats;
using System.Numerics;

namespace Gw2.Dat.Content;

public sealed record Gw2ModelTextureReference(
    uint Flags,
    ulong Token,
    PackFileReference Reference,
    byte UvPSInputIndex = 0,
    ulong BlitId = 0,
    uint UvAnimId = 0)
{
    public bool TryGetFileId(out uint fileId) => Reference.TryGetFileId(out fileId);
}

public sealed record Gw2ModelMaterialConstant(uint Token, IReadOnlyList<float> Value, uint Flags);

public sealed record Gw2ModelMaterialConstantLink(ulong LinkToken, uint ConstantToken);

public sealed record Gw2ModelUvTransformLink(ulong LinkToken, uint UvAnimId, byte Type);

public sealed record Gw2ModelTextureTransform(
    uint Flags,
    byte UvIndex,
    byte Columns,
    byte Rows,
    ushort Count,
    float Fps,
    Vector2 Scroll,
    Vector2 ScrollFrequency,
    Vector2 Scale,
    Vector2 ScaleFrequency,
    float Rotate,
    float RotateFrequency = 0f);

public sealed record Gw2ModelMaterial(
    uint MaterialId,
    PackFileReference Reference,
    IReadOnlyList<Gw2ModelTextureReference> Textures,
    IReadOnlyList<Gw2ModelMaterialConstant>? Constants = null,
    uint MaterialFlags = 0)
{
    public ulong Token { get; init; }
    public uint SortOrder { get; init; }
    public uint SortLayer { get; init; }
    public byte TexCoordCount { get; init; }
    public IReadOnlyList<Gw2ModelMaterialConstantLink> ConstantLinks { get; init; } = [];
    public IReadOnlyList<Gw2ModelUvTransformLink> UvTransformLinks { get; init; } = [];
    public IReadOnlyList<Gw2ModelTextureTransform> TextureTransforms { get; init; } = [];

    public bool TryGetFileId(out uint fileId) => Reference.TryGetFileId(out fileId);
}

public sealed record Gw2ModelMorphVertex(ushort Index, Vector3 Position);

public sealed record Gw2ModelMorphTarget(
    IReadOnlyList<Gw2ModelMorphVertex> Positions,
    IReadOnlyList<Gw2ModelMorphVertex> Normals,
    ulong Mesh);

public sealed record Gw2ModelBoneOffset(
    ulong Bone,
    Vector3 Translation,
    Vector4 Rotation,
    IReadOnlyList<Vector4> BoneInverseOffset);

public sealed record Gw2ModelEffectLight(
    ulong Bone,
    byte[] Color,
    float NearDistance,
    float FarDistance,
    float Intensity);

public sealed record Gw2ModelEffectWind(
    ulong Bone,
    float NearDistance,
    float FarDistance,
    float Intensity);

public sealed record Gw2ModelObstacle(
    uint Affinity,
    ulong Bone,
    byte Type,
    byte Response,
    uint Flags,
    float DragCoefficient,
    float GravityCoefficient,
    float Length,
    float Width,
    float Height,
    float Radius,
    Vector4 TransformRowX,
    Vector4 TransformRowY,
    Vector4 TransformRowZ);

public sealed record Gw2ModelBoundingSphere(Vector3 Center, float Radius);

public sealed record Gw2ModelMeshBounds(Vector3 Center, Vector3 BoxExtent, float SphereRadius);

public sealed record Gw2ModelMesh(
    ulong Name,
    int MaterialIndex,
    Gw2VertexBuffer Vertices,
    IReadOnlyList<ushort> Indices,
    IReadOnlyList<IReadOnlyList<ushort>> Lods,
    ulong VisBone = 0,
    uint Flags = 0,
    IReadOnlyList<uint> SeamVertIndices = null!,
    Vector3 MinBound = default,
    Vector3 MaxBound = default,
    IReadOnlyList<Gw2ModelMeshBounds> Bounds = null!,
    string? MaterialName = null,
    IReadOnlyList<ulong> BoneBindings = null!,
    IReadOnlyList<uint> Transforms = null!,
    IReadOnlyList<Gw2ModelMorphTarget> MorphTargets = null!)
{
    public const uint Lod1Flag = 0x4;

    public const uint Lod2Flag = 0x1;

    public int LodLevel => (Flags & Lod1Flag) != 0 ? 1 : (Flags & Lod2Flag) != 0 ? 2 : 0;
}

public sealed class Gw2Model
{
    public Gw2Model(IReadOnlyList<Gw2ModelMesh> meshes, IReadOnlyList<Gw2ModelMaterial> materials, Gw2ModelBoundingSphere? boundingSphere = null)
    {
        BoundingSphere = boundingSphere;
        Meshes = meshes;
        Materials = materials;
    }

    public IReadOnlyList<Gw2ModelMesh> Meshes { get; }
    public IReadOnlyList<Gw2ModelMaterial> Materials { get; }
    public Gw2ModelBoundingSphere? BoundingSphere { get; }
    public IReadOnlyList<ulong> PermutationTokens { get; init; } = [];
    public IReadOnlyList<Gw2ModelBoneOffset> BoneOffsets { get; init; } = [];
    public IReadOnlyList<Gw2ModelEffectLight> EffectLights { get; init; } = [];
    public IReadOnlyList<Gw2ModelEffectWind> EffectWinds { get; init; } = [];
    public IReadOnlyList<Gw2ModelObstacle> Obstacles { get; init; } = [];
    public IReadOnlyList<Gw2ModelCloth> Cloths { get; init; } = [];
    public Gw2ModelLightning? Lightning { get; init; }
    public float[] LodOverride { get; init; } = [];
    public PackFileReference SoundScript { get; init; }

    public bool HasGeometry => Meshes.Count != 0;

    public static Gw2Model Parse(Gw2AssetDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var materials = new List<Gw2ModelMaterial>();
        var meshes = new List<Gw2ModelMesh>();
        Gw2ModelBoundingSphere? boundingSphere = null;
        IReadOnlyList<ulong> permutationTokens = [];

        var modelRoot = Root(document, "ModelFileData");
        if (modelRoot is not null)
        {
            if (modelRoot.Fields.GetValueOrDefault("boundingSphere") is PackObject sphere)
                boundingSphere = new(Vec3(sphere, "center"), PackValue.RequireF32(sphere, "radius"));
            var permutations = Array(modelRoot, "permutations");
            permutationTokens = PackValue.AsObjects(permutations).Select(permutation => PackValue.RequireU64(permutation, "token")).ToArray();
            if (permutations is { Length: > 0 } && permutations[0] is PackObject permutation)
            {
                var materialObjects = Array(permutation, "materials");
                if (materialObjects is not null)
                {
                    foreach (var materialObject in materialObjects)
                        if (materialObject is PackObject material)
                            materials.Add(ReadMaterial(material));
                }
            }
        }

        var geometryRoot = Root(document, "ModelFileGeometry");
        if (geometryRoot is not null)
        {
            var meshObjects = Array(geometryRoot, "meshes");
            if (meshObjects is not null)
            {
                foreach (var meshObject in meshObjects)
                    if (meshObject is PackObject mesh && TryReadMesh(mesh, out var parsed))
                        meshes.Add(parsed);
            }
        }

        return new(meshes, materials, boundingSphere)
        {
            PermutationTokens = permutationTokens,
            BoneOffsets = modelRoot is null ? [] : ReadBoneOffsets(modelRoot),
            EffectLights = modelRoot is null ? [] : ReadEffectLights(modelRoot),
            EffectWinds = modelRoot is null ? [] : ReadEffectWinds(modelRoot),
            Obstacles = modelRoot is null ? [] : ReadObstacles(modelRoot),
            Cloths = modelRoot is null ? [] : Gw2ModelCloth.ParseAll(modelRoot),
            Lightning = modelRoot is null ? null : Gw2ModelLightning.Parse(modelRoot),
            LodOverride = (modelRoot?.Fields.GetValueOrDefault("lodOverride") as object?[] ?? []).Select(value => value is float single ? single : 0f).ToArray(),
            SoundScript = modelRoot is null ? default : Reference(modelRoot, "soundScript"),
        };
    }

    private static IReadOnlyList<Gw2ModelBoneOffset> ReadBoneOffsets(PackObject modelRoot) =>
        PackValue.AsObjects(modelRoot.Fields.GetValueOrDefault("boneOffsetData"))
            .Select(offset => new Gw2ModelBoneOffset(
                PackValue.RequireU64(offset, "bone"),
                Vec3(offset, "translation"),
                AsVec4(offset.Fields.GetValueOrDefault("rotation")),
                (offset.Fields.GetValueOrDefault("boneInverseOffset") as object?[] ?? []).Select(AsVec4).ToArray()))
            .ToArray();

    private static IReadOnlyList<Gw2ModelEffectLight> ReadEffectLights(PackObject modelRoot) =>
        modelRoot.Fields.GetValueOrDefault("lightData") is not PackObject lightData ? [] :
        PackValue.AsObjects(lightData.Fields.GetValueOrDefault("effectLights"))
            .Select(light => new Gw2ModelEffectLight(
                PackValue.RequireU64(light, "bone"),
                PackValue.AsBytes(light.Fields.GetValueOrDefault("color")),
                PackValue.RequireF32(light, "nearDistance"),
                PackValue.RequireF32(light, "farDistance"),
                PackValue.RequireF32(light, "intensity")))
            .ToArray();

    private static IReadOnlyList<Gw2ModelEffectWind> ReadEffectWinds(PackObject modelRoot) =>
        modelRoot.Fields.GetValueOrDefault("windData") is not PackObject windData ? [] :
        PackValue.AsObjects(windData.Fields.GetValueOrDefault("effectWind"))
            .Select(wind => new Gw2ModelEffectWind(
                PackValue.RequireU64(wind, "bone"),
                PackValue.RequireF32(wind, "nearDistance"),
                PackValue.RequireF32(wind, "farDistance"),
                PackValue.RequireF32(wind, "intensity")))
            .ToArray();

    private static IReadOnlyList<Gw2ModelObstacle> ReadObstacles(PackObject modelRoot) =>
        PackValue.AsObjects(modelRoot.Fields.GetValueOrDefault("obstacles"))
            .Select(obstacle =>
            {
                var transform = obstacle.Fields.GetValueOrDefault("transform") as PackObject;
                return new Gw2ModelObstacle(
                    PackValue.RequireU32(obstacle, "affinity"),
                    PackValue.RequireU64(obstacle, "bone"),
                    (byte)PackValue.RequireU32(obstacle, "type"),
                    (byte)PackValue.RequireU32(obstacle, "response"),
                    PackValue.RequireU32(obstacle, "flags"),
                    PackValue.RequireF32(obstacle, "dragCoef"),
                    PackValue.RequireF32(obstacle, "gravityCoef"),
                    PackValue.RequireF32(obstacle, "length"),
                    PackValue.RequireF32(obstacle, "width"),
                    PackValue.RequireF32(obstacle, "height"),
                    PackValue.RequireF32(obstacle, "radius"),
                    transform is null ? default : AsVec4(transform.Fields.GetValueOrDefault("x")),
                    transform is null ? default : AsVec4(transform.Fields.GetValueOrDefault("y")),
                    transform is null ? default : AsVec4(transform.Fields.GetValueOrDefault("z")));
            })
            .ToArray();

    public IEnumerable<uint> EnumerateDependencies()
    {
        var seen = new HashSet<uint>();
        foreach (var material in Materials)
        {
            if (material.TryGetFileId(out var materialId) && seen.Add(materialId))
                yield return materialId;

            foreach (var texture in material.Textures)
                if (texture.TryGetFileId(out var textureId) && seen.Add(textureId))
                    yield return textureId;
        }
    }

    private static PackObject? Root(Gw2AssetDocument document, string prefix)
    {
        foreach (var chunk in document.Chunks)
            if (chunk.Root is { } root && root.TypeName.StartsWith(prefix, StringComparison.Ordinal))
                return root;
        return null;
    }

    private static Gw2ModelMaterial ReadMaterial(PackObject material)
    {
        var textures = new List<Gw2ModelTextureReference>();
        var textureObjects = Array(material, "textures");
        if (textureObjects is not null)
        {
            foreach (var textureObject in textureObjects)
                if (textureObject is PackObject texture)
                    textures.Add(new(
                        PackValue.AsU32(texture.Fields.GetValueOrDefault("textureFlags")),
                        PackValue.AsU64(texture.Fields.GetValueOrDefault("token")),
                        Reference(texture, "filename"),
                        (byte)PackValue.AsU32(texture.Fields.GetValueOrDefault("uvPSInputIndex")),
                        PackValue.RequireU64(texture, "blitId"),
                        PackValue.RequireU32(texture, "uvAnimId")));
        }

        return new(PackValue.AsU32(material.Fields.GetValueOrDefault("materialId")), Reference(material, "filename"), textures, ReadConstants(material), PackValue.AsU32(material.Fields.GetValueOrDefault("materialFlags")))
        {
            Token = PackValue.RequireU64(material, "token"),
            SortOrder = PackValue.RequireU32(material, "sortOrder"),
            SortLayer = PackValue.OptionalU32(material, "sortLayer") ?? 0,
            TexCoordCount = (byte)PackValue.RequireU32(material, "texCoordCount"),
            ConstantLinks = PackValue.AsObjects(material.Fields.GetValueOrDefault("matConstLinks"))
                .Select(link => new Gw2ModelMaterialConstantLink(
                    PackValue.RequireU64(link, "linkToken"),
                    PackValue.RequireU32(link, "constantToken")))
                .ToArray(),
            UvTransformLinks = PackValue.AsObjects(material.Fields.GetValueOrDefault("uvTransLinks"))
                .Select(link => new Gw2ModelUvTransformLink(
                    PackValue.RequireU64(link, "linkToken"),
                    PackValue.RequireU32(link, "uvAnimId"),
                    (byte)PackValue.RequireU32(link, "type")))
                .ToArray(),
            TextureTransforms = PackValue.AsObjects(material.Fields.GetValueOrDefault("texTransforms"))
                .Select(transform => new Gw2ModelTextureTransform(
                    PackValue.RequireU32(transform, "flags"),
                    (byte)PackValue.RequireU32(transform, "uvIndex"),
                    (byte)PackValue.RequireU32(transform, "columns"),
                    (byte)PackValue.RequireU32(transform, "rows"),
                    (ushort)PackValue.RequireU32(transform, "count"),
                    PackValue.RequireF32(transform, "fps"),
                    Vec2(transform, "scroll"),
                    Vec2(transform, "scrollFreq"),
                    Vec2(transform, "scale"),
                    Vec2(transform, "scaleFreq"),
                    PackValue.RequireF32(transform, "rotate"),
                    PackValue.OptionalF32(transform, "rotate#2") ?? 0f))
                .ToArray(),
        };
    }

    private static IReadOnlyList<Gw2ModelMaterialConstant> ReadConstants(PackObject material)
    {
        var constantObjects = Array(material, "constants");
        if (constantObjects is null || constantObjects.Length == 0) return [];

        var constants = new List<Gw2ModelMaterialConstant>(constantObjects.Length);
        foreach (var constantObject in constantObjects)
            if (constantObject is PackObject constant)
                constants.Add(new(
                    PackValue.AsU32(constant.Fields.GetValueOrDefault("name")),
                    constant.Fields.GetValueOrDefault("value") is float[] value ? value : [],
                    PackValue.AsU32(constant.Fields.GetValueOrDefault("constantFlags"))));
        return constants;
    }

    private static bool TryReadMesh(PackObject mesh, out Gw2ModelMesh parsed)
    {
        parsed = null!;

        var geometry = Object(mesh, "geometry");
        var vertexData = geometry is null ? null : Object(geometry, "verts");
        var vertexMesh = vertexData is null ? null : Object(vertexData, "mesh");
        if (geometry is null || vertexData is null || vertexMesh is null) return false;

        var blob = Blob(vertexMesh, "vertices");
        if (blob.Length == 0) return false;

        Gw2VertexBuffer vertices;
        try
        {
            vertices = new(PackValue.AsU32(vertexMesh.Fields.GetValueOrDefault("fvf")),
                PackValue.AsU32(vertexData.Fields.GetValueOrDefault("vertexCount")), blob);
        }
        catch (Exception ex) when (ex is InvalidDataException or Gw2PackReadLimitExceededException or NotSupportedException or OverflowException)
        {
            return false;
        }

        var indexData = Object(geometry, "indices");
        var indices = indexData is null ? [] : ReadIndices(indexData);
        var lods = ReadLods(geometry);

        parsed = new(
            PackValue.AsU64(mesh.Fields.GetValueOrDefault("meshName")),
            (int)Math.Min(PackValue.AsU32(mesh.Fields.GetValueOrDefault("materialIndex")), int.MaxValue),
            vertices,
            indices,
            lods,
            PackValue.AsU64(mesh.Fields.GetValueOrDefault("visBone")),
            PackValue.AsU32(mesh.Fields.GetValueOrDefault("flags")),
            U32Array(mesh, "seamVertIndices"),
            Vec3(mesh, "minBound"),
            Vec3(mesh, "maxBound"),
            ReadBounds(mesh),
            mesh.Fields.GetValueOrDefault("materialName") as string,
            U64Array(mesh, "boneBindings"),
            U32Array(geometry, "transforms"),
            ReadMorphTargets(mesh));
        return true;
    }

    private static IReadOnlyList<Gw2ModelMeshBounds> ReadBounds(PackObject mesh)
    {
        var boundObjects = Array(mesh, "bounds");
        if (boundObjects is null || boundObjects.Length == 0) return [];

        var bounds = new List<Gw2ModelMeshBounds>(boundObjects.Length);
        foreach (var boundObject in boundObjects)
            if (boundObject is PackObject bound)
                bounds.Add(new(
                    Vec3(bound, "center"),
                    Vec3(bound, "boxExtent"),
                    AsF32(bound.Fields.GetValueOrDefault("sphereRadius"))));
        return bounds;
    }

    private static IReadOnlyList<Gw2ModelMorphTarget> ReadMorphTargets(PackObject mesh)
    {
        var targetObjects = Array(mesh, "morphTargets");
        if (targetObjects is null || targetObjects.Length == 0) return [];

        var targets = new List<Gw2ModelMorphTarget>(targetObjects.Length);
        foreach (var targetObject in targetObjects)
            if (targetObject is PackObject target)
                targets.Add(new(
                    ReadMorphVerts(target, "positions"),
                    ReadMorphVerts(target, "normals"),
                    PackValue.AsU64(target.Fields.GetValueOrDefault("mesh"))));
        return targets;
    }

    private static IReadOnlyList<Gw2ModelMorphVertex> ReadMorphVerts(PackObject target, string name)
    {
        var vertObjects = Array(target, name);
        if (vertObjects is null || vertObjects.Length == 0) return [];

        var verts = new List<Gw2ModelMorphVertex>(vertObjects.Length);
        foreach (var vertObject in vertObjects)
            if (vertObject is PackObject vert)
                verts.Add(new(
                    (ushort)PackValue.AsU32(vert.Fields.GetValueOrDefault("index")),
                    Vec3(vert, "vector")));
        return verts;
    }

    private static IReadOnlyList<uint> U32Array(PackObject obj, string name)
    {
        var values = Array(obj, name);
        if (values is null || values.Length == 0) return [];

        var result = new List<uint>(values.Length);
        foreach (var value in values) result.Add(PackValue.AsU32(value));
        return result;
    }

    private static IReadOnlyList<ulong> U64Array(PackObject obj, string name)
    {
        var values = Array(obj, name);
        if (values is null || values.Length == 0) return [];

        var result = new List<ulong>(values.Length);
        foreach (var value in values) result.Add(PackValue.AsU64(value));
        return result;
    }

    private static IReadOnlyList<IReadOnlyList<ushort>> ReadLods(PackObject geometry)
    {
        var lodObjects = Array(geometry, "lods");
        if (lodObjects is null || lodObjects.Length == 0) return [];

        var lods = new List<IReadOnlyList<ushort>>(lodObjects.Length);
        foreach (var lodObject in lodObjects)
            if (lodObject is PackObject lod)
                lods.Add(ReadIndices(lod));
        return lods;
    }

    private static IReadOnlyList<ushort> ReadIndices(PackObject indexData)
    {
        var values = Array(indexData, "indices");
        if (values is null) return [];

        var indices = new ushort[values.Length];
        for (var i = 0; i < indices.Length; i++) indices[i] = (ushort)PackValue.AsU32(values[i]);
        return indices;
    }

    private static PackObject? Object(PackObject obj, string name) =>
        obj.Fields.TryGetValue(name, out var value) ? value as PackObject : null;

    private static object?[]? Array(PackObject obj, string name) =>
        obj.Fields.TryGetValue(name, out var value) ? value as object?[] : null;

    private static ReadOnlyMemory<byte> Blob(PackObject obj, string name) =>
        obj.Fields.TryGetValue(name, out var value) && value is ReadOnlyMemory<byte> bytes ? bytes : default;

    private static PackFileReference Reference(PackObject obj, string name) =>
        obj.Fields.TryGetValue(name, out var value) && value is PackFileReference reference ? reference : default;

    private static float AsF32(object? value) => value switch
    {
        float single => single,
        double wide => (float)wide,
        uint unsigned => unsigned,
        int signed => signed,
        byte byteValue => byteValue,
        _ => 0,
    };

    private static Vector4 AsVec4(object? value) =>
        value is float[] values && values.Length >= 4 ? new Vector4(values[0], values[1], values[2], values[3]) : Vector4.Zero;

    private static Vector2 Vec2(PackObject obj, string name) =>
        obj.Fields.TryGetValue(name, out var value) && value is float[] values && values.Length >= 2
            ? new Vector2(values[0], values[1])
            : Vector2.Zero;

    private static Vector3 Vec3(PackObject obj, string name) =>
        obj.Fields.TryGetValue(name, out var value) && value is float[] values && values.Length >= 3
            ? new Vector3(values[0], values[1], values[2])
            : Vector3.Zero;
}
