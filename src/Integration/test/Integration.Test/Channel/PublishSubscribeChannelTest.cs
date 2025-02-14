// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;
using Steeltoe.Common.Contexts;
using Steeltoe.Integration.Channel;
using Steeltoe.Messaging;
using Xunit;

namespace Steeltoe.Integration.Test.Channel;

public sealed class PublishSubscribeChannelTest
{
    [Fact]
    public void TestSend()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationServices, IntegrationServices>();
        ServiceProvider provider = services.BuildServiceProvider(true);
        var handler = new CounterHandler();
        var channel = new PublishSubscribeChannel(provider.GetService<IApplicationContext>());
        channel.Subscribe(handler);
        IMessage<string> message = Message.Create("test");
        Assert.True(channel.Send(message));
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public async Task TestSendAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationServices, IntegrationServices>();
        ServiceProvider provider = services.BuildServiceProvider(true);
        var handler = new CounterHandler();
        var channel = new PublishSubscribeChannel(provider.GetService<IApplicationContext>());
        channel.Subscribe(handler);
        IMessage<string> message = Message.Create("test");
        Assert.True(await channel.SendAsync(message));
        Assert.Equal(1, handler.Count);
    }

    [Fact]
    public void TestSendOneHandler_10_000_000()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationServices, IntegrationServices>();
        ServiceProvider provider = services.BuildServiceProvider(true);
        var handler = new CounterHandler();
        var channel = new PublishSubscribeChannel(provider.GetService<IApplicationContext>());
        channel.Subscribe(handler);
        IMessage<string> message = Message.Create("test");
        Assert.True(channel.Send(message));

        for (int i = 0; i < 10_000_000; i++)
        {
            channel.Send(message);
        }

        Assert.Equal(10_000_001, handler.Count);
    }

    [Fact]
    public async Task TestSendAsyncOneHandler_10_000_000()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationServices, IntegrationServices>();
        ServiceProvider provider = services.BuildServiceProvider(true);
        var handler = new CounterHandler();
        var channel = new PublishSubscribeChannel(provider.GetService<IApplicationContext>());
        channel.Subscribe(handler);
        IMessage<string> message = Message.Create("test");
        Assert.True(await channel.SendAsync(message));

        for (int i = 0; i < 10_000_000; i++)
        {
            await channel.SendAsync(message);
        }

        Assert.Equal(10_000_001, handler.Count);
    }

    [Fact]
    public async Task TestSendAsyncTwoHandler_10_000_000()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IIntegrationServices, IntegrationServices>();
        ServiceProvider provider = services.BuildServiceProvider(true);
        var handler1 = new CounterHandler();
        var handler2 = new CounterHandler();
        var channel = new PublishSubscribeChannel(provider.GetService<IApplicationContext>());
        channel.Subscribe(handler1);
        channel.Subscribe(handler2);
        IMessage<string> message = Message.Create("test");

        for (int i = 0; i < 10_000_000; i++)
        {
            await channel.SendAsync(message);
        }

        Assert.Equal(10_000_000, handler1.Count);
        Assert.Equal(10_000_000, handler2.Count);
    }

    private sealed class CounterHandler : IMessageHandler
    {
        public int Count { get; private set; }

        public string ServiceName { get; set; } = nameof(CounterHandler);

        public void HandleMessage(IMessage message)
        {
            Count++;
        }
    }
}
