using System;
using System.Linq;
using Gw2.Dat.Content;
using Gw2.Formats;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class ContentFunctionsTests
{
    private const uint DodgeInputId = 6;
    private const byte DodgeSlot = 2;

    [TestMethod]
    public void DecodesTheSpecialDodgeBindingsAndTheVindicatorDodgeProgram()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || string.IsNullOrEmpty(Gw2Installation.ExecutablePath())) Assert.Inconclusive("Guild Wars 2 is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var functions = new Gw2ContentFunctions(reader.ContentIndex, ContentFunctionContentLayout.Current, SkillPaletteContentLayout.Current.SkillContentType);

        foreach (var (binding, function) in new (uint, uint)[] { (35, 204), (33, 71), (19, 458), (16, 531), (25, 190) })
            Assert.AreEqual(new ContentFunctionBinding(binding, DodgeInputId, function, DodgeSlot), functions.Binding(binding));
        Assert.AreEqual(4u, ContentFunctionContentLayout.Current.ServerBindingSource);

        var vindicator = functions.Program(71)!;
        Assert.AreEqual(20, vindicator.Nodes.Count);
        var landCheck = (ContentFunctionConditionalNode)vindicator.Nodes[5];
        Assert.AreEqual(new ContentFunctionCondition(ContentFunctionConditionType.Locale, false, LocaleKind: 4), landCheck.Condition);
        Assert.AreEqual(159u, landCheck.ThenFunctionUid);
        Assert.AreEqual(303u, landCheck.ElseFunctionUid);
        Assert.AreEqual(600u, ((ContentFunctionWaitNode)vindicator.Nodes[10]).Milliseconds);
        Assert.AreEqual(178u, ((ContentFunctionRunStackNode)vindicator.Nodes[18]).FunctionUid);
        var buffCheck = (ContentFunctionConditionalNode)vindicator.Nodes[19];
        Assert.AreEqual(new ContentFunctionCondition(ContentFunctionConditionType.HasBuff, false, BuffDataId: 65226, BuffOnSelf: true), buffCheck.Condition);
        Assert.AreEqual(695u, buffCheck.ThenFunctionUid);
        Assert.IsNull(buffCheck.ElseFunctionUid);

        var landing = functions.Program(178)!;
        var waitForLeap = (ContentFunctionWaitForConditionNode)landing.Nodes[0];
        Assert.AreEqual(new ContentFunctionCondition(ContentFunctionConditionType.HasMovementModifier, false, MovementModifierUid: 278), waitForLeap.Condition);
        Assert.AreEqual(2000u, waitForLeap.TimeoutMilliseconds);
        Assert.IsTrue(((ContentFunctionConditionalNode)landing.Nodes[1]).Condition.Negated);
        Assert.AreEqual(278u, ((ContentFunctionRemoveMovementModifierNode)landing.Nodes[2]).MovementModifierUid);

        Assert.IsFalse(functions.Program(204)!.Nodes.Any(node => node is ContentFunctionConditionalNode));
        }

    [TestMethod]
    public void DecodesMovementModifierApplicationsAcrossAllPrograms()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || string.IsNullOrEmpty(Gw2Installation.ExecutablePath())) Assert.Inconclusive("Guild Wars 2 is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var functions = new Gw2ContentFunctions(reader.ContentIndex, ContentFunctionContentLayout.Current, SkillPaletteContentLayout.Current.SkillContentType);
        var nodes = Enumerable.Range(0, 1000).Select(uid => functions.Program((uint)uid)).OfType<ContentFunctionProgram>().SelectMany(program => program.Nodes).ToList();

        var applied = nodes.OfType<ContentFunctionApplyMovementModifierNode>().ToList();
        Assert.IsTrue(applied.Any(node => node.DurationMilliseconds is null));
        Assert.IsTrue(applied.Any(node => node.DurationMilliseconds > 0));
        Assert.IsTrue(applied.All(node => node.MovementModifierUid != 0));
        Assert.IsFalse(nodes.Any(node => node is ContentFunctionEffectNode && node.Opcode is Gw2ContentFunctions.ApplyMovementModifierOpcode or Gw2ContentFunctions.ApplyMovementModifierWithDurationOpcode or Gw2ContentFunctions.WhileLoopOpcode));
    }

    [TestMethod]
    public void EveryNodeOfEveryProgramHasAKnownOpcodeAndDecodesItsReferences()
    {
        var dat = Gw2Installation.DatPath();
        if (string.IsNullOrEmpty(dat) || string.IsNullOrEmpty(Gw2Installation.ExecutablePath())) Assert.Inconclusive("Guild Wars 2 is not installed.");
        using var reader = Gw2ContentReader.Open(dat, verifyChecksums: false);
        var functions = new Gw2ContentFunctions(reader.ContentIndex, ContentFunctionContentLayout.Current, SkillPaletteContentLayout.Current.SkillContentType);
        var nodes = Enumerable.Range(0, 2000).Select(uid => functions.Program((uint)uid)).OfType<ContentFunctionProgram>().SelectMany(program => program.Nodes).ToList();

        Assert.IsTrue(nodes.Count > 2000);
        CollectionAssert.AreEqual(Array.Empty<uint>(), nodes.Select(node => node.Opcode).Where(opcode => !ContentFunctionArgumentLayouts.IsKnown((ContentFunctionOpcode)opcode)).Distinct().ToArray());
        var effects = nodes.OfType<ContentFunctionEffectNode>().ToList();
        Assert.IsTrue(effects.Where(effect => effect.Kind == ContentFunctionOpcode.ActivateSkill).All(effect => effect.Uid(ContentFunctionArgument.Skill) is > 0));
        Assert.IsTrue(effects.Where(effect => effect.Kind == ContentFunctionOpcode.ApplyMovementModifierAndDrain).All(effect => effect.Uid(ContentFunctionArgument.MovementModifier) is > 0));
        Assert.IsTrue(effects.Where(effect => effect.Kind == ContentFunctionOpcode.SimulateAdvancedMovementInputWhileCondition).All(effect => effect.Condition(ContentFunctionArgument.Condition) is not null));
        Assert.IsTrue(effects.Where(effect => effect.Kind == ContentFunctionOpcode.SimulateAdvancedMovementInputForDuration).All(effect => effect.Word(ContentFunctionArgument.Duration) > 0));
        Assert.IsTrue(effects.Where(effect => effect.Kind == ContentFunctionOpcode.PlayEffect).All(effect => effect.Arguments[ContentFunctionArgument.Reference].Location is not null));
    }
}
