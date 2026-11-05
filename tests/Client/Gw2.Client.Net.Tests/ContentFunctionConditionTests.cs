using Gw2.Dat.Content;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gw2.Client.Net.Tests;

[TestClass]
public sealed class ContentFunctionConditionTests
{
    private sealed class SwimmingHost : IContentFunctionHost
    {
        public byte LocaleState => 6;

        public float Endurance => 50;

        public float MaximumEndurance => 100;

        public uint AdvancedMovementInputFlags => 4;
    }

    [TestMethod]
    public void LocaleKindsFollowTheCharacterLocaleState()
    {
        Assert.IsTrue(ContentFunctionConditions.LocaleMatches(0, 3));
        Assert.IsFalse(ContentFunctionConditions.LocaleMatches(0, 4));
        Assert.IsTrue(ContentFunctionConditions.LocaleMatches(4, 6));
        Assert.IsTrue(ContentFunctionConditions.LocaleMatches(4, 9));
        Assert.IsTrue(ContentFunctionConditions.LocaleMatches(5, 11));
        Assert.IsTrue(ContentFunctionConditions.LocaleMatches(8, 200));
    }

    [TestMethod]
    public void LessComparisonsPutTheValueOnTheLeft()
    {
        Assert.IsTrue(ContentFunctionConditions.Compare(1, ContentFunctionComparison.Less, 2));
        Assert.IsTrue(ContentFunctionConditions.Compare(2, ContentFunctionComparison.LessOrEqual, 2));
        Assert.IsFalse(ContentFunctionConditions.Compare(3, ContentFunctionComparison.Greater, 3));
    }

    [TestMethod]
    public void EvaluatesNestedConditionsAgainstTheHostAndTheStackInput()
    {
        var host = new SwimmingHost();
        var underwater = new ContentFunctionCondition(ContentFunctionConditionType.Locale, false, LocaleKind: 4);
        var halfEndurance = new ContentFunctionCondition(ContentFunctionConditionType.HasEndurancePercent, false, Value: 50);
        var released = new ContentFunctionCondition(ContentFunctionConditionType.StackInputReleased, true);
        var both = new ContentFunctionCondition(ContentFunctionConditionType.And, false, Left: underwater, Right: halfEndurance);
        Assert.IsTrue(ContentFunctionConditions.Evaluate(both, host, 6, false));
        Assert.IsTrue(ContentFunctionConditions.Evaluate(released, host, 6, false));
        Assert.IsFalse(ContentFunctionConditions.Evaluate(released, host, 6, true));
        Assert.IsTrue(ContentFunctionConditions.Evaluate(new ContentFunctionCondition(ContentFunctionConditionType.StackInputEquals, false, Selector: 6), host, 6, false));
        Assert.IsTrue(ContentFunctionConditions.Evaluate(new ContentFunctionCondition(ContentFunctionConditionType.PlayerIsUsingAdvancedMovementInput, false, Selector: 1), host, 0, false));
    }
}
