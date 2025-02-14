// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace Steeltoe.Common.Expression.Internal.Spring;

public class SpelEvaluationException : EvaluationException
{
    public SpelMessage MessageCode { get; }
    public object[] Inserts { get; }

    public SpelEvaluationException(SpelMessage message, params object[] inserts)
        : base(message.FormatMessage(inserts))
    {
        MessageCode = message;
        Inserts = inserts;
    }

    public SpelEvaluationException(int position, SpelMessage message, params object[] inserts)
        : base(position, message.FormatMessage(inserts))
    {
        MessageCode = message;
        Inserts = inserts;
    }

    public SpelEvaluationException(int position, Exception innerException, SpelMessage message, params object[] inserts)
        : base(position, message.FormatMessage(inserts), innerException)
    {
        MessageCode = message;
        Inserts = inserts;
    }

    public SpelEvaluationException(Exception innerException, SpelMessage message, params object[] inserts)
        : base(message.FormatMessage(inserts), innerException)
    {
        MessageCode = message;
        Inserts = inserts;
    }
}
