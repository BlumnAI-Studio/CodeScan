namespace CodeScan.Models;

/// <summary>
/// Canonical names for the <see cref="SourceDependency.EdgeKind"/> field. Edge kinds
/// are stored as free-form strings in SQLite so adding a new one needs no schema
/// change — this class exists to keep the known set discoverable and to give
/// callers (analyzers, the semantic-docker NDJSON merger, graph queries) a single
/// place to reference.
/// </summary>
public static class EdgeKinds
{
    // --- structural (always extracted by regex analyzers) ---
    public const string Contains = "contains";              // file -> class
    public const string Defines = "defines";                // class -> method
    public const string Imports = "imports";                // file -> module
    public const string InheritsOrImplements = "inherits_or_implements"; // class -> type
    public const string Creates = "creates";                // class -> type
    public const string UsesType = "uses_type";             // class -> type

    // --- actor-model (emitted by semantic-docker; not extractable from regex) ---
    public const string SpawnsChild = "spawns_child";           // parent actor -> child actor type
    public const string ReceivesMessage = "receives_message";   // actor -> message type
    public const string SendsMessageTo = "sends_message_to";    // sender -> (ref, msg type)
    public const string SupervisesWith = "supervises_with";     // parent -> SupervisionStrategy
    public const string ActorNamed = "actor_named";             // actor -> logical path string
}
