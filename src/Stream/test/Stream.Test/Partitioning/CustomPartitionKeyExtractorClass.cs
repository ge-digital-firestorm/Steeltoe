// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Steeltoe.Messaging;
using Steeltoe.Stream.Binder;

namespace Steeltoe.Stream.Test.Partitioning;

public sealed class CustomPartitionKeyExtractorClass : IPartitionKeyExtractorStrategy
{
    public string ServiceName { get; set; }

    public CustomPartitionKeyExtractorClass()
    {
        ServiceName = GetType().Name;
    }

    public object ExtractKey(IMessage message)
    {
        return message.Headers.Get<string>("key");
    }
}
