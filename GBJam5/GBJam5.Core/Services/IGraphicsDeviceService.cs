namespace GBJam5.Services
{
    public interface IGraphicsDeviceService
        : IGameService
    {
        int PixelScale { get; set; }
    }
}