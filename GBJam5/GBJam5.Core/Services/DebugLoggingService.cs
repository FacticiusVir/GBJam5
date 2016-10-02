using System.Diagnostics;

namespace GBJam5.Services
{
    public class DebugLoggingService
        : GameService, ILoggingService
    {
        public void Log(string message)
        {
            Debug.WriteLine(message);
        }
    }
}
