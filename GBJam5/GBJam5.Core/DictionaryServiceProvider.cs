using GBJam5.Services;
using System;
using System.Collections.Generic;

namespace GBJam5
{
    public class DictionaryServiceProvider
        : IServiceProvider
    {
        private Dictionary<Type, IGameService> services = new Dictionary<Type, IGameService>();

        public object GetService(Type serviceType)
        {
            return this.services[serviceType];
        }

        public void Bind<TKey, TInstance>()
            where TKey : IGameService
            where TInstance : TKey, new()
        {
            this.services.Add(typeof(TKey), new TInstance());
        }

        public void Bind<TKey>(TKey instance)
            where TKey : IGameService
        {
            this.services.Add(typeof(TKey), instance);
        }

        public IEnumerable<IGameService> GetAll()
        {
            return this.services.Values;
        }
    }
}
