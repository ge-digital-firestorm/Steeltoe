// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Steeltoe.Integration.Extensions;
using Steeltoe.Integration.Support;
using Steeltoe.Integration.Support.Converter;
using Steeltoe.Messaging;
using Xunit;

namespace Steeltoe.Stream.Test.Extensions;

public sealed class IntegrationServicesExtensionsTest
{
    [Fact]
    public void AddIntegrationServices_AddsServices()
    {
        var container = new ServiceCollection();
        container.AddOptions();
        container.AddLogging(b => b.AddConsole());
        container.AddIntegrationServices();
        ServiceProvider serviceProvider = container.BuildServiceProvider(true);

        Assert.NotNull(serviceProvider.GetService<DefaultDataTypeChannelMessageConverter>());
        Assert.NotNull(serviceProvider.GetService<IMessageBuilderFactory>());

        IEnumerable<IMessageChannel> channels = serviceProvider.GetServices<IMessageChannel>();
        Assert.Equal(2, channels.Count());
    }
}
