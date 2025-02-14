// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections;

namespace Steeltoe.Common.Expression.Internal.Spring.Ast;

public class Projection : SpelNode
{
    private readonly bool _nullSafe;

    public Projection(bool nullSafe, int startPos, int endPos, SpelNode expression)
        : base(startPos, endPos, expression)
    {
        _nullSafe = nullSafe;
    }

    public override ITypedValue GetValueInternal(ExpressionState state)
    {
        return GetValueRef(state).GetValue();
    }

    public override string ToStringAst()
    {
        return $"![{GetChild(0).ToStringAst()}]";
    }

    protected internal override IValueRef GetValueRef(ExpressionState state)
    {
        ITypedValue op = state.GetActiveContextObject();

        object operand = op.Value;
        var operandAsArray = operand as Array;

        // When the input is a map, we push a special context object on the stack
        // before calling the specified operation. This special context object
        // has two fields 'key' and 'value' that refer to the map entries key
        // and value, and they can be referenced in the operation
        // eg. {'a':'y','b':'n'}.![value=='y'?key:null]" == ['a', null]
        if (operand is IDictionary mapData)
        {
            var result = new List<object>();

            foreach (object entry in mapData)
            {
                try
                {
                    state.PushActiveContextObject(new TypedValue(entry));
                    state.EnterScope();
                    result.Add(children[0].GetValueInternal(state).Value);
                }
                finally
                {
                    state.PopActiveContextObject();
                    state.ExitScope();
                }
            }

            return new TypedValueHolderValueRef(new TypedValue(result), this);
        }

        if (operand is IEnumerable data)
        {
            var result = new List<object>();
            Type arrayElementType = null;

            foreach (object element in data)
            {
                try
                {
                    state.PushActiveContextObject(new TypedValue(element));
                    state.EnterScope("index", result.Count);
                    object value = children[0].GetValueInternal(state).Value;

                    if (value != null && operandAsArray != null)
                    {
                        arrayElementType = DetermineCommonType(arrayElementType, value.GetType());
                    }

                    result.Add(value);
                }
                finally
                {
                    state.ExitScope();
                    state.PopActiveContextObject();
                }
            }

            if (operandAsArray != null)
            {
                arrayElementType ??= typeof(object);

                var resultArray = Array.CreateInstance(arrayElementType, result.Count);
                Array.Copy(result.ToArray(), 0, resultArray, 0, result.Count);
                return new TypedValueHolderValueRef(new TypedValue(resultArray), this);
            }

            return new TypedValueHolderValueRef(new TypedValue(result), this);
        }

        if (operand == null)
        {
            if (_nullSafe)
            {
                return NullValueRef.Instance;
            }

            throw new SpelEvaluationException(StartPosition, SpelMessage.ProjectionNotSupportedOnType, "null");
        }

        throw new SpelEvaluationException(StartPosition, SpelMessage.ProjectionNotSupportedOnType, operand.GetType().FullName);
    }

    private Type DetermineCommonType(Type oldType, Type newType)
    {
        if (oldType == null)
        {
            return newType;
        }

        if (oldType.IsAssignableFrom(newType))
        {
            return oldType;
        }

        Type nextType = newType;

        while (nextType != typeof(object))
        {
            if (nextType.IsAssignableFrom(oldType))
            {
                return nextType;
            }

            nextType = nextType.BaseType;
        }

        Type[] interfaces = newType.FindInterfaces((_, _) => true, null);

        foreach (Type nextInterface in interfaces)
        {
            if (nextInterface.IsAssignableFrom(oldType))
            {
                return nextInterface;
            }
        }

        return typeof(object);
    }
}
