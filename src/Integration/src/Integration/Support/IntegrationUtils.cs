// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Steeltoe.Messaging;

namespace Steeltoe.Integration.Support;

public static class IntegrationUtils
{
    public const string IntegrationConversionServiceBeanName = "integrationConversionService";

    public const string IntegrationMessageBuilderFactoryBeanName = "messageBuilderFactory";

    public static Exception WrapInDeliveryExceptionIfNecessary(IMessage message, string text, Exception e)
    {
        if (e is not MessagingException me)
        {
            return new MessageDeliveryException(message, text, e);
        }

        if (me.FailedMessage == null)
        {
            return new MessageDeliveryException(message, text, e);
        }

        return me;
    }

    public static Exception WrapInHandlingExceptionIfNecessary(IMessage message, string text, Exception e)
    {
        if (e is not MessagingException me)
        {
            return new MessageHandlingException(message, text, e);
        }

        if (me.FailedMessage == null)
        {
            return new MessageHandlingException(message, text, e);
        }

        return me;
    }
}
