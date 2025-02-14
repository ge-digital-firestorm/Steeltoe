// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace Steeltoe.Messaging.RabbitMQ.Connection;

public class PendingConfirm
{
    public CorrelationData CorrelationInfo { get; }

    public long Timestamp { get; }

    public string Cause { get; set; }

    public PendingConfirm(CorrelationData correlationData, long timestamp)
    {
        CorrelationInfo = correlationData;
        Timestamp = timestamp;
    }

    public override string ToString()
    {
        return $"PendingConfirm [correlationInfo={CorrelationInfo}{(Cause == null ? string.Empty : $" cause={Cause}")}]";
    }
}
