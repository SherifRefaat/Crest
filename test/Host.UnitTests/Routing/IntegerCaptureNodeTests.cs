﻿namespace Host.UnitTests.Routing
{
    using System;
    using Crest.Host;
    using Crest.Host.Routing;
    using FluentAssertions;
    using NSubstitute;
    using Xunit;

    public class IntegerCaptureNodeTests
    {
        private const string Parameter = "parameter";
        private readonly IntegerCaptureNode node = new IntegerCaptureNode(Parameter, typeof(int));

        public sealed class Constructor : IntegerCaptureNodeTests
        {
            [Fact]
            public void ShouldThrowForInvalidIntegerTypes()
            {
                Action action = () => new IntegerCaptureNode("", typeof(Guid));
                action.Should().Throw<ArgumentException>();

                action = () => new IntegerCaptureNode("", typeof(IntegerCaptureNodeTests));
                action.Should().Throw<ArgumentException>();
            }
        }

        public sealed new class Equals : IntegerCaptureNodeTests
        {
            [Fact]
            public void ShouldReturnFalseForDifferentParameters()
            {
                var other = new IntegerCaptureNode(Parameter + "New", typeof(int));
                this.node.Equals(other).Should().BeFalse();
            }

            [Fact]
            public void ShouldReturnFalseForDifferentTypes()
            {
                var other = new IntegerCaptureNode(Parameter, typeof(short));
                this.node.Equals(other).Should().BeFalse();
            }

            [Fact]
            public void ShouldReturnFalseForNonIntegerCaptureNodes()
            {
                IMatchNode other = Substitute.For<IMatchNode>();
                this.node.Equals(other).Should().BeFalse();
            }

            [Fact]
            public void ShouldReturnTrueForTheSameParameter()
            {
                var other = new IntegerCaptureNode(Parameter, typeof(int));
                this.node.Equals(other).Should().BeTrue();
            }
        }

        public sealed class Match : IntegerCaptureNodeTests
        {
            [Theory]
            [InlineData("123", 123)]
            [InlineData("+123", 123)]
            [InlineData("-123", -123)]
            public void ShouldMatchValidIntegers(string integer, int expected)
            {
                NodeMatchResult result = this.node.Match(
                    new StringSegment("/" + integer + "/", 1, integer.Length + 1));

                result.Success.Should().BeTrue();
                result.Name.Should().Be(Parameter);
                ((int)result.Value).Should().Be(expected);
            }

            [Fact]
            public void ShouldNotMatchInvalidIntegers()
            {
                NodeMatchResult result = this.node.Match(
                    new StringSegment("/ABC/", 1, 4));

                result.Success.Should().BeFalse();
            }
        }

        public sealed class ParameterName : IntegerCaptureNodeTests
        {
            [Fact]
            public void ShouldReturnTheValuePassedInTheConstructor()
            {
                this.node.ParameterName.Should().Be(Parameter);
            }
        }

        public sealed class Priority : IntegerCaptureNodeTests
        {
            [Fact]
            public void ShouldReturnAPositiveValue()
            {
                this.node.Priority.Should().BePositive();
            }
        }

        public sealed class TryConvertValue : IntegerCaptureNodeTests
        {
            [Fact]
            public void ShouldReturnFalseForInvalidValues()
            {
                bool result = this.node.TryConvertValue(
                    new StringSegment("invalid"),
                    out object value);

                result.Should().BeFalse();
                value.Should().BeNull();
            }

            [Fact]
            public void ShouldReturnTrueForValidValues()
            {
                bool result = this.node.TryConvertValue(
                    new StringSegment("123"),
                    out object value);

                result.Should().BeTrue();
                value.Should().Be(123);
            }
        }
    }
}
