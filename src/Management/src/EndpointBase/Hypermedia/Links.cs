// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace Steeltoe.Management.Endpoint.Hypermedia;

/// <summary>
/// A typed collection of links.
/// </summary>
public class Links
{
    /// <summary>
    /// Gets or sets the type of links contained in this collection.
    /// </summary>
    public string Type { get; set; } = "steeltoe";

    /// <summary>
    /// Gets or sets the list of links contained in this collection.
    /// </summary>

    // ReSharper disable once InconsistentNaming
#pragma warning disable SA1300 // Element should begin with upper-case letter
#pragma warning disable S100 // Methods and properties should be named in PascalCase
#pragma warning disable IDE1006 // Naming Styles
    public Dictionary<string, Link> _links { get; set; } = new();
#pragma warning restore IDE1006 // Naming Styles
#pragma warning restore S100 // Methods and properties should be named in PascalCase
#pragma warning restore SA1300 // Element should begin with upper-case letter
}
