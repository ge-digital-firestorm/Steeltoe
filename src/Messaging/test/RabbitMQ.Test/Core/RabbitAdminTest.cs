// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Steeltoe.Common.Contexts;
using Steeltoe.Common.RetryPolly;
using Steeltoe.Messaging.RabbitMQ.Configuration;
using Steeltoe.Messaging.RabbitMQ.Connection;
using Steeltoe.Messaging.RabbitMQ.Core;
using Steeltoe.Messaging.RabbitMQ.Exceptions;
using Steeltoe.Messaging.RabbitMQ.Extensions;
using Steeltoe.Messaging.RabbitMQ.Test.Connection;
using Xunit;
using static Steeltoe.Messaging.RabbitMQ.Configuration.Binding;
using RC = RabbitMQ.Client;

namespace Steeltoe.Messaging.RabbitMQ.Test.Core;

[Trait("Category", "Integration")]
public sealed class RabbitAdminTest : AbstractTest
{
    [Fact]
    public void TestSettingOfNullConnectionFactory()
    {
        const IConnectionFactory connectionFactory = null;
        Assert.Throws<ArgumentNullException>(() => new RabbitAdmin(connectionFactory));
    }

    [Fact]
    public void TestFailOnFirstUseWithMissingBroker()
    {
        ServiceCollection serviceCollection = CreateContainer();
        serviceCollection.AddLogging();
        serviceCollection.AddRabbitQueue(new Queue("foo"));

        serviceCollection.AddRabbitConnectionFactory<SingleConnectionFactory>((_, f) =>
        {
            f.Host = "localhost";
            f.Port = 434343;
        });

        ServiceProvider provider = serviceCollection.BuildServiceProvider(true);
        var applicationContext = provider.GetService<IApplicationContext>();
        var connectionFactory = applicationContext.GetService<IConnectionFactory>();

        var rabbitAdmin = new RabbitAdmin(applicationContext, connectionFactory)
        {
            AutoStartup = true
        };

        Assert.Throws<RabbitConnectException>(() => rabbitAdmin.DeclareQueue());
        connectionFactory.Destroy();
    }

    [Fact]
    public async Task TestGetQueueProperties()
    {
        ServiceCollection serviceCollection = CreateContainer();
        serviceCollection.AddLogging();

        serviceCollection.AddRabbitConnectionFactory<SingleConnectionFactory>((_, f) =>
        {
            f.Host = "localhost";
        });

        ServiceProvider provider = serviceCollection.BuildServiceProvider(true);
        var applicationContext = provider.GetService<IApplicationContext>();
        var connectionFactory = applicationContext.GetService<IConnectionFactory>();
        var rabbitAdmin = new RabbitAdmin(applicationContext, connectionFactory);
        string queueName = $"test.properties.{DateTimeOffset.Now.ToUnixTimeMilliseconds()}";

        try
        {
            rabbitAdmin.DeclareQueue(new Queue(queueName));
            var template = new RabbitTemplate(connectionFactory);
            template.ConvertAndSend(queueName, "foo");
            int n = 0;

            while (n++ < 100 && MessageCount(rabbitAdmin, queueName) == 0)
            {
                await Task.Delay(100);
            }

            Assert.True(n < 100);
            RC.IModel channel = connectionFactory.CreateConnection().CreateChannel();
            var consumer = new RC.DefaultBasicConsumer(channel);
            RC.IModelExensions.BasicConsume(channel, queueName, true, consumer);
            n = 0;

            while (n++ < 100 && MessageCount(rabbitAdmin, queueName) > 0)
            {
                await Task.Delay(100);
            }

            Assert.True(n < 100);

            Dictionary<string, object> props = rabbitAdmin.GetQueueProperties(queueName);
            Assert.True(props.TryGetValue(RabbitAdmin.QueueConsumerCount, out object consumerCount));
            Assert.Equal(1U, consumerCount);
            channel.Close();
        }
        finally
        {
            rabbitAdmin.DeleteQueue(queueName);
            connectionFactory.Destroy();
        }
    }

    [Fact]
    public void TestTemporaryLogs()
    {
        ServiceCollection serviceCollection = CreateContainer();
        serviceCollection.AddLogging();
        serviceCollection.AddRabbitQueue(new Queue("testq.nonDur", false, false, false));
        serviceCollection.AddRabbitQueue(new Queue("testq.ad", true, false, true));
        serviceCollection.AddRabbitQueue(new Queue("testq.excl", true, true, false));
        serviceCollection.AddRabbitQueue(new Queue("testq.all", false, true, true));
        serviceCollection.AddRabbitExchange(new DirectExchange("testex.nonDur", false, false));
        serviceCollection.AddRabbitExchange(new DirectExchange("testex.ad", true, true));
        serviceCollection.AddRabbitExchange(new DirectExchange("testex.all", false, true));

        serviceCollection.AddRabbitConnectionFactory<SingleConnectionFactory>((_, f) =>
        {
            f.Host = "localhost";
        });

        ServiceProvider provider = serviceCollection.BuildServiceProvider(true);
        var applicationContext = provider.GetService<IApplicationContext>();
        var connectionFactory = applicationContext.GetService<IConnectionFactory>();

        var logs = new List<string>();
        var mockLogger = new Mock<ILogger>();

        mockLogger.Setup(l => l.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(),
            (Func<It.IsAnyType, Exception, string>)It.IsAny<object>())).Callback(new InvocationAction(invocation =>
        {
            logs.Add(invocation.Arguments[2].ToString());
        }));

        var rabbitAdmin = new RabbitAdmin(applicationContext, connectionFactory, mockLogger.Object);

        try
        {
            connectionFactory.CreateConnection().Close();
            logs.Sort();
            Assert.NotEmpty(logs);
            Assert.Contains("(testex.ad), durable:True, auto-delete:True", logs[0], StringComparison.Ordinal);
            Assert.Contains("(testex.all), durable:False, auto-delete:True", logs[1], StringComparison.Ordinal);
            Assert.Contains("(testex.nonDur), durable:False, auto-delete:False", logs[2], StringComparison.Ordinal);
            Assert.Contains("(testq.ad) durable:True, auto-delete:True, exclusive:False", logs[3], StringComparison.Ordinal);
            Assert.Contains("(testq.all) durable:False, auto-delete:True, exclusive:True", logs[4], StringComparison.Ordinal);
            Assert.Contains("(testq.excl) durable:True, auto-delete:False, exclusive:True", logs[5], StringComparison.Ordinal);
            Assert.Contains("(testq.nonDur) durable:False, auto-delete:False, exclusive:False", logs[6], StringComparison.Ordinal);
        }
        finally
        {
            CleanQueuesAndExchanges(rabbitAdmin);
            connectionFactory.Destroy();
        }
    }

    [Fact]
    public async Task TestMultiEntities()
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        serviceCollection.AddRabbitServices();
        serviceCollection.AddRabbitAdmin();
        var e1 = new DirectExchange("e1", false, true);
        serviceCollection.AddRabbitExchange(e1);
        var q1 = new Queue("q1", false, false, true);
        serviceCollection.AddRabbitQueue(q1);
        IBinding binding = BindingBuilder.Bind(q1).To(e1).With("k1");
        serviceCollection.AddRabbitBinding(binding);
        var es = new Declarables("es", new DirectExchange("e2", false, true), new DirectExchange("e3", false, true));
        serviceCollection.AddSingleton(es);
        var qs = new Declarables("qs", new Queue("q2", false, false, true), new Queue("q3", false, false, true));
        serviceCollection.AddSingleton(qs);

        var bs = new Declarables("bs", new Binding("b1", "q2", DestinationType.Queue, "e2", "k2", null),
            new Binding("b2", "q3", DestinationType.Queue, "e3", "k3", null));

        serviceCollection.AddSingleton(bs);

        var ds = new Declarables("ds", new DirectExchange("e4", false, true), new Queue("q4", false, false, true),
            new Binding("b3", "q4", DestinationType.Queue, "e4", "k4", null));

        serviceCollection.AddSingleton(ds);

        await using (ServiceProvider provider = serviceCollection.BuildServiceProvider(true))
        {
            RabbitAdmin admin = provider.GetRabbitAdmin();
            RabbitTemplate template = admin.RabbitTemplate;
            template.ConvertAndSend("e1", "k1", "foo");
            template.ConvertAndSend("e2", "k2", "bar");
            template.ConvertAndSend("e3", "k3", "baz");
            template.ConvertAndSend("e4", "k4", "qux");
            Assert.Equal("foo", template.ReceiveAndConvert<string>("q1"));
            Assert.Equal("bar", template.ReceiveAndConvert<string>("q2"));
            Assert.Equal("baz", template.ReceiveAndConvert<string>("q3"));
            Assert.Equal("qux", template.ReceiveAndConvert<string>("q4"));
            admin.DeleteQueue("q1");
            admin.DeleteQueue("q2");
            admin.DeleteQueue("q3");
            admin.DeleteQueue("q4");
            admin.DeleteExchange("e1");
            admin.DeleteExchange("e2");
            admin.DeleteExchange("e3");
            admin.DeleteExchange("e4");
        }

        await using (ServiceProvider provider = serviceCollection.BuildServiceProvider(true))
        {
            var ctx = provider.GetService<IApplicationContext>();
            var mixedDeclarables = ctx.GetService<Declarables>("ds");
            Assert.NotNull(mixedDeclarables);
            IEnumerable<IQueue> queues = mixedDeclarables.GetDeclarablesByType<IQueue>();
            Assert.Single(queues);
            Assert.Equal("q4", queues.Single().QueueName);
            IEnumerable<IExchange> exchanges = mixedDeclarables.GetDeclarablesByType<IExchange>();
            Assert.Single(exchanges);
            Assert.Equal("e4", exchanges.Single().ExchangeName);
            IEnumerable<IBinding> bindings = mixedDeclarables.GetDeclarablesByType<IBinding>();
            Assert.Single(bindings);
            Assert.Equal("q4", bindings.Single().Destination);
        }
    }

    [Fact]
    public void TestAvoidHangAMQP_508()
    {
        var cf = new CachingConnectionFactory("localhost");
        var admin = new RabbitAdmin(cf);
        byte[] bytes = new byte[300];
        string longName = Encoding.UTF8.GetString(bytes).Replace('\u0000', 'x');

        try
        {
            admin.DeclareQueue(new Queue(longName));
            throw new Exception("expected exception");
        }
        catch (Exception)
        {
            // Ignore
        }

        const string goodName = "foobar";
        admin.DeclareQueue(new Queue(goodName));
        Assert.Null(admin.GetQueueProperties(longName));
        Assert.NotNull(admin.GetQueueProperties(goodName));
        admin.DeleteQueue(goodName);
        cf.Destroy();
    }

    [Fact]
    public void TestIgnoreDeclarationExceptionsTimeout()
    {
        var rabbitConnectionFactory = new Mock<RC.IConnectionFactory>();
        var toBeThrown = new TimeoutException("test");
        rabbitConnectionFactory.Setup(c => c.CreateConnection(It.IsAny<string>())).Throws(toBeThrown);
        var ccf = new CachingConnectionFactory(rabbitConnectionFactory.Object);

        var admin = new RabbitAdmin(ccf)
        {
            IgnoreDeclarationExceptions = true
        };

        admin.DeclareQueue(new AnonymousQueue("test"));
        DeclarationExceptionEvent lastEvent = admin.LastDeclarationExceptionEvent;
        Assert.Same(admin, lastEvent.Source);
        Assert.Same(toBeThrown, lastEvent.Exception.InnerException);
        Assert.IsType<AnonymousQueue>(lastEvent.Declarable);

        admin.DeclareQueue();
        lastEvent = admin.LastDeclarationExceptionEvent;
        Assert.Same(admin, lastEvent.Source);
        Assert.Same(toBeThrown, lastEvent.Exception.InnerException);
        Assert.Null(lastEvent.Declarable);

        admin.DeclareExchange(new DirectExchange("foo"));
        lastEvent = admin.LastDeclarationExceptionEvent;
        Assert.Same(admin, lastEvent.Source);
        Assert.Same(toBeThrown, lastEvent.Exception.InnerException);
        Assert.IsType<DirectExchange>(lastEvent.Declarable);

        admin.DeclareBinding(new Binding("foo", "foo", DestinationType.Queue, "bar", "baz", null));
        lastEvent = admin.LastDeclarationExceptionEvent;
        Assert.Same(admin, lastEvent.Source);
        Assert.Same(toBeThrown, lastEvent.Exception.InnerException);
        Assert.IsType<Binding>(lastEvent.Declarable);
    }

    [Fact]
    public void TestWithinInvoke()
    {
        var connectionFactory = new Mock<IConnectionFactory>();
        var connection = new Mock<IConnection>();
        connectionFactory.Setup(f => f.CreateConnection()).Returns(connection.Object);

        var channel1 = new Mock<RC.IModel>();
        var channel2 = new Mock<RC.IModel>();

        connection.SetupSequence(c => c.CreateChannel(false)).Returns(channel1.Object).Returns(channel2.Object);
        var declareOk = new RC.QueueDeclareOk("foo", 0, 0);

        channel1.Setup(c => c.QueueDeclare(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, object>>()))
            .Returns(declareOk);

        var template = new RabbitTemplate(connectionFactory.Object);
        var admin = new RabbitAdmin(template);

        template.Invoke<object>(_ =>
        {
            admin.DeclareQueue();
            admin.DeclareQueue();
            admin.DeclareQueue();
            admin.DeclareQueue();
            return null;
        });

        connection.Verify(c => c.CreateChannel(false), Times.Once);

        channel1.Verify(c => c.QueueDeclare(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, object>>()),
            Times.Exactly(4));

        channel1.Verify(c => c.Close(), Times.Once);
        channel2.VerifyNoOtherCalls();
    }

    [Fact]
    public void TestRetry()
    {
        var connectionFactory = new Mock<RC.IConnectionFactory>();
        var connection = new Mock<RC.IConnection>();
        connection.Setup(c => c.IsOpen).Returns(true);
        connectionFactory.Setup(f => f.CreateConnection(It.IsAny<string>())).Returns(connection.Object);

        var channel1 = new Mock<RC.IModel>();
        channel1.Setup(c => c.IsOpen).Returns(true);
        connection.Setup(c => c.CreateModel()).Returns(channel1.Object);

        channel1.Setup(c => c.QueueDeclare(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, object>>()))
            .Throws<Exception>();

        var ccf = new CachingConnectionFactory(connectionFactory.Object);

        var rtt = new PollyRetryTemplate(new Dictionary<Type, bool>(), 3, true, 1, 1, 1);
        ServiceCollection serviceCollection = CreateContainer();
        serviceCollection.AddSingleton<IConnectionFactory>(ccf);

        serviceCollection.AddRabbitAdmin((_, a) =>
        {
            a.RetryTemplate = rtt;
        });

        var foo = new AnonymousQueue("foo");
        serviceCollection.AddRabbitQueue(foo);
        ServiceProvider provider = serviceCollection.BuildServiceProvider(true);
        _ = provider.GetRabbitAdmin();
        Assert.Throws<RabbitUncategorizedException>(() => ccf.CreateConnection());

        channel1.Verify(c => c.QueueDeclare(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<IDictionary<string, object>>()),
            Times.Exactly(3));
    }

    [Fact]
    public async Task TestMasterLocator()
    {
        var factory = new RC.ConnectionFactory
        {
            Uri = new Uri("amqp://guest:guest@localhost:5672/")
        };

        var cf = new CachingConnectionFactory(factory);
        var admin = new RabbitAdmin(cf);
        var queue = new AnonymousQueue();
        admin.DeclareQueue(queue);
        var client = new HttpClient();
        byte[] authToken = Encoding.ASCII.GetBytes("guest:guest");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(authToken));

        HttpResponseMessage result = await client.GetAsync(new Uri($"http://localhost:15672/api/queues/%3F/{queue.QueueName}"));
        int n = 0;

        while (n++ < 100 && result.StatusCode == HttpStatusCode.NotFound)
        {
            await Task.Delay(100);
            result = await client.GetAsync(new Uri($"http://localhost:15672/api/queues/%2F/{queue.QueueName}"));
        }

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        string content = await result.Content.ReadAsStringAsync();
        Assert.Contains("x-queue-master-locator", content, StringComparison.Ordinal);
        Assert.Contains("client-local", content, StringComparison.Ordinal);

        queue = new AnonymousQueue
        {
            MasterLocator = null
        };

        admin.DeclareQueue(queue);

        result = await client.GetAsync(new Uri($"http://localhost:15672/api/queues/%3F/{queue.QueueName}"));
        n = 0;

        while (n++ < 100 && result.StatusCode == HttpStatusCode.NotFound)
        {
            await Task.Delay(100);
            result = await client.GetAsync(new Uri($"http://localhost:15672/api/queues/%2F/{queue.QueueName}"));
        }

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        content = await result.Content.ReadAsStringAsync();
        Assert.DoesNotContain("x-queue-master-locator", content, StringComparison.Ordinal);
        Assert.DoesNotContain("client-local", content, StringComparison.Ordinal);
        cf.Destroy();
    }

    private void CleanQueuesAndExchanges(RabbitAdmin rabbitAdmin)
    {
        rabbitAdmin.DeleteQueue("testq.nonDur");
        rabbitAdmin.DeleteQueue("testq.ad");
        rabbitAdmin.DeleteQueue("testq.excl");
        rabbitAdmin.DeleteQueue("testq.all");
        rabbitAdmin.DeleteExchange("testex.nonDur");
        rabbitAdmin.DeleteExchange("testex.ad");
        rabbitAdmin.DeleteExchange("testex.all");
    }

    private uint MessageCount(RabbitAdmin rabbitAdmin, string queueName)
    {
        QueueInformation info = rabbitAdmin.GetQueueInfo(queueName);
        Assert.NotNull(info);
        return info.MessageCount;
    }
}
