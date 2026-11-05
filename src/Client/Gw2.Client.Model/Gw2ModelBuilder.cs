using System.Numerics;
using Gw2.Dat.Content;
using Gw2.Dat.Archive;
using Gw2.Formats;
using DatCollisionBox = Gw2.Dat.Content.Gw2CollisionBox;
using DatCollisionCapsule = Gw2.Dat.Content.Gw2CollisionCapsule;
using DatCollisionMesh = Gw2.Dat.Content.Gw2ModelCollisionMesh;
using DatCollisionSphere = Gw2.Dat.Content.Gw2CollisionSphere;
using DatModel = Gw2.Dat.Content.Gw2Model;
using DatModelCollision = Gw2.Dat.Content.Gw2ModelCollision;
using DatModelMaterial = Gw2.Dat.Content.Gw2ModelMaterial;
using DatModelTextureReference = Gw2.Dat.Content.Gw2ModelTextureReference;

namespace Gw2.Client.Model;

public static class Gw2ModelBuilder
{
    private const uint TwoSidedMaterialFlag = 0x1;
    private const uint AlphaMaterialFlags = 0x1 | 0x100;

    public static Gw2Model Build(
        uint baseId,
        DatModel parserModel,
        Func<uint, Gw2AmatMaterial?> resolveMaterial,
        DatModelCollision? collision = null)
    {
        if (parserModel is null) throw new ArgumentNullException(nameof(parserModel));
        resolveMaterial ??= _ => null;

        return new Gw2Model
        {
            BaseId = baseId,
            Meshes = parserModel.Meshes.Select(Gw2VertexDecoder.Decode).Concat(parserModel.Cloths.Select(cloth => Gw2ClothMesh.Decode(cloth, MaterialNameOf(parserModel, cloth.MaterialIndex)))).ToArray(),
            Materials = BuildMaterials(parserModel, resolveMaterial),
            Collision = BuildCollision(collision),
        };
    }

    private static string? MaterialNameOf(DatModel model, uint materialIndex) =>
        model.Meshes.FirstOrDefault(mesh => mesh.MaterialIndex == materialIndex)?.MaterialName;

    private static IReadOnlyList<Gw2Material> BuildMaterials(
        DatModel model,
        Func<uint, Gw2AmatMaterial?> resolveMaterial)
    {
        var materials = new List<Gw2Material>(model.Materials.Count);
        foreach (var material in model.Materials)
        {
            var materialFileId = material.TryGetFileId(out var id) ? id : (uint?)null;
            var amat = materialFileId is uint fileId ? resolveMaterial(fileId) : null;

            var textures = new List<Gw2MaterialTexture>(material.Textures.Count);
            for (var index = 0; index < material.Textures.Count; index++)
            {
                var texture = material.Textures[index];
                var token = ResolveToken(texture, index, amat);
                var roleToken = amat is not null && index < amat.TextureTokens.Count ? amat.TextureTokens[index] : token;

                var textureFileId = texture.TryGetFileId(out var textureId) ? textureId : (uint?)null;

                var role = Gw2MaterialTokens.RoleOf(roleToken);
                if (role == "unknown") role = ShaderRole(amat, index) ?? role;

                var (textureSlot, stateIndex, samplerName) = ResolveSampler(amat, roleToken);

                textures.Add(new(
                    index,
                    token,
                    role,
                    textureFileId,
                    textureSlot,
                    stateIndex,
                    samplerName,
                    texture.UvPSInputIndex,
                    texture.BlitId,
                    texture.UvAnimId));
            }

            var constants = material.Constants?
                .Select(constant => new Gw2MaterialConstant(constant.Token, constant.Value.ToArray(), constant.Flags))
                .ToList() ?? new List<Gw2MaterialConstant>();

            var techniques = amat?.Techniques
                .Select(technique => new Gw2MaterialTechnique(technique.QualityName, technique.Quality))
                .ToList() ?? new List<Gw2MaterialTechnique>();

            var renderState = ResolveRenderState(amat);
            var alphaTested = IsAlphaTested(amat) && HasCutoutFlag(material.MaterialFlags);

            materials.Add(new Gw2Material
            {
                MaterialId = material.MaterialId,
                MaterialFileId = materialFileId,
                Textures = textures,
                Constants = constants,
                AlphaTested = alphaTested,
                Techniques = techniques,
                Quality = amat?.TechniqueFor(Gw2ShaderQuality.High)?.QualityName,
                MaterialFlags = material.MaterialFlags,
                SortOrder = material.SortOrder,
                TwoSided = (material.MaterialFlags & TwoSidedMaterialFlag) != 0,
                RenderState = renderState,
                Surface = Gw2RenderState.SurfaceClass(renderState, alphaTested),
                Blend = Gw2RenderState.BlendMode(renderState).ToString(),
                TextureTransforms = material.TextureTransforms,
            });
        }

        return materials;
    }

    private static Gw2ModelCollision? BuildCollision(DatModelCollision? collision)
    {
        if (collision is null || !collision.HasCollision) return null;

        return new Gw2ModelCollision
        {
            Meshes = collision.Meshes
                .Select(mesh => new Gw2CollisionMesh(
                    mesh.Vertices,
                    mesh.Indices,
                    mesh.Surfaces.Select(surface => (ushort)surface).ToArray()))
                .ToArray(),
            Boxes = collision.Boxes
                .Select(box => new Gw2CollisionBox(
                    box.Dimensions,
                    box.Position,
                    new Vector4(
                        box.Rotation.Length > 0 ? box.Rotation[0] : 0f,
                        box.Rotation.Length > 1 ? box.Rotation[1] : 0f,
                        box.Rotation.Length > 2 ? box.Rotation[2] : 0f,
                        box.Rotation.Length > 3 ? box.Rotation[3] : 0f),
                    box.Surface))
                .ToArray(),
            Spheres = collision.Spheres
                .Select(sphere => new Gw2CollisionSphere(sphere.Radius, sphere.Position, sphere.Surface))
                .ToArray(),
            Capsules = collision.Capsules
                .Select(capsule => new Gw2CollisionCapsule(capsule.P0, capsule.P1, capsule.Radius, capsule.Surface))
                .ToArray(),
        };
    }

    private static ulong ResolveToken(DatModelTextureReference texture, int index, Gw2AmatMaterial? amat)
    {
        if (texture.Token != 0) return texture.Token;
        if (amat is not null && index < amat.TextureTokens.Count) return amat.TextureTokens[index];
        return 0;
    }

    private static int GrmtIndexOf(Gw2AmatMaterial amat, uint low32)
    {
        for (var i = 0; i < amat.TextureTokens.Count; i++)
            if ((uint)amat.TextureTokens[i] == low32)
                return i;
        return -1;
    }

    private static (int Slot, int State, string? Name) ResolveSampler(Gw2AmatMaterial? amat, ulong token)
    {
        if (amat is null || token == 0) return (-1, -1, null);
        var grmt = GrmtIndexOf(amat, (uint)token);
        if (grmt < 0) return (-1, -1, null);

        foreach (var shader in amat.Shaders)
        {
            if (!shader.IsPixelShader) continue;
            foreach (var binary in shader.Binaries)
                foreach (var sampler in binary.Samplers)
                    if ((int)sampler.TextureIndex == grmt)
                    {
                        string? name = null;
                        if (binary.Reflection is { } reflection && reflection.TryGetSampler((int)sampler.TextureSlot, out var entry))
                            name = entry.Name;
                        return ((int)sampler.TextureSlot, (int)sampler.StateIndex, name);
                    }
        }

        return (-1, -1, null);
    }

    private static string? ShaderRole(Gw2AmatMaterial? amat, int materialTextureIndex)
    {
        if (amat is null) return null;
        return amat.MaterialRole(materialTextureIndex) switch
        {
            Gw2ShaderResourceRole.Normal => "detailNormal",
            Gw2ShaderResourceRole.BaseColor => "diffuse",
            _ => null,
        };
    }

    private static ulong ResolveRenderState(Gw2AmatMaterial? amat)
    {
        if (amat is null) return 0;

        for (var technique = 0; technique < amat.Techniques.Count; technique++)
        {
            var passes = amat.Techniques[technique].Passes;
            for (var pass = 0; pass < passes.Count; pass++)
                if (amat.TryResolveDefaultEffect(technique, pass, out var selection))
                    return selection.Effect.RenderState;
        }

        foreach (var technique in amat.Techniques)
            foreach (var pass in technique.Passes)
                if (pass.Effects.Count > 0)
                    return pass.Effects[0].RenderState;

        return 0;
    }

    private static bool HasCutoutFlag(uint materialFlags) =>
        (materialFlags & AlphaMaterialFlags) != 0;

    private static bool IsAlphaTested(Gw2AmatMaterial? amat)
    {
        if (amat is null) return false;
        foreach (var shader in amat.Shaders)
        {
            if (!shader.IsPixelShader) continue;
            foreach (var binary in shader.Binaries)
                foreach (var constant in binary.Constants)
                    if (constant.Token == Gw2MaterialConstantTokens.AlphaRef) return true;
        }

        return false;
    }
}
