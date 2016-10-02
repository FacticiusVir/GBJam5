namespace GBJam5.Services
{
    public interface ILoggingService
        : IGameService
    {
        void Log(string message);
    }
}
