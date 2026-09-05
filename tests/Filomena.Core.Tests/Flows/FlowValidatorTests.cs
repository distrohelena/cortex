using Filomena.Core.Flows;
using Xunit;

namespace Filomena.Core.Tests.Flows;

public sealed class FlowValidatorTests
{
    [Fact]
    public void AcceptsMinimalTextFlow()
    {
        var issues = FlowValidator.Validate(FlowTestFixtures.MinimalTextFlow(), NodeCatalog.V1);

        Assert.Empty(issues);
    }

    [Fact]
    public void AcceptsDeclaredDmGroupInputSetAsAlternateStarts()
    {
        var issues = FlowValidator.Validate(FlowTestFixtures.DeclaredDmGroupInputSet(), NodeCatalog.V1);

        Assert.Empty(issues);
    }

    [Fact]
    public void CatalogResolvesNamedBranchesAndTrustedEffects()
    {
        Assert.Equal(19, NodeCatalog.V1.Definitions.Count);
        Assert.True(NodeCatalog.V1.TryGet("join", 1, out var join));
        Assert.Equal(FlowEffect.Pure, join.Effect);

        var resolution = join.Resolve(System.Text.Json.JsonSerializer.SerializeToElement(new { mode = "all", branches = new[] { "left", "right" } }));

        Assert.Equal(["left", "right"], resolution.ControlInputs.Select(port => port.Name));
        Assert.Contains(resolution.DataOutputs, port => port.Name == "left");
        Assert.Contains(resolution.DataOutputs, port => port.Name == "right");
    }

    [Fact]
    public void ReportsDuplicateNodeIds()
    {
        var issues = FlowValidator.Validate(FlowTestFixtures.WithDuplicateNodeId(), NodeCatalog.V1);

        Assert.Contains(issues, issue => issue.Code == "node.id_duplicate");
    }

    [Fact]
    public void ReportsMissingTargetPort()
    {
        var issues = FlowValidator.Validate(FlowTestFixtures.WithUnknownTargetPort(), NodeCatalog.V1);

        Assert.Contains(issues, issue => issue.Code == "edge.target_port_missing");
    }

    [Fact]
    public void ReportsUnknownBlockVersion()
    {
        var issues = FlowValidator.Validate(FlowTestFixtures.WithUnknownBlockVersion(), NodeCatalog.V1);

        Assert.Contains(issues, issue => issue.Code == "node.block_version_unsupported");
    }

    [Fact]
    public void ReportsMissingRequiredDataInput()
    {
        var issues = FlowValidator.Validate(FlowTestFixtures.WithMissingRequiredDataInput(), NodeCatalog.V1);

        Assert.Contains(issues, issue => issue.Code == "node.input_missing");
    }

    [Fact]
    public void ReportsWrongFieldType()
    {
        var issues = FlowValidator.Validate(FlowTestFixtures.WithWrongFieldType(), NodeCatalog.V1);

        Assert.Contains(issues, issue => issue.Code == "edge.type_incompatible");
    }

    [Fact]
    public void ReportsDuplicateProducer()
    {
        var issues = FlowValidator.Validate(FlowTestFixtures.WithDuplicateProducer(), NodeCatalog.V1);

        Assert.Contains(issues, issue => issue.Code == "edge.target_producer_duplicate");
    }

    [Fact]
    public void ReportsMissingConditionDecisionExit()
    {
        var issues = FlowValidator.Validate(FlowTestFixtures.WithMissingDecisionExit(), NodeCatalog.V1);

        Assert.Contains(issues, issue => issue.Code == "node.decision_exit_missing");
    }

    [Fact]
    public void RejectsUnsupportedFreeformCodeNode()
    {
        var issues = FlowValidator.Validate(FlowTestFixtures.WithUnsupportedCodeNode(), NodeCatalog.V1);

        Assert.Contains(issues, issue => issue.Code == "node.kind_unsupported");
    }

    [Fact]
    public void RejectsUnreachableNodes()
    {
        var issues = FlowValidator.Validate(FlowTestFixtures.WithUnreachableNode(), NodeCatalog.V1);

        Assert.Contains(issues, issue => issue.Code == "graph.unreachable_node");
    }

    [Fact]
    public void RejectsMultipleImplicitStarts()
    {
        var issues = FlowValidator.Validate(FlowTestFixtures.WithMultipleImplicitStarts(), NodeCatalog.V1);

        Assert.Contains(issues, issue => issue.Code == "graph.entry_ambiguous");
    }

    [Fact]
    public void ReportsControlCycles()
    {
        var issues = FlowValidator.Validate(FlowTestFixtures.WithControlCycle(), NodeCatalog.V1);

        Assert.Contains(issues, issue => issue.Code == "graph.control_cycle");
    }

    [Fact]
    public void ReportsDataCycles()
    {
        var issues = FlowValidator.Validate(FlowTestFixtures.WithDataCycle(), NodeCatalog.V1);

        Assert.Contains(issues, issue => issue.Code == "graph.data_cycle");
    }

    [Fact]
    public void RejectsBranchLocalDataAtUnrelatedJoin()
    {
        var issues = FlowValidator.Validate(FlowTestFixtures.WithBranchLocalData(), NodeCatalog.V1);

        Assert.Contains(issues, issue => issue.Code == "data.value_unavailable");
    }

    [Fact]
    public void RetainsAwaitContractErrors()
    {
        var issues = FlowValidator.Validate(FlowTestFixtures.WithAwaitMissingTimeoutExit(), NodeCatalog.V1);

        Assert.Contains(issues, issue => issue.Code == "node.await_contract_invalid" && issue.Severity == FlowIssueSeverity.Error);
    }

    [Fact]
    public void RetainsFirstValidEffectWarning()
    {
        var issues = FlowValidator.Validate(FlowTestFixtures.WithFirstValidEffect(), NodeCatalog.V1);

        Assert.Contains(issues, issue => issue.Code == "node.first_valid_effect_warning" && issue.Severity == FlowIssueSeverity.Warning);
    }

    [Fact]
    public void OrdersIssuesByPathThenCode()
    {
        var issues = FlowValidator.Validate(FlowTestFixtures.WithDuplicateProducer(), NodeCatalog.V1);

        Assert.Equal(issues.OrderBy(issue => issue.Path, StringComparer.Ordinal).ThenBy(issue => issue.Code, StringComparer.Ordinal), issues);
    }
}
