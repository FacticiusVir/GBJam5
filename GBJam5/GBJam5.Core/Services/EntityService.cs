using System;
using System.Collections.Generic;

namespace GBJam5.Services
{
    public class EntityService
        : GameService, IEntityService, IUpdatable
    {
        private IUpdateLoopService updateLoop;
        private List<EntityComponent> newComponents = new List<EntityComponent>();
        private IServiceProvider serviceProvider;

        public override void Initialise(Game game)
        {
            this.updateLoop = game.Services.GetService<IUpdateLoopService>();
            this.serviceProvider = game.Services;
        }

        public override void Start()
        {
            this.updateLoop.Register(this, UpdateStage.PreUpdate);
        }

        public Entity CreateEntity()
        {
            return new Entity(this);
        }

        public void RegisterComponent(EntityComponent component)
        {
            this.newComponents.Add(component);
        }

        public void Update()
        {
            for (int componentIndex = 0; componentIndex < this.newComponents.Count; componentIndex++)
            {
                this.newComponents[componentIndex].Initialise(this.serviceProvider);
            }

            for (int componentIndex = 0; componentIndex < this.newComponents.Count; componentIndex++)
            {
                this.newComponents[componentIndex].Start();
            }

            this.newComponents.Clear();
        }
    }
}
