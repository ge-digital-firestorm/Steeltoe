// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using Steeltoe.Common;

namespace Steeltoe.Messaging.Support;

public static class MessageBuilder
{
    public static AbstractMessageBuilder FromMessage<TPayload>(IMessage<TPayload> message)
    {
        return new MessageBuilder<TPayload>(message);
    }

    public static AbstractMessageBuilder FromMessage(IMessage message, Type payloadType = null)
    {
        Type genParamType = GetGenericParamType(message, payloadType);
        Type typeToCreate = typeof(MessageBuilder<>).MakeGenericType(genParamType);

        return (AbstractMessageBuilder)Activator.CreateInstance(typeToCreate, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null,
            new object[]
            {
                message
            }, null, null);
    }

    public static AbstractMessageBuilder WithPayload<TPayload>(TPayload payload)
    {
        return new MessageBuilder<TPayload>(payload, new MessageHeaderAccessor());
    }

    public static AbstractMessageBuilder WithPayload(object payload, Type payloadType = null)
    {
        Type genParamType = GetGenericParamType(payload, payloadType);
        Type typeToCreate = typeof(MessageBuilder<>).MakeGenericType(genParamType);

        return (AbstractMessageBuilder)Activator.CreateInstance(typeToCreate, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, new[]
        {
            payload,
            new MessageHeaderAccessor()
        }, null, null);
    }

    public static IMessage<TPayload> CreateMessage<TPayload>(TPayload payload, IMessageHeaders messageHeaders)
    {
        return (IMessage<TPayload>)CreateMessage(payload, messageHeaders, typeof(TPayload));
    }

    public static IMessage CreateMessage(object payload, IMessageHeaders messageHeaders, Type payloadType = null)
    {
        ArgumentGuard.NotNull(payload);
        ArgumentGuard.NotNull(messageHeaders);

        return Message.Create(payload, messageHeaders, payloadType);
    }

    public static Type GetGenericParamType(IMessage target, Type messagePayloadType)
    {
        if (target == null && messagePayloadType == null)
        {
            return typeof(object);
        }

        if (messagePayloadType != null)
        {
            return messagePayloadType;
        }

        Type targetType = target.GetType();

        if (targetType.IsGenericType)
        {
            return targetType.GetGenericArguments()[0];
        }

        return typeof(object);
    }

    public static Type GetGenericParamType(object payload, Type messagePayloadType)
    {
        if (payload == null && messagePayloadType == null)
        {
            return typeof(object);
        }

        if (messagePayloadType != null)
        {
            return messagePayloadType;
        }

        return payload.GetType();
    }
}

public class MessageBuilder<TPayload> : AbstractMessageBuilder
{
    protected internal MessageBuilder()
    {
    }

    protected internal MessageBuilder(IMessage<TPayload> message)
        : base(message)
    {
    }

    protected internal MessageBuilder(IMessage message)
        : base(message)
    {
    }

    protected internal MessageBuilder(MessageHeaderAccessor accessor)
        : base(accessor)
    {
    }

    protected internal MessageBuilder(TPayload payload, MessageHeaderAccessor accessor)
        : base(payload, accessor)
    {
    }

    public override AbstractMessageBuilder SetHeaders(MessageHeaderAccessor accessor)
    {
        ArgumentGuard.NotNull(accessor);

        headerAccessor = accessor;
        return this;
    }

    public override AbstractMessageBuilder SetHeader(string headerName, object headerValue)
    {
        headerAccessor.SetHeader(headerName, headerValue);
        return this;
    }

    public override AbstractMessageBuilder SetHeaderIfAbsent(string headerName, object headerValue)
    {
        headerAccessor.SetHeaderIfAbsent(headerName, headerValue);
        return this;
    }

    public override AbstractMessageBuilder RemoveHeaders(params string[] headerPatterns)
    {
        headerAccessor.RemoveHeaders(headerPatterns);
        return this;
    }

    public override AbstractMessageBuilder RemoveHeader(string headerName)
    {
        headerAccessor.RemoveHeader(headerName);
        return this;
    }

    public override AbstractMessageBuilder CopyHeaders(IDictionary<string, object> headersToCopy)
    {
        headerAccessor.CopyHeaders(headersToCopy);
        return this;
    }

    public override AbstractMessageBuilder CopyHeadersIfAbsent(IDictionary<string, object> headersToCopy)
    {
        headerAccessor.CopyHeadersIfAbsent(headersToCopy);
        return this;
    }

    public override AbstractMessageBuilder SetReplyChannel(IMessageChannel replyChannel)
    {
        headerAccessor.ReplyChannel = replyChannel;
        return this;
    }

    public override AbstractMessageBuilder SetReplyChannelName(string replyChannelName)
    {
        headerAccessor.ReplyChannelName = replyChannelName;
        return this;
    }

    public override AbstractMessageBuilder SetErrorChannel(IMessageChannel errorChannel)
    {
        headerAccessor.ErrorChannel = errorChannel;
        return this;
    }

    public override AbstractMessageBuilder SetErrorChannelName(string errorChannelName)
    {
        headerAccessor.ErrorChannelName = errorChannelName;
        return this;
    }

    public new IMessage<TPayload> Build()
    {
        return (IMessage<TPayload>)base.Build();
    }
}
