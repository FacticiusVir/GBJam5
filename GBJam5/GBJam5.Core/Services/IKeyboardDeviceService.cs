using System.Windows.Forms;

namespace GBJam5.Services
{
    public interface IKeyboardDeviceService
        : IGameService
    {
        bool IsKeyDown(Keys key);

        bool OnKeyDown(Keys keys);
    }
}
