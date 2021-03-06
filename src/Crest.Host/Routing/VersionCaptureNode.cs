﻿// Copyright (c) Samuel Cragg.
//
// Licensed under the MIT license. See LICENSE file in the project root for
// full license information.

namespace Crest.Host.Routing
{
    using System;
    using Crest.Host.Conversion;

    /// <summary>
    /// Allows the capturing of information from the route and converting it
    /// with the types default TypeConverter.
    /// </summary>
    internal class VersionCaptureNode : IMatchNode
    {
        /// <summary>
        /// Gets the key that stores the version information.
        /// </summary>
        internal const string KeyName = "__version__";

        /// <inheritdoc />
        public int Priority => 1;

        /// <inheritdoc />
        string IQueryValueConverter.ParameterName => throw new NotSupportedException();

        /// <inheritdoc />
        public bool Equals(IMatchNode other)
        {
            return other is VersionCaptureNode;
        }

        /// <inheritdoc />
        public NodeMatchResult Match(StringSegment segment)
        {
            if (segment.Count > 1)
            {
                char v = segment[0];
                if ((v == 'v') || (v == 'V'))
                {
                    segment = new StringSegment(segment.String, segment.Start + 1, segment.End);
                    ParseResult<long> result = IntegerConverter.TryReadSignedInt(
                        segment.CreateSpan(),
                        int.MinValue,
                        int.MaxValue);

                    if (result.IsSuccess)
                    {
                        return new NodeMatchResult(KeyName, (int)result.Value);
                    }
                }
            }

            return NodeMatchResult.None;
        }

        /// <inheritdoc />
        bool IQueryValueConverter.TryConvertValue(StringSegment value, out object result)
        {
            throw new NotSupportedException();
        }
    }
}
