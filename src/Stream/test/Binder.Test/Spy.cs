// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace Steeltoe.Stream.Binder.Test;

/// <summary>
/// Represents an out-of-band connection to the underlying middleware, so that tests can check that some messages actually do (or do not) transit through
/// it.
/// </summary>
public sealed class Spy
{
    public Func<bool, object> Receive { get; set; }
}
