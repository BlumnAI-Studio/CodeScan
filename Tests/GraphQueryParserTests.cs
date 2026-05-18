using CodeScan.Models;
using CodeScan.Services;

namespace CodeScan.Tests;

public class GraphQueryParserTests
{
    [Fact]
    public void Parse_NodeQuery_WithKindWhereAndLimit()
    {
        var query = GraphQueryParser.Parse("MATCH (c:class) WHERE c.label CONTAINS 'HttpClient' LIMIT 20");

        Assert.Equal("c", query.LeftAlias);
        Assert.Equal("class", query.LeftKind);
        Assert.False(query.HasEdge);
        Assert.Equal(20, query.Limit);
        Assert.Single(query.Conditions);
        Assert.Equal("label", query.Conditions[0].Field);
        Assert.Equal(GraphQueryOperator.Contains, query.Conditions[0].Operator);
        Assert.Equal("HttpClient", query.Conditions[0].Value);
    }

    [Fact]
    public void Parse_EdgeQuery_WithAliasesAndKinds()
    {
        var query = GraphQueryParser.Parse("MATCH (f:file)-[r:imports]->(m:module) WHERE m.label STARTS WITH 'System' RETURN f,r,m LIMIT 10");

        Assert.True(query.HasEdge);
        Assert.Equal("f", query.LeftAlias);
        Assert.Equal("file", query.LeftKind);
        Assert.Equal("r", query.EdgeAlias);
        Assert.Equal("imports", query.EdgeKind);
        Assert.Equal("m", query.RightAlias);
        Assert.Equal("module", query.RightKind);
        Assert.Equal(10, query.Limit);
        Assert.Single(query.Conditions);
        Assert.Equal(GraphQueryOperator.StartsWith, query.Conditions[0].Operator);
    }

    [Fact]
    public void Parse_RejectsUnsupportedEdgeField()
    {
        Assert.Throws<GraphQueryParseException>(() =>
            GraphQueryParser.Parse("MATCH (a:class)-[r:uses_type]->(b:type) WHERE r.path CONTAINS 'x'"));
    }

    // Actor-model edges (T2): free-form EdgeKind means the parser accepts them
    // without code change. The canonical names live in EdgeKinds.
    [Theory]
    [InlineData(EdgeKinds.SpawnsChild)]
    [InlineData(EdgeKinds.ReceivesMessage)]
    [InlineData(EdgeKinds.SendsMessageTo)]
    [InlineData(EdgeKinds.SupervisesWith)]
    [InlineData(EdgeKinds.ActorNamed)]
    [InlineData(EdgeKinds.Activates)]
    public void Parse_AcceptsActorModelEdgeKinds(string edge)
    {
        var query = GraphQueryParser.Parse($"MATCH (a:class)-[r:{edge}]->(b:type) LIMIT 5");

        Assert.True(query.HasEdge);
        Assert.Equal(edge, query.EdgeKind);
        Assert.Equal(5, query.Limit);
    }
}
