// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Text;
using Steeltoe.Messaging.RabbitMQ.Core;
using Steeltoe.Messaging.RabbitMQ.Extensions;
using Steeltoe.Messaging.RabbitMQ.Support;
using Xunit;

namespace Steeltoe.Messaging.RabbitMQ.Test.Core;

public sealed class AddressTest
{
    [Fact]
    public void ToStringCheck()
    {
        var address = new Address("my-exchange", "routing-key");
        const string replyToUri = "my-exchange/routing-key";
        Assert.Equal(replyToUri, address.ToString());
    }

    [Fact]
    public void Parse()
    {
        const string replyToUri = "direct://my-exchange/routing-key";
        var address = new Address(replyToUri);
        Assert.Equal("my-exchange", address.ExchangeName);
        Assert.Equal("routing-key", address.RoutingKey);
    }

    [Fact]
    public void ParseUnstructuredWithRoutingKeyOnly()
    {
        var address = new Address("my-routing-key");
        Assert.Equal("my-routing-key", address.RoutingKey);
        Assert.Equal("/my-routing-key", address.ToString());

        address = new Address("/foo");
        Assert.Equal("foo", address.RoutingKey);
        Assert.Equal("/foo", address.ToString());

        address = new Address("bar/baz");
        Assert.Equal("bar", address.ExchangeName);
        Assert.Equal("baz", address.RoutingKey);
        Assert.Equal("bar/baz", address.ToString());
    }

    [Fact]
    public void ParseWithoutRoutingKey()
    {
        var address = new Address("fanout://my-exchange");
        Assert.Equal("my-exchange", address.ExchangeName);
        Assert.Equal(string.Empty, address.RoutingKey);
        Assert.Equal("my-exchange/", address.ToString());
    }

    [Fact]
    public void ParseWithDefaultExchangeAndRoutingKey()
    {
        var address = new Address("direct:///routing-key");
        Assert.Equal(string.Empty, address.ExchangeName);
        Assert.Equal("routing-key", address.RoutingKey);
        Assert.Equal("/routing-key", address.ToString());
    }

    [Fact]
    public void TestEmpty()
    {
        var address = new Address("/");
        Assert.Equal(string.Empty, address.ExchangeName);
        Assert.Equal(string.Empty, address.RoutingKey);
        Assert.Equal("/", address.ToString());
    }

    [Fact]
    public void TestDirectReplyTo()
    {
        const string replyTo = $"{Address.AmqRabbitMQReplyTo}.ab/cd/ef";
        var headers = new MessageHeaders();
        RabbitHeaderAccessor props = RabbitHeaderAccessor.GetMutableAccessor(headers);
        props.ReplyTo = replyTo;
        IMessage<byte[]> message = Message.Create(Encoding.UTF8.GetBytes("foo"), props.MessageHeaders);
        Address address = message.Headers.ReplyToAddress();
        Assert.Equal(string.Empty, address.ExchangeName);
        Assert.Equal(replyTo, address.RoutingKey);
        address = props.ReplyToAddress;
        Assert.Equal(string.Empty, address.ExchangeName);
        Assert.Equal(replyTo, address.RoutingKey);
    }

    [Fact]
    public void TestEquals()
    {
        Assert.Equal(new Address("foo/bar"), new Address("foo/bar"));
    }
}
