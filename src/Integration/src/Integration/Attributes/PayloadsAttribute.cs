// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace Steeltoe.Integration.Attributes;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Parameter)]
public sealed class PayloadsAttribute : Attribute
{
    public string Expression { get; set; }

    public PayloadsAttribute()
    {
        Expression = string.Empty;
    }

    public PayloadsAttribute(string expression)
    {
        Expression = expression;
    }
}
