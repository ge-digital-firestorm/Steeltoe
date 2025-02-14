// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace Steeltoe.Stream.Attributes;

/// <summary>
/// Indicates that an output binding target will be created by the framework.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Method)]
public sealed class OutputAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the binding target name; used as a name for binding target and as a destination name by default.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OutputAttribute" /> class.
    /// </summary>
    /// <param name="name">
    /// the binding target name.
    /// </param>
    public OutputAttribute(string name = null)
    {
        Name = name;
    }
}
