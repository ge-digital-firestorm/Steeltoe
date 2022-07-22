﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Configuration;
using Steeltoe.Common;

namespace Steeltoe.Management.Endpoint.HeapDump;

public class HeapDumpEndpointOptions : AbstractEndpointOptions, IHeapDumpOptions
{
    private const string MANAGEMENT_INFO_PREFIX = "management:endpoints:heapdump";

    public HeapDumpEndpointOptions()
        : base()
    {
        Id = "heapdump";
    }

    public HeapDumpEndpointOptions(IConfiguration config)
        : base(MANAGEMENT_INFO_PREFIX, config)
    {
        if (string.IsNullOrEmpty(Id))
        {
            Id = "heapdump";
        }
    }

    public string HeapDumpType { get; set; }

    // Default to disabled on Linux + Cloud Foundry until PTRACE is allowed
    public override bool DefaultEnabled { get; } = !(Platform.IsCloudFoundry && Platform.IsLinux);
}