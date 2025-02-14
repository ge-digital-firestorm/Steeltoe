// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace Steeltoe.Common.Order;

public class OrderComparer : IComparer<IOrdered>
{
    public static OrderComparer Instance { get; } = new();

    public int Compare(IOrdered o1, IOrdered o2)
    {
        bool p1 = o1 is IPriorityOrdered;
        bool p2 = o2 is IPriorityOrdered;

        if (p1 && !p2)
        {
            return -1;
        }

        if (p2 && !p1)
        {
            return 1;
        }

        int i1 = GetOrder(o1);
        int i2 = GetOrder(o2);

        return GetOrder(i1, i2);
    }

    protected int GetOrder(int i1, int i2)
    {
#pragma warning disable S3358 // Ternary operators should not be nested
        return i1 < i2 ? -1 : i1 == i2 ? 0 : 1;
#pragma warning restore S3358 // Ternary operators should not be nested
    }

    protected int GetOrder(IOrdered o1)
    {
        if (o1 != null)
        {
            return o1.Order;
        }

        return AbstractOrdered.LowestPrecedence;
    }
}
