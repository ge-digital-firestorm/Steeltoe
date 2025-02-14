// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using Steeltoe.Common;
using Steeltoe.Common.Contexts;
using Steeltoe.Integration.Dispatcher;
using Steeltoe.Messaging;

namespace Steeltoe.Integration.Channel;

public abstract class AbstractSubscribableChannel : AbstractMessageChannel, ISubscribableChannel
{
    public virtual int SubscriberCount => Dispatcher.HandlerCount;

    public virtual int MaxSubscribers
    {
        get => Dispatcher.MaxSubscribers;
        set => Dispatcher.MaxSubscribers = value;
    }

    public virtual bool Failover
    {
        get => Dispatcher.Failover;
        set => Dispatcher.Failover = value;
    }

    public IMessageDispatcher Dispatcher { get; }

    protected AbstractSubscribableChannel(IApplicationContext context, IMessageDispatcher dispatcher, ILogger logger = null)
        : this(context, dispatcher, null, logger)
    {
    }

    protected AbstractSubscribableChannel(IApplicationContext context, IMessageDispatcher dispatcher, string name, ILogger logger = null)
        : base(context, name, logger)
    {
        ArgumentGuard.NotNull(dispatcher);

        Dispatcher = dispatcher;
    }

    public virtual bool Subscribe(IMessageHandler handler)
    {
        bool added = Dispatcher.AddHandler(handler);

        if (added)
        {
            Logger?.LogTrace("Channel '{channel}' has handler {name}.", ServiceName, handler.ServiceName);
            Logger?.LogInformation("Channel '{channel}' has {count} subscriber(s).", ServiceName, Dispatcher.HandlerCount);
        }

        return added;
    }

    public virtual bool Unsubscribe(IMessageHandler handler)
    {
        bool removed = Dispatcher.RemoveHandler(handler);

        if (removed)
        {
            Logger?.LogInformation("Channel '{channel}' has {count} subscriber(s).", ServiceName, Dispatcher.HandlerCount);
        }

        return removed;
    }

    protected override bool DoSendInternal(IMessage message, CancellationToken cancellationToken)
    {
        try
        {
            return Dispatcher.Dispatch(message, cancellationToken);
        }
        catch (MessageDispatchingException e)
        {
            string description = $"{e.Message} for channel '{ServiceName}'.";
            throw new MessageDeliveryException(message, description, e);
        }
    }
}
