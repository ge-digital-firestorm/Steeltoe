// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using Steeltoe.Common.Contexts;
using Steeltoe.Common.Retry;
using Steeltoe.Common.Util;
using Steeltoe.Integration;
using Steeltoe.Integration.Endpoint;
using Steeltoe.Integration.Handler;
using Steeltoe.Integration.Support;
using Steeltoe.Messaging;
using Steeltoe.Stream.Binder;
using Steeltoe.Stream.Configuration;
using Steeltoe.Stream.Provisioning;
using static Steeltoe.Stream.TestBinder.TestChannelBinderProvisioner;

namespace Steeltoe.Stream.TestBinder;

public sealed class TestChannelBinder : AbstractPollableMessageSourceBinder
{
    private readonly ILogger _logger;

    public IMessage LastError { get; private set; }

    public override string ServiceName { get; set; } = "testbinder";

    public IMessageSource MessageSourceDelegate { get; set; } = new MessageSource();

    public TestChannelBinder(IApplicationContext context, TestChannelBinderProvisioner provisioningProvider, ILogger<TestChannelBinder> logger)
        : base(context, Array.Empty<string>(), provisioningProvider, logger)
    {
        _logger = logger;
    }

    protected override IMessageHandler CreateProducerMessageHandler(IProducerDestination destination, IProducerOptions producerProperties,
        IMessageChannel errorChannel)
    {
        var handler = new BridgeHandler(ApplicationContext)
        {
            OutputChannel = ((SpringIntegrationProducerDestination)destination).Channel
        };

        return handler;
    }

    protected override IMessageProducer CreateConsumerEndpoint(IConsumerDestination destination, string group, IConsumerOptions consumerOptions)
    {
        IErrorMessageStrategy errorMessageStrategy = new DefaultErrorMessageStrategy();
        ISubscribableChannel siBinderInputChannel = ((SpringIntegrationConsumerDestination)destination).Channel;

        var messageListenerContainer = new TestMessageListeningContainer();
        var endpoint = new TestMessageProducerSupportEndpoint(ApplicationContext, messageListenerContainer, _logger);

        string groupName = !string.IsNullOrEmpty(group) ? group : "anonymous";
        ErrorInfrastructure errorInfrastructure = RegisterErrorInfrastructure(destination, groupName, consumerOptions, _logger);

        if (consumerOptions.MaxAttempts > 1)
        {
            endpoint.RetryTemplate = BuildRetryTemplate(consumerOptions);
            endpoint.RecoveryCallback = errorInfrastructure.Recoverer;
        }
        else
        {
            endpoint.ErrorMessageStrategy = errorMessageStrategy;
            endpoint.ErrorChannel = errorInfrastructure.ErrorChannel;
        }

        endpoint.Init();

        siBinderInputChannel.Subscribe(messageListenerContainer);

        return endpoint;
    }

    protected override IMessageHandler GetErrorMessageHandler(IConsumerDestination destination, string group, IConsumerOptions consumerOptions)
    {
        return new ErrorMessageHandler(this);
    }

    protected override PolledConsumerResources CreatePolledConsumerResources(string name, string group, IConsumerDestination destination,
        IConsumerOptions consumerOptions)
    {
        return new PolledConsumerResources(MessageSourceDelegate, RegisterErrorInfrastructure(destination, group, consumerOptions, _logger));
    }

    public sealed class ErrorMessageHandler : ILastSubscriberMessageHandler
    {
        public TestChannelBinder Binder { get; }

        public string ServiceName { get; set; }

        public ErrorMessageHandler(TestChannelBinder binder)
        {
            Binder = binder;
            ServiceName = $"{GetType().Name}@{GetHashCode()}";
        }

        public void HandleMessage(IMessage message)
        {
            Binder.LastError = message;
        }
    }

    public sealed class MessageSource : IMessageSource
    {
        public IMessage Receive()
        {
            IMessage<string> message = Message.Create("polled data", new MessageHeaders(new Dictionary<string, object>
            {
                { MessageHeaders.ContentType, "text/plain" }
            }));

            return message;
        }
    }

    public sealed class TestMessageListeningContainer : IMessageHandler
    {
        public string ServiceName { get; set; }

        public Action<IMessage> MessageListener { get; set; }

        public TestMessageListeningContainer()
        {
            ServiceName = $"{GetType().Name}@{GetHashCode()}";
        }

        public void HandleMessage(IMessage message)
        {
            MessageListener.Invoke(message);
        }
    }

    public sealed class TestMessageProducerSupportEndpoint : MessageProducerSupportEndpoint
    {
        private static readonly AsyncLocal<IAttributeAccessor> AttributesHolder = new();
        private readonly TestMessageListeningContainer _messageListenerContainer;

        public RetryTemplate RetryTemplate { get; set; }

        public IRecoveryCallback RecoveryCallback { get; set; }

        public TestMessageProducerSupportEndpoint(IApplicationContext context, TestMessageListeningContainer messageListenerContainer, ILogger logger)
            : base(context, logger)
        {
            _messageListenerContainer = messageListenerContainer;
        }

        public void Init()
        {
            if (RetryTemplate != null && ErrorChannel != null)
            {
                throw new InvalidOperationException("Cannot have an 'errorChannel' property when a 'RetryTemplate' is " +
                    "provided; use an 'ErrorMessageSendingRecoverer' in the 'recoveryCallback' property to " +
                    "send an error message when retries are exhausted");
            }

            var messageListener = new Listener(this);

            if (RetryTemplate != null)
            {
                RetryTemplate.RegisterListener(messageListener);
            }

            _messageListenerContainer.MessageListener = m => messageListener.Accept(m);
        }

        private sealed class Listener : IRetryListener
        {
            private readonly TestMessageProducerSupportEndpoint _adapter;

            public Listener(TestMessageProducerSupportEndpoint adapter)
            {
                _adapter = adapter;
            }

            public void Accept(IMessage message)
            {
                try
                {
                    if (_adapter.RetryTemplate == null)
                    {
                        try
                        {
                            ProcessMessage(message);
                        }
                        finally
                        {
                            AttributesHolder.Value = null;
                        }
                    }
                    else
                    {
                        _adapter.RetryTemplate.Execute(_ => ProcessMessage(message), _adapter.RecoveryCallback);
                    }
                }
                catch (Exception e)
                {
                    if (_adapter.ErrorChannel != null)
                    {
                        _adapter.MessagingTemplate.Send(_adapter.ErrorChannel,
                            _adapter.BuildErrorMessage(null, new InvalidOperationException($"Message conversion failed: {message}", e)));
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            public bool Open(IRetryContext context)
            {
                if (_adapter.RecoveryCallback != null)
                {
                    AttributesHolder.Value = context;
                }

                return true;
            }

            public void Close(IRetryContext context, Exception exception)
            {
                AttributesHolder.Value = null;
            }

            public void OnError(IRetryContext context, Exception exception)
            {
                // Ignore
            }

            private void ProcessMessage(IMessage message)
            {
                _adapter.SendMessage(message);
            }
        }
    }
}
