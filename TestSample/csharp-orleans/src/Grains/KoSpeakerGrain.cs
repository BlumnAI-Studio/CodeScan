namespace HelloOrleans.Grains;

public interface IKoSpeakerGrain : IPersonGrain { }

public sealed class KoSpeakerGrain : PersonGrain, IKoSpeakerGrain
{
    public KoSpeakerGrain() : base("ko") { }
    protected override string Greeting() => "안녕, 세상아!";
}
