﻿using System;

namespace GBJam5
{
    public class EntityComponent
    {
        public Entity Entity
        {
            get;
            internal set;
        }

        public virtual void Initialise(IServiceProvider provider)
        {
        }

        public virtual void Start()
        {
        }

        public virtual void Stop()
        {
        }
    }
}