// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client.Exceptions;
using Steeltoe.Common.Transaction;
using Steeltoe.Common.Util;
using Steeltoe.Messaging.RabbitMQ.Connection;
using Steeltoe.Messaging.RabbitMQ.Core;
using Steeltoe.Messaging.RabbitMQ.Exceptions;
using Steeltoe.Messaging.RabbitMQ.Extensions;
using Steeltoe.Messaging.RabbitMQ.Listener.Exceptions;
using Steeltoe.Messaging.RabbitMQ.Listener.Support;
using Steeltoe.Messaging.RabbitMQ.Support;
using Steeltoe.Messaging.RabbitMQ.Util;
using RC = RabbitMQ.Client;

namespace Steeltoe.Messaging.RabbitMQ.Listener;

public class BlockingQueueConsumer
{
    private const int DefaultDeclarationRetries = 3;
    private const int DefaultRetryDeclarationInterval = 60000;

    private ConcurrentDictionary<string, InternalConsumer> Consumers { get; } = new();

    protected bool HasDelivery => Queue.Count != 0;

    protected bool Cancelled =>
        Cancel.Value || (AbortStarted > 0 && AbortStarted + ShutdownTimeout > DateTimeOffset.Now.ToUnixTimeMilliseconds()) || !ActiveObjectCounter.IsActive;

    public ILogger<BlockingQueueConsumer> Logger { get; }

    public IMessageHeadersConverter MessageHeadersConverter { get; set; }

    public BlockingCollection<Delivery> Queue { get; }

    public ILoggerFactory LoggerFactory { get; }

    public IConnectionFactory ConnectionFactory { get; }

    public ActiveObjectCounter<BlockingQueueConsumer> ActiveObjectCounter { get; }

    public bool Transactional { get; }

    public ushort PrefetchCount { get; }

    public List<string> Queues { get; }

    public AcknowledgeMode AcknowledgeMode { get; }

    public RC.IModel Channel { get; internal set; }

    public bool Exclusive { get; }

    public bool NoLocal { get; }

    public AtomicBoolean Cancel { get; } = new(false);

    public bool DefaultRequeueRejected { get; }

    public Dictionary<string, object> ConsumerArgs { get; } = new();

    public RC.ShutdownEventArgs Shutdown { get; private set; }

    public HashSet<ulong> DeliveryTags { get; internal set; } = new();

    public long AbortStarted { get; private set; }

    public bool NormalCancel { get; set; }

    public bool Declaring { get; set; }

    public int ShutdownTimeout { get; set; }

    public int DeclarationRetries { get; set; } = DefaultDeclarationRetries;

    public int FailedDeclarationRetryInterval { get; set; } = AbstractMessageListenerContainer.DefaultFailedDeclarationRetryInterval;

    public int RetryDeclarationInterval { get; set; } = DefaultRetryDeclarationInterval;

    public IConsumerTagStrategy TagStrategy { get; set; }

    public IBackOffExecution BackOffExecution { get; set; }

    public bool LocallyTransacted { get; set; }

    public int QueueCount => Queues.Count;

    public HashSet<string> MissingQueues => new();

    public long LastRetryDeclaration { get; set; }

    public RabbitResourceHolder ResourceHolder { get; set; }

    public BlockingQueueConsumer(IConnectionFactory connectionFactory, IMessageHeadersConverter messagePropertiesConverter,
        ActiveObjectCounter<BlockingQueueConsumer> activeObjectCounter, AcknowledgeMode acknowledgeMode, bool transactional, ushort prefetchCount,
        ILoggerFactory loggerFactory, params string[] queues)
        : this(connectionFactory, messagePropertiesConverter, activeObjectCounter, acknowledgeMode, transactional, prefetchCount, true, loggerFactory, queues)
    {
    }

    public BlockingQueueConsumer(IConnectionFactory connectionFactory, IMessageHeadersConverter messagePropertiesConverter,
        ActiveObjectCounter<BlockingQueueConsumer> activeObjectCounter, AcknowledgeMode acknowledgeMode, bool transactional, ushort prefetchCount,
        bool defaultRequeueRejected, ILoggerFactory loggerFactory, params string[] queues)
        : this(connectionFactory, messagePropertiesConverter, activeObjectCounter, acknowledgeMode, transactional, prefetchCount, defaultRequeueRejected, null,
            loggerFactory, queues)
    {
    }

    public BlockingQueueConsumer(IConnectionFactory connectionFactory, IMessageHeadersConverter messagePropertiesConverter,
        ActiveObjectCounter<BlockingQueueConsumer> activeObjectCounter, AcknowledgeMode acknowledgeMode, bool transactional, ushort prefetchCount,
        bool defaultRequeueRejected, Dictionary<string, object> consumerArgs, ILoggerFactory loggerFactory, params string[] queues)
        : this(connectionFactory, messagePropertiesConverter, activeObjectCounter, acknowledgeMode, transactional, prefetchCount, defaultRequeueRejected,
            consumerArgs, false, loggerFactory, queues)
    {
    }

    public BlockingQueueConsumer(IConnectionFactory connectionFactory, IMessageHeadersConverter messagePropertiesConverter,
        ActiveObjectCounter<BlockingQueueConsumer> activeObjectCounter, AcknowledgeMode acknowledgeMode, bool transactional, ushort prefetchCount,
        bool defaultRequeueRejected, Dictionary<string, object> consumerArgs, bool exclusive, ILoggerFactory loggerFactory, params string[] queues)
        : this(connectionFactory, messagePropertiesConverter, activeObjectCounter, acknowledgeMode, transactional, prefetchCount, defaultRequeueRejected,
            consumerArgs, false, exclusive, loggerFactory, queues)
    {
    }

    public BlockingQueueConsumer(IConnectionFactory connectionFactory, IMessageHeadersConverter messagePropertiesConverter,
        ActiveObjectCounter<BlockingQueueConsumer> activeObjectCounter, AcknowledgeMode acknowledgeMode, bool transactional, ushort prefetchCount,
        bool defaultRequeueRejected, Dictionary<string, object> consumerArgs, bool noLocal, bool exclusive, ILoggerFactory loggerFactory,
        params string[] queues)
    {
        ConnectionFactory = connectionFactory;
        MessageHeadersConverter = messagePropertiesConverter;
        ActiveObjectCounter = activeObjectCounter;
        AcknowledgeMode = acknowledgeMode;
        Transactional = transactional;
        PrefetchCount = prefetchCount;
        DefaultRequeueRejected = defaultRequeueRejected;

        if (consumerArgs != null && consumerArgs.Count > 0)
        {
            foreach (KeyValuePair<string, object> arg in consumerArgs)
            {
                ConsumerArgs.Add(arg.Key, arg.Value);
            }
        }

        NoLocal = noLocal;
        Exclusive = exclusive;
        Queues = queues.ToList();
        Queue = new BlockingCollection<Delivery>(prefetchCount);
        LoggerFactory = loggerFactory;
        Logger = loggerFactory?.CreateLogger<BlockingQueueConsumer>();
    }

    public List<string> GetConsumerTags()
    {
        return Consumers.Values.Select(c => c.ConsumerTag).Where(tag => tag != null).ToList();
    }

    public void ClearDeliveryTags()
    {
        DeliveryTags.Clear();
    }

    public IMessage NextMessage()
    {
        Logger?.LogTrace("Retrieving delivery for: {consumer}", this);
        return Handle(Queue.Take());
    }

    public IMessage NextMessage(int timeout)
    {
        Logger?.LogTrace("Retrieving delivery for: {consumer}", this);
        CheckShutdown();

        if (MissingQueues.Count > 0)
        {
            CheckMissingQueues();
        }

        Queue.TryTake(out Delivery item, timeout);
        IMessage message = Handle(item);

        if (message == null && Cancel.Value)
        {
            throw new ConsumerCancelledException();
        }

        return message;
    }

    public void Start()
    {
        Logger?.LogDebug("Starting consumer {consumer}", this);

        try
        {
            ResourceHolder = ConnectionFactoryUtils.GetTransactionalResourceHolder(ConnectionFactory, Transactional);
            Channel = ResourceHolder.GetChannel();
        }
        catch (RabbitAuthenticationException e)
        {
            throw new FatalListenerStartupException("Authentication failure", e);
        }

        DeliveryTags.Clear();
        ActiveObjectCounter.Add(this);
        PassiveDeclarations();
        SetQosAndCreateConsumers();
    }

    public void Stop()
    {
        if (AbortStarted == 0)
        {
            AbortStarted = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        }

        if (!Cancelled)
        {
            try
            {
                RabbitUtils.CloseMessageConsumer(Channel, GetConsumerTags(), Transactional);
            }
            catch (Exception e)
            {
                Logger?.LogDebug(e, "Error closing consumer: {consumer}", this);
            }
        }

        Logger?.LogDebug("Closing Rabbit Channel : {channel}", Channel);
        RabbitUtils.SetPhysicalCloseRequired(Channel, true);
        ConnectionFactoryUtils.ReleaseResources(ResourceHolder);
        DeliveryTags.Clear();
        _ = Consumers.TakeWhile(_ => Consumers.Count > 0);
        _ = Queue.TakeWhile(_ => Queue.Count > 0);
    }

    public void RollbackOnExceptionIfNecessary(Exception ex)
    {
        bool ackRequired = !AcknowledgeMode.IsAutoAck() && (!AcknowledgeMode.IsManual() || ContainerUtils.IsRejectManual(ex));

        try
        {
            if (Transactional)
            {
                Logger?.LogDebug(ex, "Initiating transaction rollback on application exception");
                RabbitUtils.RollbackIfNecessary(Channel);
            }

            if (ackRequired)
            {
                if (DeliveryTags.Count > 0)
                {
                    ulong deliveryTag = DeliveryTags.Max();
                    Channel.BasicNack(deliveryTag, true, ContainerUtils.ShouldRequeue(DefaultRequeueRejected, ex, Logger));
                }

                if (Transactional)
                {
                    // Need to commit the reject (=nack)
                    RabbitUtils.CommitIfNecessary(Channel);
                }
            }
        }
        catch (Exception e)
        {
            Logger?.LogError(ex, "Application exception overridden by rollback exception");
            throw RabbitExceptionTranslator.ConvertRabbitAccessException(e);
        }
        finally
        {
            DeliveryTags.Clear();
        }
    }

    public bool CommitIfNecessary(bool localTx)
    {
        if (DeliveryTags.Count == 0)
        {
            return false;
        }

        bool isLocallyTransacted = localTx || (Transactional && TransactionSynchronizationManager.GetResource(ConnectionFactory) == null);

        try
        {
            bool ackRequired = !AcknowledgeMode.IsAutoAck() && !AcknowledgeMode.IsManual();

            if (ackRequired && (!Transactional || isLocallyTransacted))
            {
                ulong deliveryTag = new List<ulong>(DeliveryTags)[DeliveryTags.Count - 1];
                Channel.BasicAck(deliveryTag, true);
            }

            if (isLocallyTransacted)
            {
                // For manual acks we still need to commit
                RabbitUtils.CommitIfNecessary(Channel);
            }
        }
        finally
        {
            DeliveryTags.Clear();
        }

        return true;
    }

    public override string ToString()
    {
        return
            $"Consumer@{RuntimeHelpers.GetHashCode(this)}: tags=[{string.Join(',', GetConsumerTags())}], channel={Channel}, acknowledgeMode={AcknowledgeMode} local queue size={Queue.Count}";
    }

    internal List<RC.DefaultBasicConsumer> CurrentConsumers()
    {
        return Consumers.Values.ToList<RC.DefaultBasicConsumer>();
    }

    protected void BasicCancel()
    {
        BasicCancel(false);
    }

    protected void BasicCancel(bool expected)
    {
        NormalCancel = expected;

        GetConsumerTags().ForEach(consumerTag =>
        {
            if (Channel.IsOpen)
            {
                RabbitUtils.Cancel(Channel, consumerTag);
            }
        });

        Cancel.Value = true;
        AbortStarted = DateTimeOffset.Now.ToUnixTimeMilliseconds();
    }

    private void PassiveDeclarations()
    {
        // mirrored queue might be being moved
        int passiveDeclareRetries = DeclarationRetries;
        Declaring = true;

        do
        {
            if (Cancelled)
            {
                break;
            }

            try
            {
                AttemptPassiveDeclarations();

                if (passiveDeclareRetries < DeclarationRetries)
                {
                    Logger?.LogInformation("Queue declaration succeeded after retrying");
                }

                passiveDeclareRetries = 0;
            }
            catch (DeclarationException e)
            {
                HandleDeclarationException(passiveDeclareRetries, e);
            }
        }
        while (passiveDeclareRetries-- > 0 && !Cancelled);

        Declaring = false;
    }

    private void SetQosAndCreateConsumers()
    {
        if (!AcknowledgeMode.IsAutoAck() && !Cancelled)
        {
            // Set basicQos before calling basicConsume (otherwise if we are not acking the broker
            // will send blocks of 100 messages)
            try
            {
                Channel.BasicQos(0, PrefetchCount, true);
            }
            catch (Exception e)
            {
                ActiveObjectCounter.Release(this);
                throw new RabbitIOException(e);
            }
        }

        try
        {
            if (!Cancelled)
            {
                foreach (string queueName in Queues)
                {
                    if (!MissingQueues.Contains(queueName))
                    {
                        ConsumeFromQueue(queueName);
                    }
                }
            }
        }
        catch (Exception e)
        {
            throw RabbitExceptionTranslator.ConvertRabbitAccessException(e);
        }
    }

    private void HandleDeclarationException(int passiveDeclareRetries, DeclarationException e)
    {
        if (passiveDeclareRetries > 0 && Channel.IsOpen)
        {
            Logger?.LogWarning(e, "Queue declaration failed; retries left={retries}", passiveDeclareRetries);

            try
            {
                Thread.Sleep(FailedDeclarationRetryInterval);
            }
            catch (Exception e1)
            {
                Declaring = false;
                ActiveObjectCounter.Release(this);
                throw RabbitExceptionTranslator.ConvertRabbitAccessException(e1);
            }
        }
        else if (e.FailedQueues.Count < Queues.Count)
        {
            Logger?.LogWarning(e, "Not all queues are available; only listening on those that are - configured: {queues}; not available: {notAvailable}",
                string.Join(',', Queues), string.Join(',', e.FailedQueues));

            lock (MissingQueues)
            {
                foreach (string q in e.FailedQueues)
                {
                    MissingQueues.Add(q);
                }
            }

            LastRetryDeclaration = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        }
        else
        {
            Declaring = false;
            ActiveObjectCounter.Release(this);

            throw new QueuesNotAvailableException(
                "Cannot prepare queue for listener. Either the queue doesn't exist or the broker will not allow us to use it.", e);
        }
    }

    private void ConsumeFromQueue(string queue)
    {
        var consumer = new InternalConsumer(this, Channel, queue);

        string consumerTag = Channel.BasicConsume(queue, AcknowledgeMode.IsAutoAck(), TagStrategy != null ? TagStrategy.CreateConsumerTag(queue) : string.Empty,
            NoLocal, Exclusive, ConsumerArgs, consumer);

        if (consumerTag != null)
        {
            Logger?.LogDebug("Started on queue '{queue}' with tag {consumerTag} : {consumer}", queue, consumerTag, this);
        }
        else
        {
            Logger?.LogError("Null consumer tag received for queue: {queue} ", queue);
        }
    }

    private void AttemptPassiveDeclarations()
    {
        DeclarationException failures = null;

        foreach (string queueName in Queues)
        {
            try
            {
                try
                {
                    Channel.QueueDeclarePassive(queueName);
                }
                catch (WireFormattingException e)
                {
                    try
                    {
                        if (Channel is IChannelProxy proxy)
                        {
                            proxy.TargetChannel.Close();
                        }
                    }
                    catch (TimeoutException)
                    {
                        // Intentionally left empty.
                    }

                    throw new FatalListenerStartupException("Illegal Argument on Queue Declaration", e);
                }
            }
            catch (RabbitMQClientException e)
            {
                Logger?.LogWarning(e, "Failed to declare queue: {name} ", queueName);

                if (!Channel.IsOpen)
                {
                    throw new RabbitIOException(e);
                }

                failures ??= new DeclarationException(e);
                failures.AddFailedQueue(queueName);
            }
        }

        // Rule suppressed due to Sonar bug: https://github.com/SonarSource/sonar-dotnet/issues/8140
#pragma warning disable S2583 // Conditionally executed code should be reachable
        if (failures != null)
#pragma warning restore S2583 // Conditionally executed code should be reachable
        {
            throw failures;
        }
    }

    private void CheckMissingQueues()
    {
        long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        if (now - RetryDeclarationInterval > LastRetryDeclaration)
        {
            lock (MissingQueues)
            {
                var toRemove = new List<string>();
                Exception error = null;

                foreach (string queueToCheck in MissingQueues)
                {
                    bool available = true;
                    const IConnection connection = null;
                    RC.IModel channelForCheck = null;

                    try
                    {
                        channelForCheck = ConnectionFactory.CreateConnection().CreateChannel();
                        channelForCheck.QueueDeclarePassive(queueToCheck);
                        Logger?.LogInformation("Queue '{queue}' is now available", queueToCheck);
                    }
                    catch (Exception e)
                    {
                        available = false;
                        Logger?.LogWarning(e, "Queue '{queue}' is not available", queueToCheck);
                    }
                    finally
                    {
                        RabbitUtils.CloseChannel(channelForCheck);
                        RabbitUtils.CloseConnection(connection);
                    }

                    if (available)
                    {
                        try
                        {
                            ConsumeFromQueue(queueToCheck);
                            toRemove.Add(queueToCheck);
                        }
                        catch (Exception e)
                        {
                            error = e;
                            break;
                        }
                    }
                }

                if (toRemove.Count > 0)
                {
                    foreach (string remove in toRemove)
                    {
                        MissingQueues.Remove(remove);
                    }
                }

                if (error != null)
                {
                    throw RabbitExceptionTranslator.ConvertRabbitAccessException(error);
                }
            }

            LastRetryDeclaration = now;
        }
    }

    private void CheckShutdown()
    {
        if (Shutdown != null)
        {
            throw new ShutdownSignalException(Shutdown);
        }
    }

    private IMessage Handle(Delivery delivery)
    {
        if (delivery == null && Shutdown != null)
        {
            throw new ShutdownSignalException(Shutdown);
        }

        if (delivery == null)
        {
            return null;
        }

        byte[] body = delivery.Body;
        IMessageHeaders messageProperties = MessageHeadersConverter.ToMessageHeaders(delivery.Properties, delivery.Envelope, EncodingUtils.Utf8);
        RabbitHeaderAccessor accessor = RabbitHeaderAccessor.GetMutableAccessor(messageProperties);
        accessor.ConsumerTag = delivery.ConsumerTag;
        accessor.ConsumerQueue = delivery.Queue;
        IMessage<byte[]> message = Message.Create(body, accessor.MessageHeaders);
        Logger?.LogDebug("Received message: {message}", message);

        if (messageProperties.DeliveryTag() != null)
        {
            DeliveryTags.Add(messageProperties.DeliveryTag().Value);
        }

        if (Transactional && !LocallyTransacted)
        {
            ConnectionFactoryUtils.RegisterDeliveryTag(ConnectionFactory, Channel, delivery.Envelope.DeliveryTag);
        }

        return message;
    }

    private sealed class InternalConsumer : RC.DefaultBasicConsumer
    {
        public BlockingQueueConsumer Consumer { get; }

        public string QueueName { get; }

        public ILogger<InternalConsumer> Logger { get; }

        public bool Canceled { get; set; }

        public InternalConsumer(BlockingQueueConsumer consumer, RC.IModel channel, string queue, ILogger<InternalConsumer> logger = null)
            : base(channel)
        {
            Consumer = consumer;
            QueueName = queue;
            Logger = logger;
        }

        public override void HandleBasicConsumeOk(string consumerTag)
        {
            base.HandleBasicConsumeOk(consumerTag);
            ConsumerTag = consumerTag;
            Logger?.LogDebug("ConsumeOK: {consumer} {consumerTag}", Consumer, consumerTag);
            Consumer.Consumers.TryAdd(QueueName, this);
        }

        public override void HandleModelShutdown(object model, RC.ShutdownEventArgs reason)
        {
            base.HandleModelShutdown(model, reason);

            Logger?.LogDebug("Received shutdown signal for consumer tag: {tag} reason: {reason}", ConsumerTag, reason.ReplyText);
            Consumer.Shutdown = reason;
            Consumer.DeliveryTags.Clear();
            Consumer.ActiveObjectCounter.Release(Consumer);
        }

        public override void HandleBasicCancel(string consumerTag)
        {
            Logger?.LogWarning("Cancel received for {consumerTag} : {queueName} : {consumer}", consumerTag, QueueName, this);
            Consumer.Consumers.Remove(QueueName, out _);

            if (Consumer.Consumers.Count != 0)
            {
                Consumer.BasicCancel(false);
            }
            else
            {
                Consumer.Cancel.Value = true;
            }
        }

        public override void HandleBasicCancelOk(string consumerTag)
        {
            Logger?.LogDebug("Received CancelOk for {consumerTag} : {queueName} : {consumer}", consumerTag, QueueName, this);
            Canceled = true;
        }

        public override void HandleBasicDeliver(string consumerTag, ulong deliveryTag, bool redelivered, string exchange, string routingKey,
            RC.IBasicProperties properties, byte[] body)
        {
            Logger?.LogDebug("Storing delivery for consumer tag: {tag} with deliveryTag: {deliveryTag} for consumer: {consumer}", ConsumerTag, deliveryTag,
                this);

            try
            {
                var delivery = new Delivery(consumerTag, new Envelope(deliveryTag, redelivered, exchange, routingKey), properties, body, QueueName);

                if (Consumer.AbortStarted > 0)
                {
                    if (!Consumer.Queue.TryAdd(delivery, Consumer.ShutdownTimeout))
                    {
                        RabbitUtils.SetPhysicalCloseRequired(Model, true);
                        _ = Consumer.Queue.TakeWhile(_ => Consumer.Queue.Count > 0);

                        if (!Canceled)
                        {
                            RabbitUtils.Cancel(Model, consumerTag);
                        }

                        try
                        {
                            Model.Close();
                        }
                        catch (Exception)
                        {
                            // Intentionally left empty.
                        }
                    }
                }
                else
                {
                    Consumer.Queue.TryAdd(delivery);
                }
            }
            catch (Exception e)
            {
                Logger?.LogWarning(e, "Unexpected exception during delivery");
            }
        }

        public override string ToString()
        {
            return $"InternalConsumer{{queue='{QueueName}', consumerTag='{ConsumerTag}'}}";
        }
    }

    private sealed class DeclarationException : RabbitException
    {
        private const string MessageText = "Failed to declare queue(s):";

        public List<string> FailedQueues { get; } = new();
        public override string Message => base.Message + string.Join(',', FailedQueues);

        public DeclarationException()
            : base(MessageText)
        {
        }

        public DeclarationException(Exception innerException)
            : base(MessageText, innerException)
        {
        }

        public void AddFailedQueue(string queue)
        {
            FailedQueues.Add(queue);
        }
    }
}
