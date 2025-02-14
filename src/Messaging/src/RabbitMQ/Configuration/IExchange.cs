// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Steeltoe.Common.Services;

namespace Steeltoe.Messaging.RabbitMQ.Configuration;

public interface IExchange : IDeclarable, IServiceNameAware
{
    string ExchangeName { get; set; }

    string Type { get; }

    bool IsDurable { get; set; }

    bool IsAutoDelete { get; set; }

    bool IsDelayed { get; set; }

    bool IsInternal { get; set; }
}
