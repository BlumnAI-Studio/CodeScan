using Orleans;

namespace HelloOrleans.Models;

[GenerateSerializer]
public sealed record HelloResponse(
    [property: Id(0)] string LanguageTag,
    [property: Id(1)] string Name,
    [property: Id(2)] string Greeting);
