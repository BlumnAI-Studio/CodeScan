using CodeScan.Models;
using CodeScan.Services;

namespace CodeScan.Tests;

public class GraphQueryStoreTests
{
    [Fact]
    public void QueryGraph_ReturnsRelationshipMatches_FromStoredGraph()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"codescan_test_{Guid.NewGuid():N}.db");
        var tempDir = Path.Combine(Path.GetTempPath(), $"codescan_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "Client.cs");
        File.WriteAllText(filePath, "class Client { void Send() { var http = new HttpClient(); } }");

        try
        {
            using var db = new SqliteStore(dbPath);
            var projectId = db.UpsertProject(Path.GetFullPath(tempDir));
            db.InsertScan(projectId,
            [
                new FileEntry
                {
                    FullPath = filePath,
                    RelativePath = "Client.cs",
                    Name = "Client.cs",
                    Size = new FileInfo(filePath).Length,
                    IsDirectory = false,
                    Depth = 0,
                    Methods =
                    [
                        new MethodEntry
                        {
                            ClassName = "Client",
                            MethodName = "Send",
                            StartLine = 1,
                            EndLine = 1
                        }
                    ],
                    Dependencies =
                    [
                        new SourceDependency
                        {
                            FromKind = "class",
                            FromName = "Client",
                            EdgeKind = "uses_type",
                            ToKind = "type",
                            ToName = "HttpClient",
                            Strategy = "test",
                            Detail = "type reference",
                            Line = 1
                        }
                    ]
                }
            ]);

            var graph = db.QueryGraph("MATCH (c:class)-[r:uses_type]->(t:type) WHERE t.label = 'HttpClient' LIMIT 10");

            Assert.Contains(graph.Nodes, n => n.Kind == "class" && n.Label == "Client");
            Assert.Contains(graph.Nodes, n => n.Kind == "type" && n.Label == "HttpClient");
            Assert.Contains(graph.Edges, e => e.Kind == "uses_type");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
            try { if (File.Exists(dbPath)) File.Delete(dbPath); } catch { }
        }
    }
}
