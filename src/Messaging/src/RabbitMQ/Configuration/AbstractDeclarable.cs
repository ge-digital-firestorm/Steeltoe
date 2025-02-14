// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

namespace Steeltoe.Messaging.RabbitMQ.Configuration;

public abstract class AbstractDeclarable : IDeclarable
{
    public bool ShouldDeclare { get; set; }

    public Dictionary<string, object> Arguments { get; set; }

    public List<object> DeclaringAdmins { get; set; } = new();

    public bool IgnoreDeclarationExceptions { get; set; }

    protected AbstractDeclarable(Dictionary<string, object> arguments)
    {
        ShouldDeclare = true;
        Arguments = arguments != null ? new Dictionary<string, object>(arguments) : new Dictionary<string, object>();
    }

    public virtual void SetAdminsThatShouldDeclare(params object[] adminArgs)
    {
        var admins = new List<object>();

        if (adminArgs != null)
        {
            if (adminArgs.Length > 1)
            {
                foreach (object a in adminArgs)
                {
                    if (a == null)
                    {
                        throw new InvalidOperationException("'admins' cannot contain null elements");
                    }
                }
            }

            if (adminArgs.Length > 0 && !(adminArgs.Length == 1 && adminArgs[0] == null))
            {
                admins.AddRange(adminArgs);
            }
        }

        DeclaringAdmins = admins;
    }

    public void AddArgument(string name, object value)
    {
        Arguments[name] = value;
    }

    public object RemoveArgument(string name)
    {
        Arguments.Remove(name, out object result);
        return result;
    }
}
