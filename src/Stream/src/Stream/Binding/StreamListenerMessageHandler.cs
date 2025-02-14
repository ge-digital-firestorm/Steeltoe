// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Steeltoe.Common.Contexts;
using Steeltoe.Integration.Handler;
using Steeltoe.Messaging;
using Steeltoe.Messaging.Handler.Invocation;

namespace Steeltoe.Stream.Binding;

public class StreamListenerMessageHandler : AbstractReplyProducingMessageHandler
{
    private readonly IInvocableHandlerMethod _invocableHandlerMethod;

    protected override bool ShouldCopyRequestHeaders { get; }

    public bool IsVoid => _invocableHandlerMethod.IsVoid;

    public StreamListenerMessageHandler(IApplicationContext context, IInvocableHandlerMethod invocableHandlerMethod, bool copyHeaders,
        IList<string> notPropagatedHeaders)
        : base(context)
    {
        _invocableHandlerMethod = invocableHandlerMethod;
        ShouldCopyRequestHeaders = copyHeaders;
        NotPropagatedHeaders = notPropagatedHeaders;
    }

    public override void Initialize()
    {
        // Intentionally left empty.
    }

    protected override object HandleRequestMessage(IMessage requestMessage)
    {
        try
        {
            object result = _invocableHandlerMethod.Invoke(requestMessage);
            return result;
        }
        catch (Exception e)
        {
            if (e is MessagingException)
            {
                throw;
            }

            throw new MessagingException(requestMessage, $"Exception thrown while invoking {_invocableHandlerMethod.ShortLogMessage}", e);
        }
    }
}
