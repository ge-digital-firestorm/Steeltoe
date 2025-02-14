// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Steeltoe.Messaging;
using Steeltoe.Stream.Binder;

namespace Steeltoe.Stream.Binding;

public class CompositeMessageChannelConfigurer : IMessageChannelAndSourceConfigurer
{
    private readonly List<IMessageChannelConfigurer> _messageChannelConfigurers;

    public CompositeMessageChannelConfigurer(IEnumerable<IMessageChannelConfigurer> messageChannelConfigurers)
    {
        _messageChannelConfigurers = messageChannelConfigurers.ToList();
    }

    public void ConfigureInputChannel(IMessageChannel messageChannel, string channelName)
    {
        foreach (IMessageChannelConfigurer messageChannelConfigurer in _messageChannelConfigurers)
        {
            messageChannelConfigurer.ConfigureInputChannel(messageChannel, channelName);
        }
    }

    public void ConfigureOutputChannel(IMessageChannel messageChannel, string channelName)
    {
        foreach (IMessageChannelConfigurer messageChannelConfigurer in _messageChannelConfigurers)
        {
            messageChannelConfigurer.ConfigureOutputChannel(messageChannel, channelName);
        }
    }

    public void ConfigurePolledMessageSource(IPollableMessageSource binding, string name)
    {
        foreach (IMessageChannelConfigurer channelConfigurer in _messageChannelConfigurers)
        {
            if (channelConfigurer is IMessageChannelAndSourceConfigurer configurer)
            {
                configurer.ConfigurePolledMessageSource(binding, name);
            }
        }
    }
}
