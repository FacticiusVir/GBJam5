using GBJam5.Services;
using System;

namespace GBJam5
{
    public class Game
    {
        private DictionaryServiceProvider services = new DictionaryServiceProvider();

        public IServiceProvider Services => this.services;

        public GameRunState RunState
        {
            get;
            private set;
        } = GameRunState.PreInitialise;

        public void BindService<TKey, TInstance>()
            where TKey : IGameService
            where TInstance : TKey, new()
        {
            this.services.Bind<TKey, TInstance>();
        }

        public void BindService<TKey>(TKey instance)
            where TKey : IGameService
        {
            this.services.Bind(instance);
        }

        public void Initialise()
        {
            this.CheckRunState(GameRunState.PreInitialise);

            foreach (var service in this.services.GetAll())
            {
                service.Initialise(this);
            }

            this.RunState = GameRunState.Initialised;
        }
        
        public void Start()
        {
            this.CheckRunState(GameRunState.Initialised);

            foreach (var service in this.services.GetAll())
            {
                service.Start();
            }

            this.RunState = GameRunState.Running;
        }

        public void SignalStop()
        {
            this.CheckRunState(GameRunState.Running);

            this.RunState = GameRunState.Stopping;
        }

        public void Stop()
        {
            this.CheckRunState(GameRunState.Stopping);

            foreach (var service in this.services.GetAll())
            {
                service.Stop();
            }

            this.RunState = GameRunState.Stopped;
        }

        public Entity CreateEntity()
        {
            return new Entity(this);
        }

        private void CheckRunState(GameRunState requiredState)
        {
            if (this.RunState != requiredState)
            {
                throw new InvalidOperationException();
            }
        }
    }

    public enum GameRunState
    {
        PreInitialise,
        Initialised,
        Running,
        Stopping,
        Stopped
    }
}
