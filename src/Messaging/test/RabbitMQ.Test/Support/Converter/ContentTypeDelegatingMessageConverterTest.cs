// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Text;
using Steeltoe.Messaging.Converter;
using Steeltoe.Messaging.RabbitMQ.Extensions;
using Steeltoe.Messaging.RabbitMQ.Support;
using Steeltoe.Messaging.RabbitMQ.Support.Converter;
using Xunit;

namespace Steeltoe.Messaging.RabbitMQ.Test.Support.Converter;

public sealed class ContentTypeDelegatingMessageConverterTest
{
    [Fact]
    public void TestDelegationOutbound()
    {
        var converter = new ContentTypeDelegatingMessageConverter();
        var messageConverter = new JsonMessageConverter();
        converter.AddDelegate("foo/bar", messageConverter);
        converter.AddDelegate(MessageHeaders.ContentTypeJson, messageConverter);

        var props = new RabbitHeaderAccessor();

        var foo = new Foo
        {
            FooString = "bar"
        };

        props.ContentType = "foo/bar";
        IMessage message = converter.ToMessage(foo, props.MessageHeaders);
        Assert.Equal(MessageHeaders.ContentTypeJson, message.Headers.ContentType());
        Assert.Equal("{\"fooString\":\"bar\"}", Encoding.UTF8.GetString((byte[])message.Payload));
        var converted = converter.FromMessage<Foo>(message);
        Assert.Equal("bar", converted.FooString);

        props = new RabbitHeaderAccessor
        {
            ContentType = MessageHeaders.ContentTypeJson
        };

        message = converter.ToMessage(foo, props.MessageHeaders);
        Assert.Equal("{\"fooString\":\"bar\"}", Encoding.UTF8.GetString((byte[])message.Payload));
        converted = converter.FromMessage<Foo>(message);
        Assert.Equal("bar", converted.FooString);

        converter = new ContentTypeDelegatingMessageConverter(null); // no default
        props = new RabbitHeaderAccessor();

        try
        {
            converter.ToMessage(foo, props.MessageHeaders);
            throw new Exception("Expected exception");
        }
        catch (Exception e)
        {
            Assert.IsType<MessageConversionException>(e);
            Assert.Contains("No delegate converter", e.Message, StringComparison.Ordinal);
        }
    }

    public sealed class Foo
    {
        public string FooString { get; set; }
    }
}
