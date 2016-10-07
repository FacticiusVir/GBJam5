using GBJam5.Services;
using System;

namespace GBJam5.Components
{
    public class PixelScaleManager
        : EntityComponent, IUpdatable
    {
        private IInputEventService inputEvents;
        private IGraphicsDeviceService graphicsDevice;

        public override void Initialise(IServiceProvider provider)
        {
            this.inputEvents = provider.GetService<IInputEventService>();
            this.graphicsDevice = provider.GetService<IGraphicsDeviceService>();
            provider.GetService<IUpdateLoopService>().Register(this, UpdateStage.Update);
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
