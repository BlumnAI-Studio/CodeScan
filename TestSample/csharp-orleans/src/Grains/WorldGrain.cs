using Orleans;

namespace HelloOrleans.Grains;

/// <summary>
/// Coordinator grain. Unlike Akka's WorldActor which calls
/// <c>Context.ActorOf(Props.Create(...))</c> to spawn children, Orleans uses
/// <c>GrainFactory.GetGrain&lt;T&gt;(id)</c> — the runtime activates the grain
/// on first call ("virtual actor"). Same domain, different pattern.
/// </summary>
public sealed class WorldGrain : Grain, IWorldGrain
{
    public async Task HelloAll()
    {
        var en = GrainFactory.GetGrain<IEnSpeakerGrain>("Alice");
        var ko = GrainFactory.GetGrain<IKoSpeakerGrain>("진수");
        var ja = GrainFactory.GetGrain<IJaSpeakerGrain>("ハナコ");

        var responses = await Task.WhenAll(en.SayHello(), ko.SayHello(), ja.SayHello());
        foreach (var r in responses)
            Console.WriteLine($"[{r.LanguageTag}] {r.Name}: {r.Greeting}");
    }
}
