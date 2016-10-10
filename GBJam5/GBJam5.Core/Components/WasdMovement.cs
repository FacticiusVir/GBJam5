using GBJam5.Services;
using GlmSharp;
using System;

namespace GBJam5.Components
{
    public class WasdMovement
        : EntityComponent, IUpdatable
    {
        private IUpdateLoopService updateLoop;
        private IInputEventService inputEvents;
        private Transform2 transform;

        public float Speed
        {
            get;
            set;
        } = 32;

        public override void Initialise(IServiceProvider provider)
        {
            this.updateLoop = provider.GetService<IUpdateLoopService>();
            this.inputEvents = provider.GetService<IInputEventService>();
            this.transform = this.Entity.GetComponent<Transform2>();
        }

        public override void Start()
        {
            this.updateLoop.Register(this, UpdateStage.Update);
        }

        public void Update()
        {
            vec2 deltaMovement = vec2.Zero;

            if (this.inputEvents.IsMoveUp)
            {
                deltaMovement -= vec2.UnitY;
            }
            else if (this.inputEvents.IsMoveDown)
            {
                deltaMovement += vec2.UnitY;
            }

            if (this.inputEvents.IsMoveLeft)
            {
                deltaMovement -= vec2.UnitX;
            }
            else if (this.inputEvents.IsMoveRight)
            {
                deltaMovement += vec2.UnitX;
            }

            this.transform.Position = vec2.Clamp(this.transform.Position + deltaMovement * this.updateLoop.DeltaT * this.Speed, 0, new vec2(144, 128));
        }
    }
}
