﻿namespace Host.UnitTests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading.Tasks;
    using Crest.Abstractions;
    using Crest.Host;
    using Crest.Host.Engine;
    using FluentAssertions;
    using NSubstitute;
    using Xunit;

    public class BootstrapperTests
    {
        private readonly FakeBootstrapper bootstrapper;
        private readonly IDiscoveryService discoveryService;
        private readonly IServiceLocator serviceLocator;
        private readonly IServiceRegister serviceRegister;

        public BootstrapperTests()
        {
            this.discoveryService = Substitute.For<IDiscoveryService>();
            this.discoveryService.GetDiscoveredTypes()
                .Returns(new[] { typeof(IFakeInterface), typeof(FakeClass) });

            this.serviceRegister = Substitute.For<IServiceRegister>();

            this.serviceLocator = Substitute.For<IServiceLocator, IDisposable>();
            this.serviceLocator.GetDiscoveryService().Returns(this.discoveryService);
            this.serviceLocator.GetServiceRegister().Returns(this.serviceRegister);

            this.bootstrapper = new FakeBootstrapper(this.serviceLocator);
        }

        internal interface IFakeInterface
        {
        }

        public sealed class Dispose : BootstrapperTests
        {
            [Fact]
            public void CanBeCalledMultipleTimes()
            {
                this.bootstrapper.Dispose();

                this.bootstrapper.Invoking(b => b.Dispose()).Should().NotThrow();
            }

            [Fact]
            public void ShouldDisposeOfTheServiceLocator()
            {
                this.bootstrapper.Dispose();

                ((IDisposable)this.serviceLocator).Received().Dispose();
            }

            [Fact]
            public void ShouldSetIsDisposedToTrue()
            {
                this.bootstrapper.IsDisposed.Should().BeFalse();

                this.bootstrapper.Dispose();

                this.bootstrapper.IsDisposed.Should().BeTrue();
            }
        }

        public sealed class Initialize : BootstrapperTests
        {
            private interface IFakeRoute
            {
                Task Route();
            }

            [Fact]
            public void ShouldHandleDisposableTypes()
            {
                this.discoveryService.GetDiscoveredTypes().Returns(new[] { typeof(FakeDisposabe) });

                this.bootstrapper.Invoking(b => b.Initialize())
                    .Should().NotThrow();
            }

            [Fact]
            public void ShouldHandleTypesThatCannotBeConstructed()
            {
                this.discoveryService.GetDiscoveredTypes().Returns(new[] { typeof(CannotInject) });

                this.bootstrapper.Invoking(b => b.Initialize())
                    .Should().NotThrow();
            }

            [Fact]
            public void ShouldInitializeClassesWithTheConfigurationService()
            {
                IConfigurationService configurationService = Substitute.For<IConfigurationService>();
                this.serviceLocator.GetConfigurationService()
                    .Returns(configurationService);

                // Force the passed in lambdas to be invoked
                object toInitialize = new FakeClass();
                this.serviceRegister.RegisterInitializer(
                    Arg.Do<Func<Type, bool>>(x => x(typeof(FakeClass))),
                    Arg.Do<Action<object>>(x => x(toInitialize)));

                this.bootstrapper.Initialize();

                configurationService.Received().CanConfigure(typeof(FakeClass));
                configurationService.Received().InitializeInstance(toInitialize, this.serviceLocator);
            }

            [Fact]
            public void ShouldInitializeTheConfigurationService()
            {
                IConfigurationService configurationService = Substitute.For<IConfigurationService>();
                this.serviceLocator.GetConfigurationService()
                    .Returns(configurationService);

                this.bootstrapper.Initialize();

                configurationService.ReceivedWithAnyArgs().InitializeProviders(null);
            }

            [Fact]
            public void ShouldRegisterCustomFactories()
            {
                ITypeFactory factory = Substitute.For<ITypeFactory>();
                factory.CanCreate(typeof(IFakeInterface))
                       .Returns(true);

                this.discoveryService.GetCustomFactories()
                    .Returns(new[] { factory });

                // Force the lambdas to get called
                this.serviceRegister.RegisterFactory(
                    typeof(IFakeInterface),
                    Arg.Do<Func<object>>(x => x()));

                this.bootstrapper.Initialize();

                factory.Received().Create(typeof(IFakeInterface), this.serviceLocator);
            }

            [Fact]
            public void ShouldRegisterDirectRoutes()
            {
                IDirectRouteProvider direct = Substitute.For<IDirectRouteProvider>();
                this.serviceLocator.GetDirectRouteProviders()
                    .Returns(new[] { direct });

                this.bootstrapper.Initialize();

                direct.Received().GetDirectRoutes();
            }

            [Fact]
            public void ShouldRegisterOptionalServices()
            {
                // Bootstrapper use the same trick as the ServiceLocator to
                // determine if a type is registered or not by asking for an
                // array of the interface - empty arrays mean nothing has been
                // registered
                this.serviceLocator.GetService(Arg.Is<Type>(t => t.IsArray))
                    .Returns(new object[0]);

                this.discoveryService.GetOptionalServices()
                    .Returns(new[] { typeof(FakeClass) });

                this.bootstrapper.Initialize();

                this.serviceRegister.Received().RegisterMany(
                    Arg.Is<IEnumerable<Type>>(types => types.Contains(typeof(FakeClass))),
                    Arg.Any<Func<Type, bool>>());
            }

            [Fact]
            public void ShouldRegisterSingletons()
            {
                using (var bootstrapper = new FakeBootstrapper(new ServiceLocator()))
                {
                    this.discoveryService.IsSingleInstance(typeof(IFakeInterface))
                        .Returns(true);

                    this.bootstrapper.Initialize();
                    object instance1 = this.bootstrapper.ServiceLocator.GetService(typeof(IFakeInterface));
                    object instance2 = this.bootstrapper.ServiceLocator.GetService(typeof(IFakeInterface));

                    instance1.Should().BeSameAs(instance2);
                }
            }

            [Fact]
            public void ShouldSetTheRouteMapper()
            {
                this.bootstrapper.RouteMapper.Should().BeNull();

                this.bootstrapper.Initialize();

                this.bootstrapper.RouteMapper.Should().NotBeNull();
            }

            internal class CannotInject : IFakeInterface
            {
                private CannotInject(int arg)
                {
                }
            }

            internal class FakeDisposabe : IDisposable
            {
                internal bool DisposeCalled { get; private set; }

                public void Dispose()
                {
                    this.DisposeCalled = true;
                }
            }
        }

        public sealed class ServiceLocatorProperty : BootstrapperTests
        {
            [Fact]
            public void ShouldReturnTheValuePassedToTheConstructor()
            {
                this.bootstrapper.ServiceLocator.Should().BeSameAs(this.serviceLocator);
            }

            [Fact]
            public void ShouldThrowIfDisposed()
            {
                this.bootstrapper.Dispose();

                this.bootstrapper.Invoking(b => _ = b.ServiceLocator)
                    .Should().Throw<ObjectDisposedException>();
            }
        }

        public sealed class ThrowIfDisposed : BootstrapperTests
        {
            [Fact]
            public void ShouldIncludeTheDerivedClassName()
            {
                this.bootstrapper.Dispose();

                this.bootstrapper.Invoking(b => b.ThrowIfDisposed())
                    .Should().Throw<ObjectDisposedException>()
                    .And.ObjectName.Should().Be(nameof(FakeBootstrapper));
            }
        }

        internal class FakeClass : IFakeInterface
        {
        }

        private class FakeBootstrapper : Bootstrapper
        {
            internal FakeBootstrapper(IServiceLocator locator)
                : base(locator)
            {
            }

            internal new bool IsDisposed => base.IsDisposed;

            internal new void Initialize()
            {
                base.Initialize();
            }

            internal new void ThrowIfDisposed()
            {
                base.ThrowIfDisposed();
            }
        }
    }
}
