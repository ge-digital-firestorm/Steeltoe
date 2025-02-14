// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace Steeltoe.Common.Transaction;

public abstract class AbstractTransactionSynchronization : ITransactionSynchronization
{
    public const int StatusCommitted = 0;

    public const int StatusRolledBack = 1;

    public const int StatusUnknown = 2;

    public abstract void AfterCommit();

    public abstract void AfterCompletion(int status);

    public abstract void BeforeCommit(bool readOnly);

    public abstract void BeforeCompletion();

    public abstract void Flush();

    public abstract void Resume();

    public abstract void Suspend();
}
