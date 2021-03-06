﻿// Copyright (c) Samuel Cragg.
//
// Licensed under the MIT license. See LICENSE file in the project root for
// full license information.

namespace Crest.Host
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using Crest.Abstractions;
    using Crest.Host.Engine;
    using Crest.Host.Routing;

    /// <summary>
    /// Allows the configuration of the Crest framework during application
    /// startup.
    /// </summary>
    public abstract class Bootstrapper : IDisposable
    {
        private readonly IServiceLocator serviceLocator;
        private readonly IServiceRegister serviceRegister;

        /// <summary>
        /// Initializes a new instance of the <see cref="Bootstrapper"/> class.
        /// </summary>
        protected Bootstrapper()
            : this(new ServiceLocator())
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Bootstrapper"/> class.
        /// </summary>
        /// <param name="serviceLocator">Used to locate the services.</param>
        protected Bootstrapper(IServiceLocator serviceLocator)
        {
            Check.IsNotNull(serviceLocator, nameof(serviceLocator));
            this.serviceLocator = serviceLocator;
            this.serviceRegister = this.serviceLocator.GetServiceRegister();
        }

        /// <summary>
        /// Finalizes an instance of the <see cref="Bootstrapper"/> class.
        /// </summary>
        ~Bootstrapper()
        {
            this.Dispose(false);
        }

        /// <summary>
        /// Gets the discovered routes mapped with their methods.
        /// </summary>
        public virtual IRouteMapper RouteMapper
        {
            get;
            private set;
        }

        /// <summary>
        /// Gets the instance to use to resolve services.
        /// </summary>
        public IServiceLocator ServiceLocator
        {
            get
            {
                this.ThrowIfDisposed();
                return this.serviceLocator;
            }
        }

        /// <summary>
        /// Gets a value indicating whether the object has been disposed or not.
        /// </summary>
        protected bool IsDisposed
        {
            get;
            private set;
        }

        /// <summary>
        /// Releases all resources used by this instance.
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Releases the unmanaged resources used by this instance and
        /// optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">
        /// <c>true</c> to release both managed and unmanaged resources;
        /// <c>false</c> to release only unmanaged resources.
        /// </param>
        protected virtual void Dispose(bool disposing)
        {
            if (!this.IsDisposed)
            {
                if (disposing)
                {
                    if (this.serviceLocator is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }

                this.IsDisposed = true;
            }
        }

        /// <summary>
        /// Initializes the container and routes.
        /// </summary>
        protected void Initialize()
        {
            IDiscoveryService discovery = this.serviceLocator.GetDiscoveryService();
            IReadOnlyCollection<Type> types = this.RegisterTypes(discovery, discovery.GetDiscoveredTypes());
            this.RegisterTypes(discovery, this.GetDefaultOptionalServices(discovery, types));

            List<RouteMetadata> routes =
                types.SelectMany(discovery.GetRoutes).ToList();
            this.RouteMapper = new RouteMapper(routes, this.GetDirectRoutes());

            IConfigurationService configuration = this.serviceLocator.GetConfigurationService();
            configuration.InitializeProviders(types).Wait();
            this.serviceRegister.RegisterInitializer(
                configuration.CanConfigure,
                instance => configuration.InitializeInstance(instance, this.serviceLocator));
        }

        /// <summary>
        /// Raises the <see cref="ObjectDisposedException"/> if <see cref="Dispose()"/>
        /// has been called on this instance.
        /// </summary>
        protected void ThrowIfDisposed()
        {
            if (this.IsDisposed)
            {
                throw new ObjectDisposedException(this.GetType().Name);
            }
        }

        private IEnumerable<Type> GetDefaultOptionalServices(IDiscoveryService discovery, IReadOnlyCollection<Type> types)
        {
            foreach (Type serviceType in discovery.GetOptionalServices())
            {
                foreach (Type serviceInterface in serviceType.GetInterfaces())
                {
                    // Try to resolve the service as an array so that if any
                    // haven't been registered we don't get an exception
                    var implementations = (Array)this.serviceLocator.GetService(
                        serviceInterface.MakeArrayType());

                    if (implementations.Length == 0)
                    {
                        yield return serviceType;
                    }
                }
            }
        }

        private IEnumerable<DirectRouteMetadata> GetDirectRoutes()
        {
            return this.serviceLocator.GetDirectRouteProviders()
                       .SelectMany(d => d.GetDirectRoutes());
        }

        private Func<object> GetFactory(ITypeFactory[] factories, Type type)
        {
            for (int i = 0; i < factories.Length; i++)
            {
                // Assign to local so that the lambda doesn't capture the whole array
                ITypeFactory factory = factories[i];
                if (factory.CanCreate(type))
                {
                    return () => factory.Create(type, this.serviceLocator);
                }
            }

            return null;
        }

        private IReadOnlyCollection<Type> RegisterTypes(IDiscoveryService discovery, IEnumerable<Type> types)
        {
            ITypeFactory[] factories = discovery.GetCustomFactories().ToArray();
            var normal = new List<Type>();
            var custom = new List<Type>(); // We need to store these to return them at the end

            foreach (Type type in types)
            {
                Func<object> factory = this.GetFactory(factories, type);
                if (factory == null)
                {
                    normal.Add(type);
                }
                else
                {
                    this.serviceRegister.RegisterFactory(type, factory);
                    custom.Add(type);
                }
            }

            this.serviceRegister.RegisterMany(normal, discovery.IsSingleInstance);

            // Normal probably has the most types in it, so fold the others into it
            normal.AddRange(custom);
            return normal;
        }
    }
}
