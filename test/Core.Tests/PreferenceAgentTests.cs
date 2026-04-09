using Core.Contracts;
using Core.Context;
using IAW.Testing;
using IAW.Agents.Personal;
using Xunit;

namespace IAW.Core.Tests;

public class PreferenceAgentTests : AgentTest<PreferenceAgent>
{
    private IPreference Pref(string id) => (IPreference)Agent(id);

    [Fact]
    public async Task SetAndGetPreference()
    {
        var ct = TestContext.Current.CancellationToken;
        var pref = Pref(UniqueId("set-get"));

        await pref.SetRuleAsync(PreferenceRule.Create(
            "testing", "No mocks in integration tests",
            "Past incident: mock/prod divergence", "high"), ct);

        var rules = await pref.GetRulesAsync("testing", ct);
        Assert.Single(rules);
        Assert.Equal("No mocks in integration tests", rules[0].Rule);
    }

    [Fact]
    public async Task GetRulesByCategory_FiltersCorrectly()
    {
        var ct = TestContext.Current.CancellationToken;
        var pref = Pref(UniqueId("filter"));

        await pref.SetRuleAsync(PreferenceRule.Create("testing", "No mocks", "incident", "high"), ct);
        await pref.SetRuleAsync(PreferenceRule.Create("architecture", "Prefer Cosmos", "latency", "high"), ct);
        await pref.SetRuleAsync(PreferenceRule.Create("testing", "Use real DB", "reliability", "medium"), ct);

        var testingRules = await pref.GetRulesAsync("testing", ct);
        Assert.Equal(2, testingRules.Count);

        var archRules = await pref.GetRulesAsync("architecture", ct);
        Assert.Single(archRules);
    }

    [Fact]
    public async Task RemoveRule_DeletesFromState()
    {
        var ct = TestContext.Current.CancellationToken;
        var pref = Pref(UniqueId("remove"));

        await pref.SetRuleAsync(PreferenceRule.Create("style", "No summary comments", "convention", "high"), ct);
        var rulesBefore = await pref.GetRulesAsync("style", ct);
        Assert.Single(rulesBefore);

        await pref.RemoveRuleAsync("style", "No summary comments", ct);
        var rulesAfter = await pref.GetRulesAsync("style", ct);
        Assert.Empty(rulesAfter);
    }

    [Fact]
    public async Task GetAllRules_ReturnsAllCategories()
    {
        var ct = TestContext.Current.CancellationToken;
        var pref = Pref(UniqueId("all"));

        await pref.SetRuleAsync(PreferenceRule.Create("testing", "No mocks", null, "high"), ct);
        await pref.SetRuleAsync(PreferenceRule.Create("architecture", "Prefer Cosmos", null, "medium"), ct);
        await pref.SetRuleAsync(PreferenceRule.Create("tools", "Use Aspire", null, "high"), ct);

        var all = await pref.GetAllRulesAsync(ct);
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task Preferences_SurviveGrainDeactivation()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = UniqueId("durable-pref");
        var pref = Pref(id);

        await pref.SetRuleAsync(PreferenceRule.Create(
            "style", "No summary comments", "project convention", "high"), ct);

        var mgmt = Cluster.GrainFactory.GetGrain<IManagementGrain>(0);
        await mgmt.ForceActivationCollection(TimeSpan.Zero);
        await Task.Delay(1000, ct);

        var pref2 = Pref(id);
        var rules = await pref2.GetRulesAsync("style", ct);
        Assert.Single(rules);
        Assert.Equal("No summary comments", rules[0].Rule);
    }

    [Fact]
    public async Task PreferenceContextProvider_InjectsRules()
    {
        var ct = TestContext.Current.CancellationToken;
        var prefId = UniqueId("ctx-pref");
        var pref = Pref(prefId);

        await pref.SetRuleAsync(PreferenceRule.Create(
            "testing", "No mocks in integration tests", "past incident", "high"), ct);

        var provider = new PreferenceContextProvider(Cluster.GrainFactory, prefId, "testing");
        var context = await provider.GetContextAsync("dotnet-agent", "write integration tests", ct);

        Assert.NotEmpty(context);
        var combined = string.Join("\n", context);
        Assert.Contains("No mocks", combined);
    }
}
