using Gw2.Formats;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Crystal;
using Crystal.Graphics;
using Gw2.Dat.Content;
using Gw2.Client.Graphics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Graphics.Tests;

[TestClass]
public sealed class GrArchiveMaterialPipelineTests
{
    private const uint TerrainMaterialFileId = 0x2D29C;
    private const uint CanonicalPropMaterialFileId = 0x1130E;
    private const uint LeafMaterialFileId = 0xE106;

    [TestMethod]
    [DataRow(TerrainMaterialFileId, false, false)]
    [DataRow(CanonicalPropMaterialFileId, false, false)]
    [DataRow(CanonicalPropMaterialFileId, true, false)]
    [DataRow(LeafMaterialFileId, false, false)]
    [DataRow(LeafMaterialFileId, true, true)]
    public void ArchiveMaterialBuildsProgramsAndPipelinesOnWarpUnderTheDebugLayer(uint materialFileId, bool instanced, bool gpuBasedValidation)
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var material = reader.ReadMaterial(materialFileId)!;
        var vertexLayout = materialFileId == TerrainMaterialFileId ? GrVertexTerrain.Layout : instanced ? GrVertexStatic.WithInstances : GrVertexStatic.WithExtraUvs;
        var vertexShaderVariant = instanced ? GrArchiveShaderMaterial.InstancedVertexShaderVariant : GrArchiveShaderMaterial.StaticVertexShaderVariant;

        var pipelineCount = 0;
        GrWarpGame.RunOnce(graphicsDevice =>
        {
            using var device = new GrDevice(graphicsDevice);
            using var color = device.CreateRenderTarget(64, 64, SurfaceFormat.Rgba8Unorm, "PipelineTestColor");
            using var depth = device.CreateDepthTarget(64, 64, "PipelineTestDepth");
            device.SetRenderTarget(color, depth);
            foreach (var effectKey in new[] { Gw2AmatEffectKeys.Default, Gw2AmatEffectKeys.NormalSpecDepth })
            {
                var archiveMaterial = GrArchiveShaderMaterial.Create(device, material, effectKey, vertexShaderVariant, Gw2ShaderQuality.High);
                Assert.IsNotNull(archiveMaterial, $"effect {effectKey:X}");
                foreach (var pass in new[] { archiveMaterial }.Concat(archiveMaterial.LaterPasses))
                {
                    var drawState = pass.DrawState;
                    device.Pipeline(
                        pass.Program,
                        GrBlendStates.From(pass.RenderState, drawState.ColorWrite),
                        GrDrawStates.DepthStencil(drawState),
                        GrDrawStates.WithDepthBias(RasterizerState.CullNone, drawState),
                        vertexLayout);
                    pipelineCount++;
                }
            }
        }, gpuBasedValidation);

        Assert.IsGreaterThanOrEqualTo(2, pipelineCount);
    }

    private sealed class GrWarpGame : Game
    {
        private readonly Action<GraphicsDevice> _draw;
        private readonly List<GraphicsDebugMessage> _debugErrors = [];
        private int _drawCount;

        private GrWarpGame(Action<GraphicsDevice> draw, bool gpuBasedValidation)
            : base(new GameOptions
            {
                Title = "Gw2.Client.Graphics.Tests",
                Width = 64,
                Height = 64,
                StartHidden = true,
                IsFixedTimeStep = false,
                Graphics = new GraphicsDeviceOptions
                {
                    VSync = false,
                    AdapterPreference = GraphicsAdapterPreference.Software,
                    EnableDebugLayer = true,
                    EnableGpuBasedValidation = gpuBasedValidation,
                },
            })
        {
            _draw = draw;
            GraphicsDevice.DebugMessageReceived += (_, message) =>
            {
                if (message.IsError) _debugErrors.Add(message);
            };
        }

        public static void RunOnce(Action<GraphicsDevice> draw, bool gpuBasedValidation)
        {
            var game = new GrWarpGame(draw, gpuBasedValidation);
            try
            {
                game.Run();
            }
            finally
            {
                game.Dispose();
            }
            Assert.AreEqual(1, game._drawCount);
            Assert.IsEmpty(game._debugErrors, string.Join(Environment.NewLine, game._debugErrors));
        }

        protected override void Draw(GameTime gameTime)
        {
            _draw(GraphicsDevice);
            _drawCount++;
            Exit();
        }
    }
}
