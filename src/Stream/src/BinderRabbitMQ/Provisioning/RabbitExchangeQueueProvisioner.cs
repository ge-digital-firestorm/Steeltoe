// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Steeltoe.Common;
using Steeltoe.Common.Contexts;
using Steeltoe.Messaging.RabbitMQ.Configuration;
using Steeltoe.Messaging.RabbitMQ.Connection;
using Steeltoe.Messaging.RabbitMQ.Core;
using Steeltoe.Messaging.RabbitMQ.Exceptions;
using Steeltoe.Stream.Binder.RabbitMQ.Configuration;
using Steeltoe.Stream.Configuration;
using Steeltoe.Stream.Provisioning;
using static Steeltoe.Messaging.RabbitMQ.Configuration.Binding;

namespace Steeltoe.Stream.Binder.RabbitMQ.Provisioning;

public class RabbitExchangeQueueProvisioner : IProvisioningProvider
{
    private const string GroupIndexDelimiter = ".";
    private readonly IApplicationContext _autoDeclareContext;
    private readonly ILogger _logger;

    private bool _notOurAdminException = true; // Should be set by onApplicationEvent

    private RabbitAdmin Admin { get; }

    private List<IDeclarableCustomizer> Customizers { get; }

    private RabbitBindingsOptions Options { get; }

    public RabbitExchangeQueueProvisioner(IConnectionFactory connectionFactory, IOptionsMonitor<RabbitBindingsOptions> bindingsOptions,
        IApplicationContext applicationContext, ILogger<RabbitExchangeQueueProvisioner> logger)
        : this(connectionFactory, new List<IDeclarableCustomizer>(), bindingsOptions, applicationContext, logger)
    {
    }

    public RabbitExchangeQueueProvisioner(IConnectionFactory connectionFactory, List<IDeclarableCustomizer> customizers,
        IOptionsMonitor<RabbitBindingsOptions> bindingsOptions, IApplicationContext applicationContext, ILogger<RabbitExchangeQueueProvisioner> logger)
    {
        Admin = new RabbitAdmin(applicationContext, connectionFactory, logger);

        _autoDeclareContext = applicationContext;
        _logger = logger;
        Admin.ApplicationContext = _autoDeclareContext;
        Admin.Initialize();
        Customizers = customizers;
        Options = bindingsOptions.CurrentValue;
    }

    public static string ConstructDlqName(string name)
    {
        return $"{name}.dlq";
    }

    public static string ApplyPrefix(string prefix, string name)
    {
        return prefix + name;
    }

    public IProducerDestination ProvisionProducerDestination(string name, IProducerOptions options)
    {
        RabbitProducerOptions producerProperties = Options.GetRabbitProducerOptions(options.BindingName);

        string exchangeName = ApplyPrefix(producerProperties.Prefix, name);
        IExchange exchange = BuildExchange(producerProperties, exchangeName);

        if (producerProperties.DeclareExchange.Value)
        {
            DeclareExchange(exchangeName, exchange);
        }

        Steeltoe.Messaging.RabbitMQ.Configuration.IBinding binding = null;

        foreach (string requiredGroupName in options.RequiredGroups)
        {
            string baseQueueName = producerProperties.QueueNameGroupOnly.Value ? requiredGroupName : $"{exchangeName}.{requiredGroupName}";

            if (!options.IsPartitioned)
            {
                AutoBindDlq(baseQueueName, baseQueueName, producerProperties);

                if (producerProperties.BindQueue.Value)
                {
                    var queue = new Queue(baseQueueName, true, false, false, GetQueueArgs(baseQueueName, producerProperties, false));
                    DeclareQueue(baseQueueName, queue);
                    List<string> routingKeys = BindingRoutingKeys(producerProperties);

                    if (routingKeys == null || routingKeys.Count == 0)
                    {
                        binding = NotPartitionedBinding(exchange, queue, null, producerProperties);
                    }
                    else
                    {
                        foreach (string routingKey in routingKeys)
                        {
                            binding = NotPartitionedBinding(exchange, queue, routingKey, producerProperties);
                        }
                    }
                }
            }
            else
            {
                // if the stream is partitioned, create one queue for each target partition for the default group
                for (int i = 0; i < options.PartitionCount; i++)
                {
                    string partitionSuffix = $"-{i}";
                    string partitionQueueName = baseQueueName + partitionSuffix;
                    AutoBindDlq(baseQueueName, baseQueueName + partitionSuffix, producerProperties);

                    if (producerProperties.BindQueue.Value)
                    {
                        var queue = new Queue(partitionQueueName, true, false, false, GetQueueArgs(partitionQueueName, producerProperties, false));
                        DeclareQueue(queue.QueueName, queue);
                        string prefix = producerProperties.Prefix;
                        string destination = string.IsNullOrEmpty(prefix) ? exchangeName : exchangeName.Substring(prefix.Length);
                        List<string> routingKeys = BindingRoutingKeys(producerProperties);

                        if (routingKeys == null || routingKeys.Count == 0)
                        {
                            binding = PartitionedBinding(destination, exchange, queue, null, producerProperties, i);
                        }
                        else
                        {
                            foreach (string routingKey in routingKeys)
                            {
                                binding = PartitionedBinding(destination, exchange, queue, routingKey, producerProperties, i);
                            }
                        }
                    }
                }
            }
        }

        return new RabbitProducerDestination(exchange, binding);
    }

    public IConsumerDestination ProvisionConsumerDestination(string name, string group, IConsumerOptions options)
    {
        IConsumerDestination consumerDestination;

        if (!options.Multiplex)
        {
            consumerDestination = DoProvisionConsumerDestination(name, group, options);
        }
        else
        {
            var consumerDestinationNames = new List<string>();
            IEnumerable<string> trimmed = name.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim());

            foreach (string destination in trimmed)
            {
                if (options.IsPartitioned && options.InstanceIndexList.Count > 0)
                {
                    foreach (int index in options.InstanceIndexList)
                    {
                        var temporaryOptions = options.Clone() as ConsumerOptions;
                        temporaryOptions.InstanceIndex = index;
                        consumerDestinationNames.Add(DoProvisionConsumerDestination(destination, group, temporaryOptions).Name);
                    }
                }
                else
                {
                    consumerDestinationNames.Add(DoProvisionConsumerDestination(destination, group, options).Name);
                }
            }

            consumerDestination = new RabbitConsumerDestination(string.Join(',', consumerDestinationNames), null);
        }

        return consumerDestination;
    }

    public void CleanAutoDeclareContext(IConsumerDestination destination, IConsumerOptions consumerProperties)
    {
        lock (_autoDeclareContext)
        {
            destination.Name.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList().ForEach(name =>
            {
                name = name.Trim();
                RemoveSingleton($"{name}.binding");
                RemoveSingleton(name);
                string dlq = $"{name}.dlq";
                RemoveSingleton($"{dlq}.binding");
                RemoveSingleton(dlq);
            });
        }
    }

    protected virtual string GetGroupedName(string name, string group)
    {
        return name + GroupIndexDelimiter + (!string.IsNullOrEmpty(group) ? group : "default");
    }

    private IConsumerDestination DoProvisionConsumerDestination(string name, string group, IConsumerOptions options)
    {
        RabbitConsumerOptions consumerProperties = Options.GetRabbitConsumerOptions(options.BindingName);
        bool anonymous = string.IsNullOrEmpty(group);
        Base64UrlNamingStrategy anonQueueNameGenerator = null;

        if (anonymous)
        {
            anonQueueNameGenerator = new Base64UrlNamingStrategy(consumerProperties.AnonymousGroupPrefix ?? string.Empty);
        }

        string baseQueueName;

        if (consumerProperties.QueueNameGroupOnly.GetValueOrDefault())
        {
            baseQueueName = anonymous ? anonQueueNameGenerator.GenerateName() : group;
        }
        else
        {
            baseQueueName = GetGroupedName(name, anonymous ? anonQueueNameGenerator.GenerateName() : group);
        }

        string prefix = consumerProperties.Prefix;
        string exchangeName = ApplyPrefix(prefix, name);
        IExchange exchange = BuildExchange(consumerProperties, exchangeName);

        if (consumerProperties.DeclareExchange.GetValueOrDefault())
        {
            DeclareExchange(exchangeName, exchange);
        }

        string queueName = ApplyPrefix(prefix, baseQueueName);
        bool partitioned = !anonymous && options.IsPartitioned;
        bool durable = !anonymous && consumerProperties.DurableSubscription.Value;
        Queue queue;

        if (anonymous)
        {
            string anonQueueName = queueName;
            queue = new AnonymousQueue(new GivenNamingStrategy(() => anonQueueName), GetQueueArgs(queueName, consumerProperties, false));
        }
        else
        {
            if (partitioned)
            {
                string partitionSuffix = $"-{options.InstanceIndex}";
                queueName += partitionSuffix;
            }

            queue = durable
                ? new Queue(queueName, true, false, false, GetQueueArgs(queueName, consumerProperties, false))
                : new Queue(queueName, false, false, true, GetQueueArgs(queueName, consumerProperties, false));
        }

        Steeltoe.Messaging.RabbitMQ.Configuration.IBinding binding = null;

        if (consumerProperties.BindQueue.GetValueOrDefault())
        {
            DeclareQueue(queueName, queue);
            List<string> routingKeys = BindingRoutingKeys(consumerProperties);

            if (routingKeys == null || routingKeys.Count == 0)
            {
                binding = DeclareConsumerBindings(name, null, options, exchange, partitioned, queue);
            }
            else
            {
                foreach (string routingKey in routingKeys)
                {
                    binding = DeclareConsumerBindings(name, routingKey, options, exchange, partitioned, queue);
                }
            }
        }

        if (durable)
        {
            AutoBindDlq(ApplyPrefix(consumerProperties.Prefix, baseQueueName), queueName, consumerProperties);
        }

        return new RabbitConsumerDestination(queue.QueueName, binding);
    }

    private Steeltoe.Messaging.RabbitMQ.Configuration.IBinding DeclareConsumerBindings(string name, string routingKey, IConsumerOptions options,
        IExchange exchange, bool partitioned, Queue queue)
    {
        RabbitConsumerOptions consumerProperties = Options.GetRabbitConsumerOptions(options.BindingName);

        if (partitioned)
        {
            return PartitionedBinding(name, exchange, queue, routingKey, consumerProperties, options.InstanceIndex);
        }

        return NotPartitionedBinding(exchange, queue, routingKey, consumerProperties);
    }

    private Steeltoe.Messaging.RabbitMQ.Configuration.IBinding PartitionedBinding(string destination, IExchange exchange, Queue queue, string rk,
        RabbitCommonOptions extendedProperties, int index)
    {
        string bindingKey = rk ?? destination;

        bindingKey += $"-{index}";
        var arguments = new Dictionary<string, object>();

        foreach (KeyValuePair<string, string> entry in extendedProperties.QueueBindingArguments)
        {
            arguments.Add(entry.Key, entry.Value);
        }

        switch (exchange)
        {
            case TopicExchange topic:
            {
                Steeltoe.Messaging.RabbitMQ.Configuration.IBinding binding = BindingBuilder.Bind(queue).To(topic).With(bindingKey);
                DeclareBinding(queue.QueueName, binding);
                return binding;
            }

            case DirectExchange direct:
            {
                Steeltoe.Messaging.RabbitMQ.Configuration.IBinding binding = BindingBuilder.Bind(queue).To(direct).With(bindingKey);
                DeclareBinding(queue.QueueName, binding);
                return binding;
            }

            case FanOutExchange:
                throw new ProvisioningException("A fan-out exchange is not appropriate for partitioned apps");
            case HeadersExchange:
            {
                var binding = new Steeltoe.Messaging.RabbitMQ.Configuration.Binding($"{queue.QueueName}.{exchange.ExchangeName}.binding", queue.QueueName,
                    DestinationType.Queue, exchange.ExchangeName, string.Empty, arguments);

                DeclareBinding(queue.QueueName, binding);
                return binding;
            }

            default:
                throw new ProvisioningException($"Cannot bind to a {exchange.Type} exchange");
        }
    }

    private Steeltoe.Messaging.RabbitMQ.Configuration.IBinding NotPartitionedBinding(IExchange exchange, Queue queue, string rk,
        RabbitCommonOptions extendedProperties)
    {
        string routingKey = rk ?? "#";

        var arguments = new Dictionary<string, object>();

        foreach (KeyValuePair<string, string> entry in extendedProperties.QueueBindingArguments)
        {
            arguments.Add(entry.Key, entry.Value);
        }

        switch (exchange)
        {
            case TopicExchange topic:
            {
                Steeltoe.Messaging.RabbitMQ.Configuration.IBinding binding = BindingBuilder.Bind(queue).To(topic).With(routingKey);
                DeclareBinding(queue.QueueName, binding);
                return binding;
            }

            case DirectExchange direct:
            {
                Steeltoe.Messaging.RabbitMQ.Configuration.IBinding binding = BindingBuilder.Bind(queue).To(direct).With(routingKey);
                DeclareBinding(queue.QueueName, binding);
                return binding;
            }

            case FanOutExchange fanOut:
            {
                Steeltoe.Messaging.RabbitMQ.Configuration.IBinding binding = BindingBuilder.Bind(queue).To(fanOut);
                DeclareBinding(queue.QueueName, binding);
                return binding;
            }

            case HeadersExchange:
            {
                var binding = new Steeltoe.Messaging.RabbitMQ.Configuration.Binding($"{queue.QueueName}.{exchange.ExchangeName}.binding", queue.QueueName,
                    DestinationType.Queue, exchange.ExchangeName, string.Empty, arguments);

                DeclareBinding(queue.QueueName, binding);
                return binding;
            }

            default:
                throw new ProvisioningException($"Cannot bind to a {exchange.Type} exchange");
        }
    }

    private List<string> BindingRoutingKeys(RabbitCommonOptions extendedProperties)
    {
        /*
         * When the delimiter is null, we get a String[1] containing the original.
         */
        string delimiter = extendedProperties.BindingRoutingKeyDelimiter;

        if (delimiter == null)
        {
            if (extendedProperties.BindingRoutingKey == null)
            {
                return null;
            }

            return new List<string>
            {
                extendedProperties.BindingRoutingKey.Trim()
            };
        }

        if (extendedProperties.BindingRoutingKey == null)
        {
            return null;
        }

        IEnumerable<string> trimmed = extendedProperties.BindingRoutingKey.Split(delimiter, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim());
        return new List<string>(trimmed);
    }

    private void AutoBindDlq(string baseQueueName, string routingKey, RabbitCommonOptions properties)
    {
        bool autoBindDlq = properties.AutoBindDlq.Value;

        _logger.LogDebug("autoBindDLQ={autoBindDLQ} for: {queueName}", autoBindDlq, baseQueueName);

        if (autoBindDlq)
        {
            string dlqName = properties.DeadLetterQueueName ?? ConstructDlqName(baseQueueName);

            var dlq = new Queue(dlqName, true, false, false, GetQueueArgs(dlqName, properties, true));
            DeclareQueue(dlqName, dlq);
            string dlxName = GetDeadLetterExchangeName(properties);

            if (properties.DeclareDlx.Value)
            {
                DeclareExchange(dlxName, new ExchangeBuilder(dlxName, properties.DeadLetterExchangeType).Durable(true).Build());
            }

            var arguments = new Dictionary<string, object>();

            properties.DlqBindingArguments?.ToList().ForEach(entry => arguments.Add(entry.Key, entry.Value));

            string dlRoutingKey = properties.DeadLetterRoutingKey ?? routingKey;
            string dlBindingName = $"{dlq.QueueName}.{dlxName}.{dlRoutingKey}.binding";

            var dlqBinding =
                new Steeltoe.Messaging.RabbitMQ.Configuration.Binding(dlBindingName, dlq.QueueName, DestinationType.Queue, dlxName, dlRoutingKey, arguments);

            DeclareBinding(dlqName, dlqBinding);

            if (properties is RabbitConsumerOptions options && options.RepublishToDlq.Value)
            {
                /*
                 * Also bind with the base queue name when republishToDlq is used, which does not know about partitioning
                 */
                string bindingName = $"{dlq.QueueName}.{dlxName}.{baseQueueName}.binding";

                DeclareBinding(dlqName,
                    new Steeltoe.Messaging.RabbitMQ.Configuration.Binding(bindingName, dlq.QueueName, DestinationType.Queue, dlxName, baseQueueName,
                        arguments));
            }
        }
    }

    private string GetDeadLetterExchangeName(RabbitCommonOptions properties)
    {
        if (properties.DeadLetterExchange == null)
        {
            return properties.Prefix + RabbitCommonOptions.DeadLetterExchangeName;
        }

        return properties.DeadLetterExchange;
    }

    private void DeclareQueue(string beanName, Queue queueArg)
    {
        Queue queue = queueArg;

        foreach (IDeclarableCustomizer customizer in Customizers)
        {
            queue = (Queue)customizer.Apply(queue);
        }

        try
        {
            Admin.DeclareQueue(queue);
        }
        catch (RabbitConnectException e)
        {
            _logger.LogDebug(e, "Declaration of queue: {queueName} deferred - connection not available", queue.QueueName);
        }
        catch (Exception e)
        {
            if (_notOurAdminException)
            {
                _notOurAdminException = false;
                throw;
            }

            _logger.LogDebug(e, "Declaration of queue: {queueName} deferred", queue.QueueName);
        }

        AddToAutoDeclareContext(beanName, queue);
    }

    private Dictionary<string, object> GetQueueArgs(string queueName, RabbitCommonOptions properties, bool isDlq)
    {
        var args = new Dictionary<string, object>();

        if (!isDlq)
        {
            if (properties.AutoBindDlq.Value)
            {
                string dlx = properties.DeadLetterExchange ?? ApplyPrefix(properties.Prefix, "DLX");

                args.Add("x-dead-letter-exchange", dlx);

                string dlRk = properties.DeadLetterRoutingKey ?? queueName;

                args.Add("x-dead-letter-routing-key", dlRk);
            }
        }
        else
        {
            if (properties.DlqDeadLetterExchange != null)
            {
                args.Add("x-dead-letter-exchange", properties.DlqDeadLetterExchange);
            }

            if (properties.DlqDeadLetterRoutingKey != null)
            {
                args.Add("x-dead-letter-routing-key", properties.DlqDeadLetterRoutingKey);
            }
        }

        AddAdditionalArgs(args, properties, isDlq);
        return args;
    }

    private void AddAdditionalArgs(Dictionary<string, object> args, RabbitCommonOptions properties, bool isDlq)
    {
        int? expires = isDlq ? properties.DlqExpires : properties.Expires;
        int? maxLength = isDlq ? properties.DlqMaxLength : properties.MaxLength;
        int? maxLengthBytes = isDlq ? properties.DlqMaxLengthBytes : properties.MaxLengthBytes;
        int? maxPriority = isDlq ? properties.DlqMaxPriority : properties.MaxPriority;
        int? ttl = isDlq ? properties.DlqTtl : properties.Ttl;
        bool? lazy = isDlq ? properties.DlqLazy : properties.Lazy;
        string overflow = isDlq ? properties.DlqOverflowBehavior : properties.OverflowBehavior;
        RabbitCommonOptions.QuorumConfig quorum = isDlq ? properties.DlqQuorum : properties.Quorum;
        bool? singleActive = isDlq ? properties.DlqSingleActiveConsumer : properties.SingleActiveConsumer;

        if (expires != null)
        {
            args.Add("x-expires", expires.Value);
        }

        if (maxLength != null)
        {
            args.Add("x-max-length", maxLength.Value);
        }

        if (maxLengthBytes != null)
        {
            args.Add("x-max-length-bytes", maxLengthBytes.Value);
        }

        if (maxPriority != null)
        {
            args.Add("x-max-priority", maxPriority.Value);
        }

        if (ttl != null)
        {
            args.Add("x-message-ttl", ttl.Value);
        }

        if (lazy.GetValueOrDefault())
        {
            args.Add("x-queue-mode", "lazy");
        }

        if (!string.IsNullOrEmpty(overflow))
        {
            args.Add("x-overflow", overflow);
        }

        if (quorum != null && quorum.Enabled.Value)
        {
            args.Add("x-queue-type", "quorum");

            if (quorum.DeliveryLimit != null)
            {
                args.Add("x-delivery-limit", quorum.DeliveryLimit.Value);
            }

            if (quorum.InitialQuorumSize != null)
            {
                args.Add("x-quorum-initial-group-size", quorum.InitialQuorumSize.Value);
            }
        }

        if (singleActive.GetValueOrDefault())
        {
            args.Add("x-single-active-consumer", true);
        }
    }

    private IExchange BuildExchange(RabbitCommonOptions properties, string exchangeName)
    {
        try
        {
            var builder = new ExchangeBuilder(exchangeName, properties.ExchangeType);
            builder.Durable(properties.ExchangeDurable.GetValueOrDefault());

            if (properties.ExchangeAutoDelete.GetValueOrDefault())
            {
                builder.AutoDelete();
            }

            if (properties.DelayedExchange.GetValueOrDefault())
            {
                builder.Delayed();
            }

            return builder.Build();
        }
        catch (Exception e)
        {
            throw new ProvisioningException("Failed to create exchange object", e);
        }
    }

    private void DeclareExchange(string rootName, IExchange exchangeArg)
    {
        IExchange exchange = exchangeArg;

        foreach (IDeclarableCustomizer customizer in Customizers)
        {
            exchange = (IExchange)customizer.Apply(exchange);
        }

        try
        {
            Admin.DeclareExchange(exchange);
        }
        catch (RabbitConnectException e)
        {
            _logger.LogDebug(e, "Declaration of exchange: {exchangeName} deferred - connection not available", exchange.ExchangeName);
        }
        catch (Exception e)
        {
            if (_notOurAdminException)
            {
                _notOurAdminException = false;
                throw;
            }

            _logger.LogDebug(e, "Declaration of exchange: {exchangeName} deferred", exchange.ExchangeName);
        }

        AddToAutoDeclareContext($"{rootName}.exchange", exchange);
    }

    private void AddToAutoDeclareContext(string name, object bean)
    {
        lock (_autoDeclareContext)
        {
            if (!_autoDeclareContext.ContainsService(name, bean.GetType()))
            {
                _autoDeclareContext.Register(name, bean);
            }
        }
    }

    private void DeclareBinding(string rootName, Steeltoe.Messaging.RabbitMQ.Configuration.IBinding bindingArg)
    {
        Steeltoe.Messaging.RabbitMQ.Configuration.IBinding binding = bindingArg;

        foreach (IDeclarableCustomizer customizer in Customizers)
        {
            binding = (Steeltoe.Messaging.RabbitMQ.Configuration.IBinding)customizer.Apply(binding);
        }

        try
        {
            Admin.DeclareBinding(binding);
        }
        catch (RabbitConnectException e)
        {
            _logger.LogDebug(e, "Declaration of binding: {name}.binding deferred - connection not available", rootName);
        }
        catch (Exception e)
        {
            if (_notOurAdminException)
            {
                _notOurAdminException = false;
                throw;
            }

            _logger.LogDebug(e, "Declaration of binding: {name}.binding deferred", rootName);
        }

        AddToAutoDeclareContext($"{rootName}.binding", binding);
    }

    private void RemoveSingleton(string name)
    {
        if (_autoDeclareContext.ContainsService(name))
        {
            _autoDeclareContext.Deregister(name);
        }
    }

    private sealed class GivenNamingStrategy : INamingStrategy
    {
        private readonly Func<string> _strategy;

        public GivenNamingStrategy(Func<string> strategy)
        {
            _strategy = strategy;
        }

        public string GenerateName()
        {
            return _strategy();
        }
    }

    private sealed class RabbitProducerDestination : IProducerDestination
    {
        public IExchange Exchange { get; }

        public string Name => Exchange.ExchangeName;

        public Steeltoe.Messaging.RabbitMQ.Configuration.IBinding Binding { get; }

        public RabbitProducerDestination(IExchange exchange, Steeltoe.Messaging.RabbitMQ.Configuration.IBinding binding)
        {
            ArgumentGuard.NotNull(exchange);

            Exchange = exchange;
            Binding = binding;
        }

        public string GetNameForPartition(int partition)
        {
            return Exchange.ExchangeName;
        }

        public override string ToString()
        {
            return $"RabbitProducerDestination{{exchange={Exchange}, binding={Binding}}}";
        }
    }

    private sealed class RabbitConsumerDestination : IConsumerDestination
    {
        public string Name { get; }

        public Steeltoe.Messaging.RabbitMQ.Configuration.IBinding Binding { get; }

        public RabbitConsumerDestination(string queueName, Steeltoe.Messaging.RabbitMQ.Configuration.IBinding binding)
        {
            ArgumentGuard.NotNull(queueName);

            Name = queueName;
            Binding = binding;
        }

        public override string ToString()
        {
            return $"RabbitConsumerDestination{{queue={Name}, binding={Binding}}}";
        }
    }
}
