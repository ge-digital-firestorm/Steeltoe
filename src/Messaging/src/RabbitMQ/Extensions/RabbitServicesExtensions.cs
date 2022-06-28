// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Steeltoe.Common.Contexts;
using Steeltoe.Common.Expression.Internal.Contexts;
using Steeltoe.Common.Lifecycle;
using Steeltoe.Messaging.Converter;
using Steeltoe.Messaging.Handler.Attributes.Support;
using Steeltoe.Messaging.RabbitMQ.Config;
using Steeltoe.Messaging.RabbitMQ.Connection;
using Steeltoe.Messaging.RabbitMQ.Core;
using Steeltoe.Messaging.RabbitMQ.Host;
using Steeltoe.Messaging.RabbitMQ.Listener;
using System;
using System.Linq;
using static Steeltoe.Common.Contexts.AbstractApplicationContext;
using RC = RabbitMQ.Client;

namespace Steeltoe.Messaging.RabbitMQ.Extensions;

public static class RabbitServicesExtensions
{
    public static IServiceCollection AddRabbitTemplate(this IServiceCollection services)
    {
        return services.AddRabbitTemplate(RabbitTemplate.DEFAULT_SERVICE_NAME);
    }

    public static IServiceCollection AddRabbitTemplate(this IServiceCollection services, Action<IServiceProvider, RabbitTemplate> configure)
    {
        return services.AddRabbitTemplate(RabbitTemplate.DEFAULT_SERVICE_NAME, configure);
    }

    public static IServiceCollection AddRabbitTemplate(this IServiceCollection services, string serviceName, Action<IServiceProvider, RabbitTemplate> configure = null)
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            throw new ArgumentException(nameof(serviceName));
        }

        services.AddSingleton(p =>
        {
            var template = ActivatorUtilities.CreateInstance(p, typeof(RabbitTemplate)) as RabbitTemplate;
            template.ServiceName = serviceName;
            if (configure != null)
            {
                configure(p, template);
            }

            return template;
        });

        services.AddSingleton<IRabbitTemplate>(p =>
        {
            return p.GetServices<RabbitTemplate>().SingleOrDefault(t => t.ServiceName == serviceName);
        });

        return services;
    }

    public static IServiceCollection AddRabbitAdmin(this IServiceCollection services)
    {
        return services.AddRabbitAdmin(RabbitAdmin.DEFAULT_SERVICE_NAME);
    }

    public static IServiceCollection AddRabbitAdmin(this IServiceCollection services, Action<IServiceProvider, RabbitAdmin> configure)
    {
        return services.AddRabbitAdmin(RabbitAdmin.DEFAULT_SERVICE_NAME, configure);
    }

    public static IServiceCollection AddRabbitAdmin(this IServiceCollection services, string serviceName, Action<IServiceProvider, RabbitAdmin> configure = null)
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            throw new ArgumentException(nameof(serviceName));
        }

        services.AddSingleton(p =>
        {
            var admin = ActivatorUtilities.CreateInstance(p, typeof(RabbitAdmin)) as RabbitAdmin;
            admin.ServiceName = serviceName;
            if (configure != null)
            {
                configure(p, admin);
            }

            return admin;
        });
        services.AddSingleton<IRabbitAdmin>(p =>
        {
            return p.GetServices<RabbitAdmin>().SingleOrDefault(t => t.ServiceName == serviceName);
        });
        return services;
    }

    public static IServiceCollection AddRabbitQueues(this IServiceCollection services, params IQueue[] queues)
    {
        foreach (var q in queues)
        {
            services.AddRabbitQueue(q);
        }

        return services;
    }

    public static IServiceCollection AddRabbitQueue(this IServiceCollection services, IQueue queue)
    {
        services.RegisterService(queue.ServiceName, typeof(IQueue));
        return services.AddSingleton(queue);
    }

    public static IServiceCollection AddRabbitQueue(this IServiceCollection services, Func<IServiceProvider, IQueue> factory)
    {
        services.AddSingleton(factory);

        return services;
    }

    public static IServiceCollection AddRabbitQueue(this IServiceCollection services, string queueName, Action<IServiceProvider, Queue> configure = null)
    {
        services.RegisterService(queueName, typeof(IQueue));
        services.AddSingleton<IQueue>(p =>
        {
            var queue = new Queue(queueName);
            if (configure != null)
            {
                configure(p, queue);
            }

            return queue;
        });

        return services;
    }

    public static IServiceCollection AddRabbitExchanges(this IServiceCollection services, params IExchange[] exchanges)
    {
        foreach (var e in exchanges)
        {
            services.AddRabbitExchange(e);
        }

        return services;
    }

    public static IServiceCollection AddRabbitExchange(this IServiceCollection services, Func<IServiceProvider, IExchange> factory)
    {
        services.AddSingleton(factory);

        return services;
    }

    public static IServiceCollection AddRabbitExchange(this IServiceCollection services, IExchange exchange)
    {
        return services.AddSingleton(exchange);
    }

    public static IServiceCollection AddRabbitExchange(this IServiceCollection services, string exchangeName, string exchangeType, Action<IServiceProvider, IExchange> configure = null)
    {
        if (string.IsNullOrEmpty(exchangeName))
        {
            throw new ArgumentException(nameof(exchangeName));
        }

        if (string.IsNullOrEmpty(exchangeType))
        {
            throw new ArgumentException(nameof(exchangeType));
        }

        services.AddSingleton(p =>
        {
            var exchange = ExchangeBuilder.Create(exchangeName, exchangeType);
            if (configure != null)
            {
                configure(p, exchange);
            }

            return exchange;
        });

        return services;
    }

    public static IServiceCollection AddRabbitBindings(this IServiceCollection services, params IBinding[] bindings)
    {
        foreach (var b in bindings)
        {
            services.AddRabbitBinding(b);
        }

        return services;
    }

    public static IServiceCollection AddRabbitBinding(this IServiceCollection services, Func<IServiceProvider, IBinding> factory)
    {
        services.AddSingleton(factory);

        return services;
    }

    public static IServiceCollection AddRabbitBinding(this IServiceCollection services, IBinding binding)
    {
        services.RegisterService(binding.ServiceName, typeof(IBinding));
        services.AddSingleton(binding);
        return services;
    }

    public static IServiceCollection AddRabbitBinding(this IServiceCollection services, string bindingName, Binding.DestinationType bindingType, Action<IServiceProvider, IBinding> configure = null)
    {
        if (string.IsNullOrEmpty(bindingName))
        {
            throw new ArgumentException(nameof(bindingName));
        }

        services.RegisterService(bindingName, typeof(IBinding));
        services.AddSingleton(p =>
        {
            var binding = BindingBuilder.Create(bindingName, bindingType);
            if (configure != null)
            {
                configure(p, binding);
            }

            return binding;
        });
        return services;
    }

    public static IServiceCollection ConfigureRabbitOptions(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<RabbitOptions>()
            .Bind(config.GetSection(RabbitOptions.PREFIX))
            .Configure<IServiceProvider>((options, provider) =>
            {
                using var scope = provider.CreateScope();
                var connectionFactory = scope.ServiceProvider.GetService<RC.IConnectionFactory>() as RC.ConnectionFactory;

                if (connectionFactory is not null)
                {
                    options.Addresses = $"{connectionFactory.UserName}:{connectionFactory.Password}@{connectionFactory.HostName}:{connectionFactory.Port}";
                    options.VirtualHost = connectionFactory.VirtualHost;
                    options.Host = connectionFactory.HostName;
                    options.Username = connectionFactory.UserName;
                    options.Password = connectionFactory.Password;
                }
            });

        return services;
    }

    public static IServiceCollection AddRabbitServices(this IServiceCollection services, bool useJsonMessageConverter = false)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.AddRabbitHostingServices();

        services.AddRabbitConnectionFactory();
        if (useJsonMessageConverter)
        {
            services.AddRabbitJsonMessageConverter();
        }
        else
        {
            services.AddRabbitDefaultMessageConverter();
        }

        services.AddRabbitMessageHandlerMethodFactory();
        services.AddRabbitListenerContainerFactory();
        services.AddRabbitListenerEndpointRegistry();
        services.AddRabbitListenerEndpointRegistrar();
        services.AddRabbitListenerAttributeProcessor();
        return services;
    }

    public static IServiceCollection AddRabbitHostingServices(this IServiceCollection services)
    {
        services.AddOptions();
        services.AddHostedService<RabbitHostService>();
        services.TryAddSingleton<ILifecycleProcessor, DefaultLifecycleProcessor>();
        services.TryAddSingleton<IApplicationContext>(p =>
        {
            var context = new GenericApplicationContext(p.GetRequiredService<IServiceProvider>(), p.GetService<IConfiguration>(), p.GetServices<NameToTypeMapping>());
            context.ServiceExpressionResolver = new StandardServiceExpressionResolver();
            return context;
        });

        return services;
    }

    public static IServiceCollection AddRabbitListenerAttributeProcessor(this IServiceCollection services, Action<IServiceProvider, RabbitListenerAttributeProcessor> configure = null)
    {
        return services.AddRabbitListenerAttributeProcessor<RabbitListenerAttributeProcessor>(RabbitListenerAttributeProcessor.DEFAULT_SERVICE_NAME, configure);
    }

    public static IServiceCollection AddRabbitListenerAttributeProcessor(this IServiceCollection services, string serviceName, Action<IServiceProvider, RabbitListenerAttributeProcessor> configure = null)
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            throw new ArgumentException(nameof(serviceName));
        }

        services.AddRabbitListenerAttributeProcessor<RabbitListenerAttributeProcessor>(serviceName, configure);
        return services;
    }

    public static IServiceCollection AddRabbitListenerAttributeProcessor<TProcessor>(this IServiceCollection services, Action<IServiceProvider, TProcessor> configure)
        where TProcessor : IRabbitListenerAttributeProcessor
    {
        return services.AddRabbitListenerAttributeProcessor(null, configure);
    }

    public static IServiceCollection AddRabbitListenerAttributeProcessor<TProcessor>(this IServiceCollection services, string serviceName = null, Action<IServiceProvider, TProcessor> configure = null)
        where TProcessor : IRabbitListenerAttributeProcessor
    {
        services.TryAddSingleton<IRabbitListenerAttributeProcessor>(p =>
        {
            var instance = (TProcessor)ActivatorUtilities.GetServiceOrCreateInstance(p, typeof(TProcessor));
            if (!string.IsNullOrEmpty(serviceName))
            {
                instance.ServiceName = serviceName;
            }

            if (configure != null)
            {
                configure(p, instance);
            }

            return instance;
        });

        return services;
    }

    public static IServiceCollection AddRabbitListenerEndpointRegistrar(this IServiceCollection services, Action<IServiceProvider, RabbitListenerEndpointRegistrar> configure = null)
    {
        services.AddRabbitListenerEndpointRegistrar<RabbitListenerEndpointRegistrar>(RabbitListenerEndpointRegistrar.DEFAULT_SERVICE_NAME, configure);
        return services;
    }

    public static IServiceCollection AddRabbitListenerEndpointRegistrar(this IServiceCollection services, string serviceName, Action<IServiceProvider, RabbitListenerEndpointRegistrar> configure = null)
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            throw new ArgumentException(nameof(serviceName));
        }

        services.AddRabbitListenerEndpointRegistrar<RabbitListenerEndpointRegistrar>(serviceName, configure);
        return services;
    }

    public static IServiceCollection AddRabbitListenerEndpointRegistrar<TRegistrar>(this IServiceCollection services, Action<IServiceProvider, TRegistrar> configure)
        where TRegistrar : IRabbitListenerEndpointRegistrar
    {
        return services.AddRabbitListenerEndpointRegistrar(null, configure);
    }

    public static IServiceCollection AddRabbitListenerEndpointRegistrar<TRegistrar>(this IServiceCollection services, string serviceName = null, Action<IServiceProvider, TRegistrar> configure = null)
        where TRegistrar : IRabbitListenerEndpointRegistrar
    {
        services.TryAddSingleton<IRabbitListenerEndpointRegistrar>(p =>
        {
            var instance = (TRegistrar)ActivatorUtilities.GetServiceOrCreateInstance(p, typeof(TRegistrar));
            if (!string.IsNullOrEmpty(serviceName))
            {
                instance.ServiceName = serviceName;
            }

            if (configure != null)
            {
                configure(p, instance);
            }

            return instance;
        });

        return services;
    }

    public static IServiceCollection AddRabbitListenerEndpointRegistry(this IServiceCollection services, Action<IServiceProvider, RabbitListenerEndpointRegistry> configure = null)
    {
        services.AddRabbitListenerEndpointRegistry<RabbitListenerEndpointRegistry>(RabbitListenerEndpointRegistry.DEFAULT_SERVICE_NAME, configure);
        return services;
    }

    public static IServiceCollection AddRabbitListenerEndpointRegistry(this IServiceCollection services, string serviceName, Action<IServiceProvider, RabbitListenerEndpointRegistry> configure = null)
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            throw new ArgumentException(nameof(serviceName));
        }

        services.AddRabbitListenerEndpointRegistry<RabbitListenerEndpointRegistry>(serviceName, configure);
        return services;
    }

    public static IServiceCollection AddRabbitListenerEndpointRegistry<TRegistry>(this IServiceCollection services, Action<IServiceProvider, TRegistry> configure)
        where TRegistry : IRabbitListenerEndpointRegistry
    {
        return services.AddRabbitListenerEndpointRegistry(null, configure);
    }

    public static IServiceCollection AddRabbitListenerEndpointRegistry<TRegisty>(this IServiceCollection services, string serviceName = null, Action<IServiceProvider, TRegisty> configure = null)
        where TRegisty : IRabbitListenerEndpointRegistry
    {
        services.TryAddSingleton<IRabbitListenerEndpointRegistry>(p =>
        {
            var instance = (TRegisty)ActivatorUtilities.GetServiceOrCreateInstance(p, typeof(TRegisty));
            if (!string.IsNullOrEmpty(serviceName))
            {
                instance.ServiceName = serviceName;
            }

            if (configure != null)
            {
                configure(p, instance);
            }

            return instance;
        });

        services.AddSingleton(p =>
        {
            var instance = p.GetRequiredService<IRabbitListenerEndpointRegistry>() as ILifecycle;
            return instance;
        });

        return services;
    }

    public static IServiceCollection AddRabbitListenerContainerFactory(this IServiceCollection services, Action<IServiceProvider, DirectRabbitListenerContainerFactory> configure = null)
    {
        return services.AddRabbitListenerContainerFactory<DirectRabbitListenerContainerFactory>(DirectRabbitListenerContainerFactory.DEFAULT_SERVICE_NAME, configure);
    }

    public static IServiceCollection AddRabbitListenerContainerFactory(this IServiceCollection services, string serviceName, Action<IServiceProvider, DirectRabbitListenerContainerFactory> configure = null)
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            throw new ArgumentNullException(serviceName);
        }

        return services.AddRabbitListenerContainerFactory<DirectRabbitListenerContainerFactory>(serviceName, configure);
    }

    public static IServiceCollection AddRabbitListenerContainerFactory<TFactory>(this IServiceCollection services, Action<IServiceProvider, TFactory> configure)
        where TFactory : IRabbitListenerContainerFactory
    {
        return services.AddRabbitListenerContainerFactory(null, configure);
    }

    public static IServiceCollection AddRabbitListenerContainerFactory<TFactory>(this IServiceCollection services, string serviceName = null, Action<IServiceProvider, TFactory> configure = null)
        where TFactory : IRabbitListenerContainerFactory
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            throw new ArgumentException(nameof(serviceName));
        }

        services.AddSingleton<IRabbitListenerContainerFactory>(p =>
        {
            var instance = (TFactory)ActivatorUtilities.CreateInstance(p, typeof(TFactory));
            if (!string.IsNullOrEmpty(serviceName))
            {
                instance.ServiceName = serviceName;
            }

            if (configure != null)
            {
                configure(p, instance);
            }

            return instance;
        });
        return services;
    }

    public static IServiceCollection AddRabbitConnectionFactory(this IServiceCollection services, Action<IServiceProvider, CachingConnectionFactory> configure = null)
    {
        return services.AddRabbitConnectionFactory<CachingConnectionFactory>(CachingConnectionFactory.DEFAULT_SERVICE_NAME, configure);
    }

    public static IServiceCollection AddRabbitConnectionFactory(this IServiceCollection services, string serviceName, Action<IServiceProvider, CachingConnectionFactory> configure = null)
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            throw new ArgumentException(nameof(serviceName));
        }

        return services.AddRabbitConnectionFactory<CachingConnectionFactory>(serviceName, configure);
    }

    public static IServiceCollection AddRabbitConnectionFactory<TFactory>(this IServiceCollection services, Action<IServiceProvider, TFactory> configure)
        where TFactory : IConnectionFactory
    {
        return services.AddRabbitConnectionFactory(null, configure);
    }

    public static IServiceCollection AddRabbitConnectionFactory<TFactory>(this IServiceCollection services, string serviceName = null, Action<IServiceProvider, TFactory> configure = null)
        where TFactory : IConnectionFactory
    {
        services.AddSingleton(provider =>
        {
            using var scope = provider.CreateScope();
            var rabbitConnectionFactory = scope.ServiceProvider.GetService<RC.IConnectionFactory>() as RC.ConnectionFactory;

            IConnectionFactory instance =
                rabbitConnectionFactory is not null && typeof(TFactory) == typeof(CachingConnectionFactory) ?
                    new CachingConnectionFactory(rabbitConnectionFactory) :
                    (TFactory)ActivatorUtilities.GetServiceOrCreateInstance(provider, typeof(TFactory));

            if (!string.IsNullOrEmpty(serviceName))
            {
                instance.ServiceName = serviceName;
            }

            if (configure != null)
            {
                configure(provider, (TFactory)instance);
            }

            return instance;
        });

        return services;
    }

    public static IServiceCollection AddRabbitJsonMessageConverter(this IServiceCollection services, Action<IServiceProvider, Support.Converter.JsonMessageConverter> configure = null)
    {
        return services.AddRabbitMessageConverter(Support.Converter.JsonMessageConverter.DEFAULT_SERVICE_NAME, configure);
    }

    public static IServiceCollection AddRabbitJsonMessageConverter(this IServiceCollection services, string serviceName, Action<IServiceProvider, Support.Converter.JsonMessageConverter> configure = null)
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            throw new ArgumentException(serviceName);
        }

        return services.AddRabbitMessageConverter(serviceName, configure);
    }

    public static IServiceCollection AddRabbitDefaultMessageConverter(this IServiceCollection services, Action<IServiceProvider, Support.Converter.SimpleMessageConverter> configure = null)
    {
        return services.AddRabbitMessageConverter(SimpleMessageConverter.DEFAULT_SERVICE_NAME, configure);
    }

    public static IServiceCollection AddRabbitDefaultMessageConverter(this IServiceCollection services, string serviceName, Action<IServiceProvider, Support.Converter.SimpleMessageConverter> configure = null)
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            throw new ArgumentException(serviceName);
        }

        return services.AddRabbitMessageConverter(serviceName, configure);
    }

    public static IServiceCollection AddRabbitMessageConverter<TConverter>(this IServiceCollection services, Action<IServiceProvider, TConverter> configure)
        where TConverter : ISmartMessageConverter
    {
        return services.AddRabbitMessageConverter(null, configure);
    }

    public static IServiceCollection AddRabbitMessageConverter<TConverter>(this IServiceCollection services, string serviceName = null, Action<IServiceProvider, TConverter> configure = null)
        where TConverter : ISmartMessageConverter
    {
        services.TryAddSingleton<ISmartMessageConverter>(p =>
        {
            var instance = (TConverter)ActivatorUtilities.GetServiceOrCreateInstance(p, typeof(TConverter));
            if (!string.IsNullOrEmpty(serviceName))
            {
                instance.ServiceName = serviceName;
            }

            if (configure != null)
            {
                configure(p, instance);
            }

            return instance;
        });

        return services;
    }

    public static IServiceCollection AddRabbitMessageHandlerMethodFactory(this IServiceCollection services, Action<IServiceProvider, RabbitMessageHandlerMethodFactory> configure = null)
    {
        return services.AddRabbitMessageHandlerMethodFactory<RabbitMessageHandlerMethodFactory>(RabbitMessageHandlerMethodFactory.DEFAULT_SERVICE_NAME, configure);
    }

    public static IServiceCollection AddRabbitMessageHandlerMethodFactory(this IServiceCollection services, string serviceName, Action<IServiceProvider, RabbitMessageHandlerMethodFactory> configure = null)
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            throw new ArgumentException(serviceName);
        }

        return services.AddRabbitMessageHandlerMethodFactory<RabbitMessageHandlerMethodFactory>(serviceName, configure);
    }

    public static IServiceCollection AddRabbitMessageHandlerMethodFactory<TFactory>(this IServiceCollection services, Action<IServiceProvider, TFactory> configure)
        where TFactory : IMessageHandlerMethodFactory
    {
        return services.AddRabbitMessageHandlerMethodFactory(null, configure);
    }

    public static IServiceCollection AddRabbitMessageHandlerMethodFactory<TFactory>(this IServiceCollection services, string serviceName = null, Action<IServiceProvider, TFactory> configure = null)
        where TFactory : IMessageHandlerMethodFactory
    {
        services.TryAddSingleton<IMessageHandlerMethodFactory>(p =>
        {
            var instance = (TFactory)ActivatorUtilities.GetServiceOrCreateInstance(p, typeof(TFactory));
            if (!string.IsNullOrEmpty(serviceName))
            {
                instance.ServiceName = serviceName;
            }

            if (configure != null)
            {
                configure(p, instance);
            }

            instance.Initialize();

            return instance;
        });

        return services;
    }

    public static IServiceCollection AddRabbitDirecListenerContainer(this IServiceCollection services, Func<IServiceProvider, DirectMessageListenerContainer> factory)
    {
        return services.AddRabbitListenerContainer(factory);
    }

    public static IServiceCollection AddRabbitDirecListenerContainer(this IServiceCollection services, string serviceName = null, Action<IServiceProvider, DirectMessageListenerContainer> configure = null)
    {
        return services.AddRabbitListenerContainer(serviceName, configure);
    }

    public static IServiceCollection AddRabbitListenerContainer<TContainer>(this IServiceCollection services, Func<IServiceProvider, TContainer> factory)
        where TContainer : AbstractMessageListenerContainer
    {
        services.AddSingleton<ISmartLifecycle>(factory);

        return services;
    }

    public static IServiceCollection AddRabbitListenerContainer<TContainer>(this IServiceCollection services, string serviceName = null, Action<IServiceProvider, TContainer> configure = null)
        where TContainer : AbstractMessageListenerContainer
    {
        services.AddSingleton<ISmartLifecycle>(p =>
        {
            var instance = (TContainer)ActivatorUtilities.CreateInstance(p, typeof(TContainer));
            if (!string.IsNullOrEmpty(serviceName))
            {
                instance.ServiceName = serviceName;
            }

            if (configure != null)
            {
                configure(p, instance);
            }

            instance.Initialize();

            return instance;
        });

        return services;
    }

    public static IServiceCollection AddRabbitListenerErrorHandler<THandler>(this IServiceCollection services, string serviceName, Func<IServiceProvider, THandler> factory)
        where THandler : IRabbitListenerErrorHandler
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            throw new ArgumentException(serviceName);
        }

        services.RegisterService(serviceName, typeof(IRabbitListenerErrorHandler));
        services.AddSingleton<IRabbitListenerErrorHandler>(p =>
        {
            var result = factory(p);
            result.ServiceName = serviceName;
            return result;
        });

        return services;
    }

    public static IServiceCollection AddRabbitListenerErrorHandler<THandler>(this IServiceCollection services, string serviceName)
        where THandler : IRabbitListenerErrorHandler
    {
        if (string.IsNullOrEmpty(serviceName))
        {
            throw new ArgumentException(serviceName);
        }

        services.RegisterService(serviceName, typeof(IRabbitListenerErrorHandler));
        services.AddSingleton<IRabbitListenerErrorHandler>(p =>
        {
            var instance = (THandler)ActivatorUtilities.CreateInstance(p, typeof(THandler));
            instance.ServiceName = serviceName;
            return instance;
        });

        return services;
    }
}
