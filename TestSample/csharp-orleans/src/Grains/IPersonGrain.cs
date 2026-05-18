using HelloOrleans.Models;
using Orleans;

namespace HelloOrleans.Grains;

/// <summary>
/// Base grain interface — every speaker grain extends this. The string key is
/// the speaker's name (Alice / 진수 / ハナコ).
/// </summary>
public interface IPersonGrain : IGrainWithStringKey
{
    Task<HelloResponse> SayHello();
}
