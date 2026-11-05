using System;
using System.Numerics;
using Gw2.Dat.Content;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2ModelLinkTests
{
    private static Gw2ModelSkeleton ItemWithGrip(Matrix4x4 gripBindWorld)
    {
        Matrix4x4.Invert(gripBindWorld, out var gripInverse);
        return new Gw2ModelSkeleton(
            default,
            [
                new Gw2ModelBone("bone:root", 0xFFFFFFFF, 0, Vector3.Zero, Quaternion.Identity, Matrix4x4.Identity, Matrix4x4.Identity, 0f),
                new Gw2ModelBone("bone:RGripHand", 0, 0, gripBindWorld.Translation, Quaternion.CreateFromRotationMatrix(gripBindWorld), Matrix4x4.Identity, gripInverse, 0f),
            ],
            [], [], [], []);
    }

    [TestMethod]
    public void PutsTheChildGripBoneOnTheParentBone()
    {
        var gripBind = Matrix4x4.CreateRotationZ(0.5f) * Matrix4x4.CreateTranslation(1f, 2f, 3f);
        var socket = Matrix4x4.CreateRotationX(1.1f) * Matrix4x4.CreateTranslation(10f, -4f, 7f);
        var item = ItemWithGrip(gripBind);

        var root = Gw2ModelLink.ChildWorld(socket, item, Token64.Encode("rgriphand"), Gw2ModelLinkFlags.EquipmentAttach, Matrix4x4.Identity);
        var gripWorld = gripBind * root;

        Assert.AreEqual(socket.Translation.X, gripWorld.Translation.X, 1e-4f);
        Assert.AreEqual(socket.Translation.Y, gripWorld.Translation.Y, 1e-4f);
        Assert.AreEqual(socket.Translation.Z, gripWorld.Translation.Z, 1e-4f);
        Assert.AreEqual(socket.M22, gripWorld.M22, 1e-4f);
        Assert.AreEqual(socket.M23, gripWorld.M23, 1e-4f);
    }

    [TestMethod]
    public void UsesTheChildOriginWithoutAGripBone()
    {
        var socket = Matrix4x4.CreateTranslation(5f, 0f, 0f);
        var root = Gw2ModelLink.ChildWorld(socket, ItemWithGrip(Matrix4x4.Identity), 0, Gw2ModelLinkFlags.EquipmentAttach, Matrix4x4.Identity);
        Assert.AreEqual(socket, root);
    }

    [TestMethod]
    public void DropsTheParentRotationWhenAsked()
    {
        var socket = Matrix4x4.CreateRotationY(0.7f) * Matrix4x4.CreateTranslation(1f, 1f, 1f);
        var root = Gw2ModelLink.ChildWorld(socket, ItemWithGrip(Matrix4x4.Identity), 0, Gw2ModelLinkFlags.DontInheritRotation, Matrix4x4.Identity);
        Assert.AreEqual(Matrix4x4.CreateTranslation(1f, 1f, 1f), root);
    }
}
