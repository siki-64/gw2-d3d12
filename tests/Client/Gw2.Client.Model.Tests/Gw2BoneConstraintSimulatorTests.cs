using Gw2.Formats;
using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Model.Tests;

[TestClass]
public sealed class Gw2BoneConstraintSimulatorTests
{
    private const uint BoneConstraintModelFileId = 2467602;
    private const float FrameSeconds = 1f / 60f;

    [TestMethod]
    public void LinkDirectionPointsDownTheNegativeYAxisAtZeroAngles()
    {
        var link = new Gw2ModelBoneConstraintLink(0f, 0f, Vector2.Zero, 0);
        var direction = Gw2BoneConstraintSimulator.LinkDirection(link, Matrix4x4.Identity);
        Assert.AreEqual(0f, direction.X, 1e-6f);
        Assert.AreEqual(-1f, direction.Y, 1e-6f);
        Assert.AreEqual(0f, direction.Z, 1e-6f);
        var raised = Gw2BoneConstraintSimulator.LinkDirection(link with { Angle = 90f }, Matrix4x4.Identity);
        Assert.AreEqual(-1f, raised.Z, 1e-6f);
        var turned = Gw2BoneConstraintSimulator.LinkDirection(link with { Azimuth = 90f }, Matrix4x4.Identity);
        Assert.AreEqual(1f, turned.X, 1e-6f);
    }

    [TestMethod]
    public void PlanarAngleLimitKeepsTheDirectionInItsPlane()
    {
        var constraint = new Gw2ModelBoneConstraint(0, Gw2BoneConstraintSimulator.AngleLimitFlag | Gw2BoneConstraintSimulator.PlanarAngleLimitFlag, 0f, 0f, 0f, 0f, 0f, Array.Empty<Gw2ModelBoneConstraintLink>())
        {
            Angle = new Vector2(MathF.PI, MathF.PI),
        };
        var limited = Gw2BoneConstraintSimulator.LimitAngle(constraint, Vector3.UnitX, Vector3.Normalize(Vector3.One), Vector3.UnitZ, Vector3.UnitZ, FrameSeconds);
        Assert.AreEqual(0f, limited.Y, 1e-5f);
        Assert.AreEqual(1f, limited.Length(), 1e-5f);
    }

    [TestMethod]
    public void ModelSpeedAboveOneRaisesTheBlendUnlessOptedOut()
    {
        var constraint = new Gw2ModelBoneConstraint(0, 0, 0f, 0.25f, 0f, 0f, 0f, Array.Empty<Gw2ModelBoneConstraintLink>());
        Assert.AreEqual(0.25f, Gw2BoneConstraintSimulator.AnimBlendOf(constraint, 1f));
        Assert.AreEqual(0.5f, Gw2BoneConstraintSimulator.AnimBlendOf(constraint, 1.5f), 1e-6f);
        Assert.AreEqual(0.25f, Gw2BoneConstraintSimulator.AnimBlendOf(constraint with { Flags = Gw2BoneConstraintSimulator.IgnoresModelSpeedFlag }, 1.5f));
    }

    [TestMethod]
    public void MotionFollowedDependsOnTheMotionFlags()
    {
        var constraint = new Gw2ModelBoneConstraint(0, 0, 0f, 0f, 0f, 0f, 0f, Array.Empty<Gw2ModelBoneConstraintLink>());
        Assert.AreEqual(0f, Gw2BoneConstraintSimulator.MotionFollowed(constraint, 0.3f));
        Assert.AreEqual(1f, Gw2BoneConstraintSimulator.MotionFollowed(constraint with { Flags = Gw2BoneConstraintSimulator.FollowsModelMotionFlag }, 0.3f));
        Assert.AreEqual(0.7f, Gw2BoneConstraintSimulator.MotionFollowed(constraint with { Flags = Gw2BoneConstraintSimulator.FollowsModelMotionByBlendFlag }, 0.3f), 1e-6f);
    }

    [TestMethod]
    public void TwistCorrectionStaysBetweenTheStartAndTheEnd()
    {
        var constraint = new Gw2ModelBoneConstraint(0, Gw2BoneConstraintSimulator.TwistLimitFlag, 0f, 0f, 0f, 0f, 0f, Array.Empty<Gw2ModelBoneConstraintLink>())
        {
            Twist = new Vector2(0.5f, 1f),
            TwistStrength = 2f,
            TwistType = 0,
        };
        Assert.AreEqual(0f, Gw2BoneConstraintSimulator.TwistCorrection(constraint, 0.4f, FrameSeconds));
        Assert.AreEqual(0.25f * 2f * FrameSeconds, Gw2BoneConstraintSimulator.TwistCorrection(constraint, 0.625f, FrameSeconds), 1e-6f);
        Assert.AreEqual(0.5f + 2f * FrameSeconds, Gw2BoneConstraintSimulator.TwistCorrection(constraint, 1.5f, FrameSeconds), 1e-6f);
        Assert.AreEqual(1.5f, Gw2BoneConstraintSimulator.TwistCorrection(constraint, 2f, 10f), 1e-6f);
    }

    [TestMethod]
    public void TwistAxisIsPerpendicularToTheBindOffset()
    {
        foreach (var offset in new[] { new Vector3(3f, 1f, 2f), new Vector3(0f, 0f, 5f) })
        {
            var axis = Gw2BoneConstraintSimulator.TwistAxisOf(offset);
            Assert.AreEqual(0f, Vector3.Dot(axis, offset), 1e-5f);
            Assert.IsTrue(axis.LengthSquared() > 0.5f);
        }
    }

    [TestMethod]
    public void LinkTotalIsClampedToTheLargestSingleCorrection()
    {
        var corrections = new[] { new Vector3(2f, 0f, 0f), new Vector3(2f, 0f, 0f) };
        var total = Gw2BoneConstraintSimulator.ClampLinkTotal(new Vector3(4f, 0f, 0f), corrections);
        Assert.AreEqual(2f, total.X, 1e-5f);
        var single = Gw2BoneConstraintSimulator.ClampLinkTotal(new Vector3(1f, 0f, 0f), new[] { new Vector3(1f, 0f, 0f) });
        Assert.AreEqual(1f, single.X, 1e-5f);
    }

    [TestMethod]
    public void CurvesShapeTheRatioLikeTheExecutable()
    {
        Assert.AreEqual(0.25f, Gw2BoneConstraintCurve.Shape(0, 0.25f));
        Assert.AreEqual(1f, Gw2BoneConstraintCurve.Shape(1, 0.25f));
        Assert.AreEqual(0.5f, Gw2BoneConstraintCurve.Shape(2, 0.25f));
        Assert.AreEqual(0.0625f, Gw2BoneConstraintCurve.Shape(3, 0.25f));
        Assert.AreEqual(0.15625f, Gw2BoneConstraintCurve.Shape(4, 0.25f), 1e-6f);
        Assert.AreEqual(1f, Gw2BoneConstraintCurve.Shape(9, 0.25f));
    }

    [TestMethod]
    public void ShortestSlerpTakesTheShorterArc()
    {
        var quarterTurn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);
        var halfway = Gw2BoneConstraintCurve.ShortestSlerp(Quaternion.Identity, Quaternion.Negate(quarterTurn), 0.5f);
        var expected = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 4f);
        Assert.AreEqual(1f, MathF.Abs(Quaternion.Dot(halfway, expected)), 1e-5f);
    }

    [TestMethod]
    public void AngleLimitPullsBackBeyondTheOuterAngle()
    {
        var constraint = new Gw2ModelBoneConstraint(0, 1, 0, 0, 0, 0, 0, []) { Angle = new Vector2(0.1f, 0.3f), AngleStrength = 0f, AngleType = 1 };
        var limited = Gw2BoneConstraintSimulator.LimitAngle(constraint, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, Vector3.UnitZ, FrameSeconds);
        Assert.AreEqual(0.3f, MathF.Acos(Vector3.Dot(limited, Vector3.UnitX)), 1e-4f);
    }

    [TestMethod]
    public void EllipticalAngleLimitWidensAcrossTheTangent()
    {
        var constraint = new Gw2ModelBoneConstraint(0, 9, 0, 0, 0, 0, 0, []) { Angle = new Vector2(0.1f, 0.3f), AngleStrength = 0f, AngleType = 1, EllipseRatio = 2f };
        var alongTangent = Gw2BoneConstraintSimulator.LimitAngle(constraint, Vector3.UnitX, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, FrameSeconds);
        var acrossTangent = Gw2BoneConstraintSimulator.LimitAngle(constraint, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, Vector3.UnitZ, FrameSeconds);
        Assert.AreEqual(0.3f, MathF.Acos(Vector3.Dot(alongTangent, Vector3.UnitX)), 1e-4f);
        Assert.AreEqual(0.6f, MathF.Acos(Vector3.Dot(acrossTangent, Vector3.UnitX)), 1e-4f);
    }

    [TestMethod]
    public void DistanceLimitKeepsTheStretchWithinTheLimit()
    {
        var constraint = new Gw2ModelBoneConstraint(0, 4, 0, 0, 0, 0, 0, [])
        {
            DistanceInner = new Vector2(1f, 2f), DistanceInnerStrength = 0f, DistanceInnerType = 1,
            DistanceOuter = new Vector2(1f, 3f), DistanceOuterStrength = 0f, DistanceOuterType = 1,
        };
        Assert.AreEqual(8f, Gw2BoneConstraintSimulator.LimitDistance(constraint, 10f, 5f, FrameSeconds), 1e-5f);
        Assert.AreEqual(13f, Gw2BoneConstraintSimulator.LimitDistance(constraint, 10f, 20f, FrameSeconds), 1e-5f);
        Assert.AreEqual(10.5f, Gw2BoneConstraintSimulator.LimitDistance(constraint, 10f, 10.5f, FrameSeconds), 1e-5f);
    }

    [TestMethod]
    public void ConstrainedBonesSettleAndStayInsideTheirConesWhenConfigured()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || !File.Exists(dat)) Assert.Inconclusive("Gw2.dat is not installed.");

        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var skeleton = reader.ReadModelSkeleton(BoneConstraintModelFileId)!;
        var simulator = new Gw2BoneConstraintSimulator(skeleton);
        Assert.AreEqual(185, simulator.ConstrainedBones.Count);

        var constraintByBone = simulator.ConstrainedBones
            .Zip(skeleton.BoneConstraints)
            .ToDictionary(pair => pair.First, pair => pair.Second);
        var largestAngle = 0f;
        var root = skeleton.Bones.ToList().FindIndex(bone => bone.Name == "bone:COG");
        for (var frame = 0; frame < 240; frame++)
        {
            var pose = Gw2SkeletonPose.LocalPoseAt(skeleton, null, 0f);
            pose.Orientations[root] = Quaternion.Concatenate(pose.Orientations[root], Quaternion.CreateFromAxisAngle(Vector3.UnitZ, frame * 0.2f));
            simulator.Step(pose, FrameSeconds);

            foreach (var (bone, constraint) in constraintByBone)
            {
                var animated = Vector3.Normalize(skeleton.Bones[bone].Position);
                var simulated = pose.Positions[bone];
                Assert.IsTrue(float.IsFinite(simulated.X) && float.IsFinite(simulated.Y) && float.IsFinite(simulated.Z), skeleton.Bones[bone].Name);
                Assert.AreEqual(skeleton.Bones[bone].Position.Length(), simulated.Length(), 1e-3f, skeleton.Bones[bone].Name);
                var angle = MathF.Acos(Math.Clamp(Vector3.Dot(animated, Vector3.Normalize(simulated)), -1f, 1f));
                largestAngle = MathF.Max(largestAngle, angle);
                Assert.IsTrue(angle <= constraint.Angle.Y * (1f - constraint.AnimBlend) + 1e-3f, $"{skeleton.Bones[bone].Name} {angle} {constraint.Angle.Y}");
            }
        }
        Assert.IsTrue(largestAngle > 0.01f, largestAngle.ToString());
    }
}
