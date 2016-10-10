using System;
using GBJam5.Services;
using GlmSharp;

namespace GBJam5.Components
{
    public class SpriteRenderer
        : EntityComponent, IUpdatable
    {
        private IGraphicsDeviceService graphicsDevice;
        private IUpdateLoopService updateLoop;
        private Transform2 transform;

        private int spriteIndex;

        public override void Initialise(IServiceProvider provider)
        {
            this.graphicsDevice = provider.GetService<IGraphicsDeviceService>();
            this.updateLoop = provider.GetService<IUpdateLoopService>();
            this.transform = this.Entity.GetComponent<Transform2>();
        }

        public override void Start()
        {
            this.spriteIndex = this.graphicsDevice.RegisterSprite(this.transform.Position);
            this.updateLoop.Register(this, UpdateStage.PreRender);
        }

        public void Update()
        {
            this.graphicsDevice.UpdateSprite(this.spriteIndex, this.transform.Position);
        }
    }
}
