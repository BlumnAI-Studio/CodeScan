using HelloOrleans.Models;
using Orleans;

namespace HelloOrleans.Grains;

/// <summary>
/// Abstract base. The Orleans equivalent of csharp-akka's PersonActor — only
/// <see cref="Greeting"/> is abstract; SayHello is shared.
/// </summary>
public abstract class PersonGrain : Grain, IPersonGrain
{
    private readonly string _languageTag;

    protected PersonGrain(string languageTag)
    {
        _languageTag = languageTag;
    }

    protected abstract string Greeting();

    public Task<HelloResponse> SayHello()
        => Task.FromResult(new HelloResponse(_languageTag, this.GetPrimaryKeyString(), Greeting()));
}
