using System;
using System.Collections.Generic;

namespace GBJam5
{
    public class Entity
    {
        private Dictionary<Type, EntityComponent> components = new Dictionary<Type, EntityComponent>();

        public Entity(Game game)
        {
            this.Game = game;
        }

        public Game Game
        {
            get;
            private set;
        }

        public void AddComponent<T>()
            where T : EntityComponent, new()
        {
            var component = new T();

            component.Entity = this;

            this.components.Add(typeof(T), component);
        }

        public void Initialise()
        {
            foreach(var component in this.components.Values)
            {
                component.Initialise();
            }
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
