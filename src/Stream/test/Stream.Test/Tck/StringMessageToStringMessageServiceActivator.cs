// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Steeltoe.Common.Util;
using Steeltoe.Integration.Attributes;
using Steeltoe.Messaging;
using Steeltoe.Messaging.Support;
using Steeltoe.Stream.Messaging;

namespace Steeltoe.Stream.Test.Tck;

public sealed class StringMessageToStringMessageServiceActivator
{
    [ServiceActivator(InputChannel = ISink.InputName, OutputChannel = ISource.OutputName)]
    public IMessage<string> Echo(IMessage<string> value)
    {
        var settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        // assume it is string because CT is text/plain
        var serializer = JsonSerializer.Create(settings);
        var textReader = new StringReader(value.Payload);
        var person = (Person)serializer.Deserialize(textReader, typeof(Person));

        return (IMessage<string>)MessageBuilder.WithPayload(person.ToString()).SetHeader(MessageHeaders.ContentType, MimeTypeUtils.TextPlain).Build();
    }
}
