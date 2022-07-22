﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace Steeltoe.Management.Endpoint.Hypermedia;

public class Link
{
    public string Href { get; set; }

    public bool Templated { get; }

    public Link()
    {
    }

    public Link(string href)
    {
        Href = href;
        Templated = href.Contains("{");
    }
}