// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Hosting;

namespace Steeltoe.Messaging.RabbitMQ.Test.Host;

public sealed class MockRabbitHostedService : IHostedService, IDisposable
{
    public int StartCount { get; internal set; }

    public int StopCount { get; internal set; }

    public int DisposeCount { get; internal set; }

    public Action<CancellationToken> StartAction { get; set; }

    public Action<CancellationToken> StopAction { get; set; }

    public Action DisposeAction { get; set; }

    public void Dispose()
    {
        DisposeCount++;
        DisposeAction?.Invoke();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        StartCount++;
        StartAction?.Invoke(cancellationToken);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopCount++;
        StopAction?.Invoke(cancellationToken);
        return Task.CompletedTask;
    }
}
