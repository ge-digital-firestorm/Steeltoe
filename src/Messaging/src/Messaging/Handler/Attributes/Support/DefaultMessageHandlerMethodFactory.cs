// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using Steeltoe.Common.Contexts;
using Steeltoe.Common.Converter;
using Steeltoe.Messaging.Converter;
using Steeltoe.Messaging.Handler.Invocation;

namespace Steeltoe.Messaging.Handler.Attributes.Support;

public class DefaultMessageHandlerMethodFactory : IMessageHandlerMethodFactory
{
    public const string DefaultServiceName = nameof(DefaultMessageHandlerMethodFactory);

    protected readonly HandlerMethodArgumentResolverComposite ArgumentResolvers = new();

    public virtual string ServiceName { get; set; } = DefaultServiceName;

    public virtual IConversionService ConversionService { get; set; }

    public virtual IMessageConverter MessageConverter { get; set; }

    public virtual List<IHandlerMethodArgumentResolver> CustomArgumentResolvers { get; set; }

    public virtual IApplicationContext ApplicationContext { get; set; }

    public DefaultMessageHandlerMethodFactory(IApplicationContext context = null)
        : this(null, null, null, context)
    {
    }

    public DefaultMessageHandlerMethodFactory(IConversionService conversionService, IApplicationContext context = null)
        : this(conversionService, null, null, context)
    {
        ConversionService = conversionService;
    }

    public DefaultMessageHandlerMethodFactory(IConversionService conversionService, IMessageConverter converter, IApplicationContext context = null)
        : this(conversionService, converter, null, context)
    {
        ConversionService = conversionService;
        MessageConverter = converter;
    }

    public DefaultMessageHandlerMethodFactory(IConversionService conversionService, IMessageConverter converter, List<IHandlerMethodArgumentResolver> resolvers,
        IApplicationContext context = null)
    {
        ConversionService = conversionService;
        MessageConverter = converter;
        CustomArgumentResolvers = resolvers;

        ConversionService ??= new GenericConversionService();

        MessageConverter ??= new GenericMessageConverter(ConversionService);

        if (ArgumentResolvers.Resolvers.Count == 0)
        {
            ArgumentResolvers.AddResolvers(InitArgumentResolvers());
        }

        ApplicationContext = context;
    }

    public virtual void SetArgumentResolvers(List<IHandlerMethodArgumentResolver> argumentResolvers)
    {
        if (argumentResolvers == null)
        {
            ArgumentResolvers.Clear();
            return;
        }

        if (argumentResolvers.Count > 0)
        {
            ArgumentResolvers.Clear();
        }

        ArgumentResolvers.AddResolvers(argumentResolvers);
    }

    public virtual IInvocableHandlerMethod CreateInvocableHandlerMethod(object instance, MethodInfo method)
    {
        var handlerMethod = new InvocableHandlerMethod(instance, method)
        {
            MessageMethodArgumentResolvers = ArgumentResolvers
        };

        return handlerMethod;
    }

    public virtual void Initialize()
    {
        ArgumentResolvers.Clear();

        ConversionService ??= new GenericConversionService();

        MessageConverter ??= new GenericMessageConverter(ConversionService);

        if (ArgumentResolvers.Resolvers.Count == 0)
        {
            ArgumentResolvers.AddResolvers(InitArgumentResolvers());
        }
    }

    protected List<IHandlerMethodArgumentResolver> InitArgumentResolvers()
    {
        var resolvers = new List<IHandlerMethodArgumentResolver>
        {
            // Annotation-based argument resolution
            new HeaderMethodArgumentResolver(ConversionService, ApplicationContext),
            new HeadersMethodArgumentResolver(),

            // Type-based argument resolution
            new MessageMethodArgumentResolver(MessageConverter)
        };

        if (CustomArgumentResolvers != null)
        {
            resolvers.AddRange(CustomArgumentResolvers);
        }

        resolvers.Add(new PayloadMethodArgumentResolver(MessageConverter));

        return resolvers;
    }
}
