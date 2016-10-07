using GBJam5.Services;
using System;
using System.Collections.Generic;

namespace GBJam5
{
    public class Entity
    {
        private Dictionary<Type, EntityComponent> components = new Dictionary<Type, EntityComponent>();
        private IEntityService manager;

        public Entity(IEntityService manager)
        {
            this.manager = manager;
        }

        public void AddComponent<T>()
            where T : EntityComponent, new()
        {
            var component = new T();

            component.Entity = this;

            this.components.Add(typeof(T), component);

            this.manager.RegisterComponent(component);
        }

        public T GetComponent<T>()
            where T : EntityComponent
        {
            EntityComponent result;

            this.components.TryGetValue(typeof(T), out result);

            return (T)result;
        }
    }
}
