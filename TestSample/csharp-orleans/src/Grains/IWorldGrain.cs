using Orleans;

namespace HelloOrleans.Grains;

public interface IWorldGrain : IGrainWithStringKey
{
    Task HelloAll();
}
