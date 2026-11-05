using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gw2.Client.Graphics;
using Gw2.Client.Net.Game;
using Gw2.Client.Scene;
using Crystal.Graphics;

namespace Gw2.Client;

public sealed partial class Gw2Client
{
    private const int MaxDrawnPlayerModels = 50;

    private sealed class PlayerModel(PlayerLook look, CharacterScene template)
    {
        public PlayerLook Look { get; } = look;
        public CharacterScene Template { get; } = template;
        public Gw2CharacterAnimator Animator { get; } = new();
        public List<GrSkinnedMesh> Meshes { get; } = [];
        public Matrix4x4[] Palette { get; set; } = [];
    }

    private sealed record PlayerLook(Gw2AppearanceIndices? Appearance, Gw2CharacterArmor Armor, AgentCompositeIdentity? CompositeIdentity);

    private readonly Dictionary<PlayerLook, CharacterScene> _playerModelTemplates = [];
    private readonly Dictionary<PlayerLook, (Texture2D? Atlas, Texture2D? NormalAtlas)> _playerModelAtlases = [];
    private readonly Dictionary<uint, PlayerModel> _playerModels = [];
    private readonly HashSet<uint> _presentPlayerModels = [];

    private void PlacePlayerModels(float elapsedSeconds)
    {
        if (_characterPass is null || _content is null) return;
        var ownAgentId = _login?.ControlledAgentId;
        var nearest = _world.Agents.All
            .Where(agent => agent.AgentId != ownAgentId)
            .Select(agent => (Agent: agent, Look: LookOf(agent.AgentId)))
            .Where(candidate => candidate.Look is not null)
            .Select(candidate => (candidate.Agent, Look: candidate.Look!))
            .OrderBy(candidate => Vector3.DistanceSquared(candidate.Agent.PositionInGameUnits, _characterPosition))
            .Take(MaxDrawnPlayerModels)
            .ToList();

        foreach (var template in _playerModelTemplates.Values) template.Update(elapsedSeconds);

        _presentPlayerModels.Clear();
        foreach (var (agent, look) in nearest)
        {
            _presentPlayerModels.Add(agent.AgentId);
            if (_playerModels.TryGetValue(agent.AgentId, out var model) && model.Look != look)
            {
                RemovePlayerModel(agent.AgentId);
                model = null;
            }
            model ??= _playerModels[agent.AgentId] = new PlayerModel(look, PlayerModelTemplateOf(look));
            if (!model.Template.IsSettled || model.Template.Error is not null || model.Template.Skeleton is not { } skeleton) continue;
            if (model.Meshes.Count == 0) UploadPlayerModel(model, skeleton);
            if (model.Palette.Length != skeleton.Bones.Count) model.Palette = new Matrix4x4[skeleton.Bones.Count];
            var facing = new Vector2(agent.Facing.X, agent.Facing.Y);
            var velocity = new Vector2(agent.VelocityInGameUnits.X, agent.VelocityInGameUnits.Y);
            var motion = new Gw2CharacterMotion(velocity.Length(), facing, velocity);
            var world = Matrix4x4.CreateRotationZ(FacingYawOf(facing) - CharacterModelForwardYaw) * Matrix4x4.CreateTranslation(agent.PositionInGameUnits);
            if (!model.Template.PoseOther(model.Animator, motion, elapsedSeconds, world, model.Palette)) continue;
            var offset = _characterPass.AppendAgentPalette(model.Palette);
            foreach (var mesh in model.Meshes) mesh.Pose(model.Palette, offset, null);
        }

        foreach (var agentId in _playerModels.Keys.Where(agentId => !_presentPlayerModels.Contains(agentId)).ToArray())
            RemovePlayerModel(agentId);
    }

    private PlayerLook? LookOf(uint agentId)
    {
        if (_world.Appearances.AppearanceOf(agentId) is { } block && Gw2AppearanceIndices.Of(block) is { CompositeRaceName: not null } appearance)
            return new PlayerLook(appearance, ArmorOf(agentId), null);
        return _world.CompositeIdentities.IdentityOf(agentId) is { } identity ? new PlayerLook(null, identity.VariantToken is null ? ArmorOf(agentId) : Gw2CharacterArmor.None, identity) : null;
    }

    private bool HasPlayerModel(uint agentId) => _playerModels.TryGetValue(agentId, out var model) && model.Meshes.Count > 0;

    private static float FacingYawOf(Vector2 facing) =>
        facing.X != 0f || facing.Y != 0f ? MathF.Atan2(facing.Y, facing.X) : CharacterModelForwardYaw;

    private CharacterScene PlayerModelTemplateOf(PlayerLook look)
    {
        if (!_playerModelTemplates.TryGetValue(look, out var template))
            _playerModelTemplates[look] = template = look.CompositeIdentity is { } identity
                ? CharacterScene.OfCompositeIdentity(_content!, identity.RaceToken, identity.VariantToken, look.Armor, _highResolutionFiles)
                : CharacterScene.Of(_content!, look.Appearance!, look.Armor, _highResolutionFiles)!;
        return template;
    }

    private void UploadPlayerModel(PlayerModel model, Gw2.Dat.Content.Gw2ModelSkeleton skeleton)
    {
        if (!_playerModelAtlases.TryGetValue(model.Look, out var atlases))
        {
            var template = model.Template;
            atlases = (
                template.Atlas is { } image ? _characterPass!.UploadAtlas(image.Width, image.Height, image.Pixels) : null,
                template.NormalAtlas is { } normals ? _characterPass!.UploadAtlas(normals.Width, normals.Height, normals.Pixels) : null);
            _playerModelAtlases[model.Look] = atlases;
        }
        foreach (var part in model.Template.Parts)
            foreach (var mesh in part.Meshes.Where(mesh => mesh.IsRenderable && mesh.LodLevel == 0))
                model.Meshes.Add(_characterPass!.AddAgentSkinnedMesh(mesh, skeleton, atlases.Atlas, atlases.NormalAtlas));
    }

    private void RemovePlayerModel(uint agentId)
    {
        if (!_playerModels.Remove(agentId, out var model)) return;
        foreach (var mesh in model.Meshes) _characterPass?.RemoveAgentSkinnedMesh(mesh);
    }

    private void ClearPlayerModels()
    {
        foreach (var agentId in _playerModels.Keys.ToArray()) RemovePlayerModel(agentId);
    }
}
