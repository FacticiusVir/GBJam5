using GBJam5.Services;

namespace GBJam5.Components
{
    public class PixelScaleManager
        : EntityComponent, IUpdatable
    {
        private IInputEventService inputEvents;
        private IGraphicsDeviceService graphicsDevice;

        public override void Initialise()
        {
            this.inputEvents = this.Entity.Game.Services.GetService<IInputEventService>();
            this.graphicsDevice = this.Entity.Game.Services.GetService<IGraphicsDeviceService>();
            this.Entity.Game.Services.GetService<IUpdateLoopService>().Register(this, UpdateStage.Update);
        }

        public void Update()
        {
            int pixelScale = this.graphicsDevice.PixelScale;

            if (this.inputEvents.OnPixelScaleUp)
            {
                pixelScale++;
            }

            if (this.inputEvents.OnPixelScaleDown)
            {
                pixelScale--;
            }

            if (pixelScale < 1)
            {
                pixelScale = 1;
            }
            else if (pixelScale > 8)
            {
                pixelScale = 8;
            }

            this.graphicsDevice.PixelScale = pixelScale;
        }
    }
}
