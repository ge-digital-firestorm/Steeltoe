// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Steeltoe.Stream.Binder;

namespace Steeltoe.Stream.Test.Partitioning;

public sealed class CustomPartitionSelectorClass : IPartitionSelectorStrategy
{
    public string ServiceName { get; set; }

    public CustomPartitionSelectorClass()
    {
        ServiceName = GetType().Name;
    }

    public int SelectPartition(object key, int partitionCount)
    {
        return int.Parse((string)key, CultureInfo.InvariantCulture);
    }
}
