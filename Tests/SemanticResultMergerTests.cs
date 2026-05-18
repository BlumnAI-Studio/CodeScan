using CodeScan.Models;
using CodeScan.Services.Semantic;

namespace CodeScan.Tests;

public class SemanticResultMergerTests
{
    [Fact]
    public void Parse_EdgeLine_BecomesSourceDependency()
    {
        var ndjson = """
            {"kind":"edge","from":{"type":"class","name":"EnSpeaker"},"to":{"type":"class","name":"Person"},"rel":"inherits_or_implements","detail":"semantic","line":3}
            """;

        var deps = SemanticResultMerger.Parse(ndjson);

        Assert.Single(deps);
        var d = deps[0];
        Assert.Equal("class", d.FromKind);
        Assert.Equal("EnSpeaker", d.FromName);
        Assert.Equal("inherits_or_implements", d.EdgeKind);
        Assert.Equal("class", d.ToKind);
        Assert.Equal("Person", d.ToName);
        Assert.Equal(3, d.Line);
        Assert.Equal("semantic", d.Strategy);
    }

    [Fact]
    public void Parse_NodeLines_AreIgnored()
    {
        var ndjson = """
            {"kind":"node","type":"class","name":"EnSpeaker","file":"src/EnSpeaker.cs","line":3}
            {"kind":"edge","from":{"type":"file","name":"Main.cs"},"to":{"type":"module","name":"HelloWorld"},"rel":"imports","line":1}
            """;

        var deps = SemanticResultMerger.Parse(ndjson);

        Assert.Single(deps);
        Assert.Equal("imports", deps[0].EdgeKind);
    }

    [Fact]
    public void Parse_ActorModelEdge_PreservesEdgeKind()
    {
        // The actor-model edges from T2 must round-trip without special-casing —
        // the merger is edge-kind-agnostic.
        var ndjson = """
            {"kind":"edge","from":{"type":"class","name":"WorldActor"},"to":{"type":"class","name":"EnSpeakerActor"},"rel":"spawns_child","line":12}
            """;

        var deps = SemanticResultMerger.Parse(ndjson);

        Assert.Single(deps);
        Assert.Equal(EdgeKinds.SpawnsChild, deps[0].EdgeKind);
    }

    [Fact]
    public void Parse_MalformedLines_AreSkipped()
    {
        var ndjson = """
            this is not json
            {"kind":"edge","from":{"type":"class","name":"A"},"to":{"type":"class","name":"B"},"rel":"inherits_or_implements","line":1}
            {also broken
            """;

        var deps = SemanticResultMerger.Parse(ndjson);

        Assert.Single(deps);
        Assert.Equal("A", deps[0].FromName);
    }

    [Fact]
    public void Merge_SemanticOverridesRegex_OnSameTriple()
    {
        var regex = new List<SourceDependency>
        {
            new()
            {
                FromKind = "class", FromName = "A",
                EdgeKind = "creates",
                ToKind = "class", ToName = "B",
                Strategy = "regex", Detail = "object creation", Line = 5
            }
        };
        var semantic = new List<SourceDependency>
        {
            new()
            {
                FromKind = "class", FromName = "A",
                EdgeKind = "creates",
                ToKind = "class", ToName = "B",
                Strategy = "semantic", Detail = "Roslyn", Line = 5
            }
        };

        var merged = SemanticResultMerger.Merge(regex, semantic);

        Assert.Single(merged);
        Assert.Equal("semantic", merged[0].Strategy);
    }

    [Fact]
    public void Merge_KeepsRegexEdgesNotInSemantic()
    {
        var regex = new List<SourceDependency>
        {
            new()
            {
                FromKind = "class", FromName = "A",
                EdgeKind = "uses_type",
                ToKind = "type", ToName = "Logger",
                Strategy = "regex", Line = 7
            }
        };
        var semantic = new List<SourceDependency>(); // empty

        var merged = SemanticResultMerger.Merge(regex, semantic);

        Assert.Single(merged);
        Assert.Equal("regex", merged[0].Strategy);
    }

    [Fact]
    public void Cache_Key_IsDeterministic_PerContent()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmp, "namespace X; class Y { }");
            var k1 = SemanticCache.ComputeKey(tmp, "roslyn-4.13");
            var k2 = SemanticCache.ComputeKey(tmp, "roslyn-4.13");
            Assert.Equal(k1, k2);

            var k3 = SemanticCache.ComputeKey(tmp, "roslyn-4.14");
            Assert.NotEqual(k1, k3); // tool version is part of the key
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
