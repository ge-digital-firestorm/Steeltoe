// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using RabbitMQ.Client;
using Steeltoe.Messaging;
using Steeltoe.Messaging.RabbitMQ.Listener.Exceptions;

namespace Steeltoe.Integration.RabbitMQ.Support;

public class ManualAckListenerExecutionFailedException : ListenerExecutionFailedException
{
    public IModel Channel { get; }
    public ulong DeliveryTag { get; }

    public ManualAckListenerExecutionFailedException(string message, Exception innerException, IMessage failedMessage, IModel channel, ulong deliveryTag)
        : base(message, innerException, failedMessage)
    {
        Channel = channel;
        DeliveryTag = deliveryTag;
    }
}
