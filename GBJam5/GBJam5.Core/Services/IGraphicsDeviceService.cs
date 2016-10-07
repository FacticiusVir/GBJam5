using GlmSharp;

namespace GBJam5.Services
{
    public interface IGraphicsDeviceService
        : IGameService
    {
        int PixelScale { get; set; }

        int RegisterSprite(vec2 position);

        void UpdateSprite(int index, vec2 position);
    }
}