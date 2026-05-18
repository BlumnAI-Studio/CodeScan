namespace HelloOrleans.Grains;

public interface IJaSpeakerGrain : IPersonGrain { }

public sealed class JaSpeakerGrain : PersonGrain, IJaSpeakerGrain
{
    public JaSpeakerGrain() : base("ja") { }
    protected override string Greeting() => "こんにちは、世界!";
}
