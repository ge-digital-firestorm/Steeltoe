// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.DependencyInjection;
using Moq;
using Steeltoe.Common.Lifecycle;
using Steeltoe.Messaging;
using Steeltoe.Stream.Binder;
using Steeltoe.Stream.Configuration;
using Steeltoe.Stream.Messaging;
using Xunit;

namespace Steeltoe.Stream.Test.Binder;

public sealed class InputOutputBindingOrderTest : AbstractTest
{
    [Fact]
    public async Task TestInputOutputBindingOrder()
    {
        List<string> searchDirectories = GetSearchDirectories("MockBinder");
        ServiceCollection container = CreateStreamsContainerWithBinding(searchDirectories, typeof(IProcessor), "spring:cloud:stream:defaultBinder=mock");

        container.AddSingleton<SomeLifecycle>();
        container.AddSingleton<ILifecycle>(p => p.GetService<SomeLifecycle>());
        ServiceProvider provider = container.BuildServiceProvider(true);

        await provider.GetRequiredService<ILifecycleProcessor>().OnRefreshAsync(); // Only starts Autostart

        var factory = provider.GetService<IBinderFactory>();
        IBinder binder = factory.GetBinder(null, typeof(IMessageChannel));
        var processor = provider.GetService<IProcessor>();

        Mock<IBinder> mock = Mock.Get(binder);
        mock.Verify(b => b.BindConsumer("input", null, processor.Input, It.IsAny<ConsumerOptions>()));

        var lifecycle = provider.GetService<SomeLifecycle>();
        Assert.True(lifecycle.IsRunning);
    }

    public sealed class SomeLifecycle : ISmartLifecycle
    {
        public bool IsRunning { get; private set; }

        public IBinderFactory Factory { get; }

        public IProcessor Processor { get; }

        public bool IsAutoStartup => true;

        public int Phase => 0;

        public SomeLifecycle(IBinderFactory factory, IProcessor processor)
        {
            Factory = factory;
            Processor = processor;
        }

        public Task StartAsync()
        {
            IBinder binder = Factory.GetBinder(null, typeof(IMessageChannel));

            Mock<IBinder> mock = Mock.Get(binder);
            mock.Verify(b => b.BindProducer("output", Processor.Output, It.IsAny<ProducerOptions>()));

            IsRunning = true;
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            IsRunning = false;
            return Task.CompletedTask;
        }

        public async Task StopAsync(Action callback)
        {
            await StopAsync();
            callback?.Invoke();
        }
    }
}
