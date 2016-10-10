using System;
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

        public bool IsMoveUp
        {
            get
            {
                return this.keyboardDevice.IsKeyDown(Keys.W);
            }
        }

        public bool IsMoveDown
        {
            get
            {
                return this.keyboardDevice.IsKeyDown(Keys.S);
            }
        }

        public bool IsMoveRight
        {
            get
            {
                return this.keyboardDevice.IsKeyDown(Keys.D);
            }
        }

        public bool IsMoveLeft
        {
            get
            {
                return this.keyboardDevice.IsKeyDown(Keys.A);
            }
        }
    }

    public interface IInputEventService
        : IGameService
    {
        bool OnPixelScaleUp { get; }

        bool OnPixelScaleDown { get; }

        bool IsMoveUp { get; }

        bool IsMoveDown { get; }

        bool IsMoveRight { get; }

        bool IsMoveLeft { get; }
    }
}
