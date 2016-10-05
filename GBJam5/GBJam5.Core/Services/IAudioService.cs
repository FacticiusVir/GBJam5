using GBJam5.Audio;

namespace GBJam5.Services
{
    public interface IAudioService
        : IGameService
    {
        ISoundEffect CreateSoundEffect(string filePath);

        IAudio CreateAudio(string filePath);
    }
}
