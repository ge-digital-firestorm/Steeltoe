﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Microsoft.Extensions.Logging;

namespace Steeltoe.Integration.Channel;

public class TaskSchedulerChannelWriter : AbstractSubscribableChannelWriter
{
    public TaskSchedulerChannelWriter(TaskSchedulerChannel channel, ILogger logger = null)
        : base(channel, logger)
    {
    }

    public new TaskSchedulerChannel Channel => (TaskSchedulerChannel)channel;
}