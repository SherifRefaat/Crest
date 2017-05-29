﻿namespace Host.UnitTests.Engine
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;
    using Crest.Core;
    using Crest.Host.Diagnostics;
    using Crest.Host.Engine;
    using FluentAssertions;
    using NSubstitute;
    using Xunit;

    public class DiscoveryServiceTests
    {
        private readonly ExecutingAssembly executingAssembly;
        private readonly DiscoveryService service;

        public DiscoveryServiceTests()
        {
            this.executingAssembly = Substitute.For<ExecutingAssembly>();
            this.executingAssembly.LoadCompileLibraries()
                .Returns(new[] { typeof(DiscoveryServiceTests).GetTypeInfo().Assembly });

            this.service = new DiscoveryService(this.executingAssembly);
        }

        public class FakeTypeFactory : ITypeFactory
        {
            public bool CanCreate(Type type)
            {
                return false;
            }

            public object Create(Type type, IServiceProvider serviceProvider)
            {
                return null;
            }
        }

        public sealed class GetCustomFactories : DiscoveryServiceTests
        {
            [Fact]
            public void ShouldReturnInstancesOfTheITypeFactory()
            {
                IEnumerable<ITypeFactory> factories = this.service.GetCustomFactories();

                factories.Should().Contain(f => f is FakeTypeFactory);
            }
        }

        public sealed class GetDiscoveredTypes : DiscoveryServiceTests
        {
            [Fact]
            public void ShouldExcludeDryIocTypes()
            {
                this.executingAssembly.LoadCompileLibraries()
                    .Returns(new[] { typeof(DryIoc.Container).GetTypeInfo().Assembly });

                IEnumerable<Type> types = this.service.GetDiscoveredTypes();

                types.Should().NotContain(typeof(DryIoc.Container));
            }

            [Fact]
            public void ShouldIncludeInternalTypes()
            {
                IEnumerable<Type> types = this.service.GetDiscoveredTypes();

                types.Should().Contain(typeof(ExampleInternalClass));
            }

            [Fact]
            public void ShouldIncludeTypesWithNoNamespace()
            {
                IEnumerable<Type> types = this.service.GetDiscoveredTypes();

                types.Should().Contain(typeof(ClassWithNoNameSpace));
            }

            [Fact]
            public void ShouldIncludeTypesWithSingleNamespace()
            {
                IEnumerable<Type> types = this.service.GetDiscoveredTypes();

                types.Should().Contain(typeof(SingleNamespace.ClassWithSingleNamespace));
            }

            internal class ExampleInternalClass
            {
            }
        }

        public sealed class GetRoutes : DiscoveryServiceTests
        {
            internal interface IHasRoutes
            {
                [Delete("Route")]
                [Version(1)]
                Task DeleteMethod();

                [Get("Route1")]
                [Get("Route2")]
                [Version(1)]
                Task MultipleRoutes();

                [Get("Version")]
                [Version(2, 3)]
                Task VersionedRoute();
            }

            private interface IInvalidMissingVersion
            {
                [Get("Route")]
                Task NoVersion();
            }

            private interface IInvalidMixedAttributes
            {
                [Delete("Route1")]
                [Get("Route2")]
                [Version(1)]
                Task DifferentVerbs();
            }

            private interface IInvalidVersionRange
            {
                [Get("Route")]
                [Version(5, 3)]
                Task InvalidVersionRange();
            }

            [Fact]
            public void ShouldNotAllowAMethodWithAMinimumVersionAfterItsMaximumVersion()
            {
                // Use ToList to force evaluation
                Action action = () => this.service.GetRoutes(typeof(IInvalidVersionRange)).ToList();

                action.ShouldThrow<InvalidOperationException>();
            }

            [Fact]
            public void ShouldNotAllowAMethodWithDifferentVerbs()
            {
                // Use ToList to force evaluation
                Action action = () => this.service.GetRoutes(typeof(IInvalidMixedAttributes)).ToList();

                action.ShouldThrow<InvalidOperationException>();
            }

            [Fact]
            public void ShouldNotAllowAMethodWithoutAVersion()
            {
                // Use ToList to force evaluation
                Action action = () => this.service.GetRoutes(typeof(IInvalidMissingVersion)).ToList();

                action.ShouldThrow<InvalidOperationException>();
            }

            [Fact]
            public void ShouldReturnAllTheRoutesOnAMethod()
            {
                IEnumerable<string> routes =
                    this.service.GetRoutes(typeof(IHasRoutes))
                        .Where(rm => rm.Method.Name == nameof(IHasRoutes.MultipleRoutes))
                        .Select(rm => rm.RouteUrl);

                routes.Should().BeEquivalentTo("Route1", "Route2");
            }

            [Fact]
            public void ShouldReturnTheVerb()
            {
                RouteMetadata metadata =
                    this.service.GetRoutes(typeof(IHasRoutes))
                        .Where(rm => rm.Method.Name == nameof(IHasRoutes.DeleteMethod))
                        .Single();

                metadata.Verb.Should().BeEquivalentTo("DELETE");
            }

            [Fact]
            public void ShouldReturnTheVersionInformation()
            {
                RouteMetadata metadata =
                    this.service.GetRoutes(typeof(IHasRoutes))
                        .Where(rm => rm.Method.Name == nameof(IHasRoutes.VersionedRoute))
                        .Single();

                metadata.MinimumVersion.Should().Be(2);
                metadata.MaximumVersion.Should().Be(3);
            }
        }

        public sealed class IsSingleInstance : DiscoveryServiceTests
        {
            [Fact]
            public void ShouldReturnFalse()
            {
                // Out of the box everything is created per request
                bool result = this.service.IsSingleInstance(typeof(DiscoveryServiceTests));

                result.Should().BeFalse();
            }
        }
    }
}

namespace SingleNamespace
{
    internal class ClassWithSingleNamespace
    {
    }
}

internal class ClassWithNoNameSpace
{
}
