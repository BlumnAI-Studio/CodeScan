namespace HelloOrleans.Grains;

public interface IEnSpeakerGrain : IPersonGrain { }

public sealed class EnSpeakerGrain : PersonGrain, IEnSpeakerGrain
{
    public EnSpeakerGrain() : base("en") { }
    protected override string Greeting() => "Hello, World!";
}
