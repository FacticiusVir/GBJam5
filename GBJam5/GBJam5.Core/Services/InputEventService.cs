using System.Windows.Forms;

namespace GBJam5.Services
{
    public class InputEventService
        : GameService, IInputEventService
    {
        private IKeyboardDeviceService keyboardDevice;

        public override void Initialise(Game game)
        {
            this.keyboardDevice = game.Services.GetService<IKeyboardDeviceService>();
        }

        public bool OnPixelScaleDown
        {
            get
            {
                return this.keyboardDevice.OnKeyDown(Keys.Subtract)
                    || this.keyboardDevice.OnKeyDown(Keys.OemMinus);
            }
        }

        public bool OnPixelScaleUp
        {
            get
            {
                return this.keyboardDevice.OnKeyDown(Keys.Add)
                    || this.keyboardDevice.OnKeyDown(Keys.Oemplus);
            }
        }
    }

    public interface IInputEventService
        : IGameService
    {
        bool OnPixelScaleUp { get; }

        bool OnPixelScaleDown { get; }
    }
}
