// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using Steeltoe.Common.Expression.Internal.Spring.Common;
using Steeltoe.Common.Expression.Internal.Spring.Support;

namespace Steeltoe.Common.Expression.Internal.Spring.Ast;

public class ConstructorReference : SpelNode
{
    private readonly bool _isArrayConstructor;

    private readonly SpelNode[] _dimensions;

    // Is this caching safe - passing the expression around will mean this executor is also being passed around
    // The cached executor that may be reused on subsequent evaluations.
    private volatile IConstructorExecutor _cachedExecutor;

    private bool HasInitializer => ChildCount > 1;

    public ConstructorReference(int startPos, int endPos, params SpelNode[] arguments)
        : base(startPos, endPos, arguments)
    {
        _isArrayConstructor = false;
    }

    public ConstructorReference(int startPos, int endPos, SpelNode[] dimensions, params SpelNode[] arguments)
        : base(startPos, endPos, arguments)
    {
        _isArrayConstructor = true;
        _dimensions = dimensions;
    }

    public override ITypedValue GetValueInternal(ExpressionState state)
    {
        if (_isArrayConstructor)
        {
            return CreateArray(state);
        }

        return CreateNewInstance(state);
    }

    public override bool IsCompilable()
    {
        if (_cachedExecutor is not ReflectiveConstructorExecutor executor || exitTypeDescriptor == null)
        {
            return false;
        }

        if (ChildCount > 1)
        {
            for (int c = 1, max = ChildCount; c < max; c++)
            {
                if (!children[c].IsCompilable())
                {
                    return false;
                }
            }
        }

        ConstructorInfo constructor = executor.Constructor;
        return constructor.IsPublic && ReflectionHelper.IsPublic(constructor.DeclaringType);
    }

    public override void GenerateCode(ILGenerator gen, CodeFlow cf)
    {
        var executor = (ReflectiveConstructorExecutor)_cachedExecutor;

        if (executor == null)
        {
            throw new InvalidOperationException("No cached executor");
        }

        ConstructorInfo constructor = executor.Constructor;

        // children[0] is the type of the constructor, don't want to include that in argument processing
        var arguments = new SpelNode[children.Length - 1];
        Array.Copy(children, 1, arguments, 0, children.Length - 1);
        GenerateCodeForArguments(gen, cf, constructor, arguments);
        gen.Emit(OpCodes.Newobj, constructor);
        cf.PushDescriptor(exitTypeDescriptor);
    }

    public override string ToStringAst()
    {
        var sb = new StringBuilder("new ");
        int index = 0;
        sb.Append(GetChild(index++).ToStringAst());
        sb.Append('(');

        for (int i = index; i < ChildCount; i++)
        {
            if (i > index)
            {
                sb.Append(',');
            }

            sb.Append(GetChild(i).ToStringAst());
        }

        sb.Append(')');
        return sb.ToString();
    }

    private ITypedValue CreateNewInstance(ExpressionState state)
    {
        object[] arguments = new object[ChildCount - 1];
        var argumentTypes = new List<Type>(ChildCount - 1);

        for (int i = 0; i < arguments.Length; i++)
        {
            ITypedValue childValue = children[i + 1].GetValueInternal(state);
            object value = childValue.Value;
            arguments[i] = value;
            Type valueType = value?.GetType();
            argumentTypes.Add(valueType);
        }

        IConstructorExecutor executorToUse = _cachedExecutor;

        if (executorToUse != null)
        {
            try
            {
                return executorToUse.Execute(state.EvaluationContext, arguments);
            }
            catch (AccessException ex)
            {
                // Two reasons this can occur:
                // 1. the method invoked actually threw a real exception
                // 2. the method invoked was not passed the arguments it expected and has become 'stale'

                // In the first case we should not retry, in the second case we should see if there is a
                // better suited method.

                // To determine which situation it is, the AccessException will contain a cause.
                // If the cause is an InvocationTargetException, a user exception was thrown inside the constructor.
                // Otherwise the constructor could not be invoked.
                if (ex.InnerException is TargetInvocationException)
                {
                    // User exception was the root cause - exit now
                    Exception rootCause = ex.InnerException.InnerException;

                    if (rootCause is SystemException)
                    {
                        throw rootCause;
                    }

                    string name = (string)children[0].GetValueInternal(state).Value;

                    throw new SpelEvaluationException(StartPosition, rootCause, SpelMessage.ConstructorInvocationProblem, name,
                        FormatHelper.FormatMethodForMessage(string.Empty, argumentTypes));
                }

                // At this point we know it wasn't a user problem so worth a retry if a better candidate can be found
                _cachedExecutor = null;
            }
        }

        // Either there was no accessor or it no longer exists
        string typeName = (string)children[0].GetValueInternal(state).Value;

        if (typeName == null)
        {
            throw new InvalidOperationException("No type name");
        }

        executorToUse = FindExecutorForConstructor(typeName, argumentTypes, state);

        try
        {
            _cachedExecutor = executorToUse;

            if (executorToUse is ReflectiveConstructorExecutor executor)
            {
                exitTypeDescriptor = CodeFlow.ToDescriptor(executor.Constructor.DeclaringType);
            }

            return executorToUse.Execute(state.EvaluationContext, arguments);
        }
        catch (AccessException ex)
        {
            throw new SpelEvaluationException(StartPosition, ex, SpelMessage.ConstructorInvocationProblem, typeName,
                FormatHelper.FormatMethodForMessage(string.Empty, argumentTypes));
        }
    }

    private IConstructorExecutor FindExecutorForConstructor(string typeName, List<Type> argumentTypes, ExpressionState state)
    {
        IEvaluationContext evalContext = state.EvaluationContext;
        List<IConstructorResolver> ctorResolvers = evalContext.ConstructorResolvers;

        foreach (IConstructorResolver ctorResolver in ctorResolvers)
        {
            try
            {
                IConstructorExecutor ce = ctorResolver.Resolve(state.EvaluationContext, typeName, argumentTypes);

                if (ce != null)
                {
                    return ce;
                }
            }
            catch (AccessException ex)
            {
                throw new SpelEvaluationException(StartPosition, ex, SpelMessage.ConstructorInvocationProblem, typeName,
                    FormatHelper.FormatMethodForMessage(string.Empty, argumentTypes));
            }
        }

        throw new SpelEvaluationException(StartPosition, SpelMessage.ConstructorNotFound, typeName,
            FormatHelper.FormatMethodForMessage(string.Empty, argumentTypes));
    }

    private TypedValue CreateArray(ExpressionState state)
    {
        // First child gives us the array type which will either be a primitive or reference type
        object intendedArrayType = GetChild(0).GetValue(state);

        if (intendedArrayType is not string type)
        {
            throw new SpelEvaluationException(GetChild(0).StartPosition, SpelMessage.TypeNameExpectedForArrayConstruction,
                FormatHelper.FormatClassNameForMessage(intendedArrayType?.GetType()));
        }

        SpelTypeCode arrayTypeCode = SpelTypeCode.ForName(type);
        Type componentType = arrayTypeCode == SpelTypeCode.Object ? state.FindType(type) : arrayTypeCode.Type;

        object newArray;

        if (!HasInitializer)
        {
            // Confirm all dimensions were specified (for example [3][][5] is missing the 2nd dimension)
            if (_dimensions != null)
            {
                foreach (SpelNode dimension in _dimensions)
                {
                    if (dimension == null)
                    {
                        throw new SpelEvaluationException(StartPosition, SpelMessage.MissingArrayDimension);
                    }
                }
            }
            else
            {
                throw new SpelEvaluationException(StartPosition, SpelMessage.MissingArrayDimension);
            }

            ITypeConverter typeConverter = state.EvaluationContext.TypeConverter;

            // Shortcut for 1 dimensional
            if (_dimensions.Length == 1)
            {
                ITypedValue o = _dimensions[0].GetTypedValue(state);
                int arraySize = ExpressionUtils.ToInt(typeConverter, o);
                newArray = Array.CreateInstance(componentType, arraySize);
            }
            else
            {
                // Multi-dimensional - hold onto your hat!
                int[] dims = new int[_dimensions.Length];

                for (int d = 0; d < _dimensions.Length; d++)
                {
                    ITypedValue o = _dimensions[d].GetTypedValue(state);
                    dims[d] = ExpressionUtils.ToInt(typeConverter, o);
                }

                newArray = Array.CreateInstance(componentType, dims);
            }
        }
        else
        {
            // There is an initializer
            if (_dimensions == null || _dimensions.Length > 1)
            {
                // There is an initializer but this is a multi-dimensional array (e.g. new int[][]{{1,2},{3,4}}) - this
                // is not currently supported
                throw new SpelEvaluationException(StartPosition, SpelMessage.MultidimensionalArrayInitializerNotSupported);
            }

            ITypeConverter typeConverter = state.EvaluationContext.TypeConverter;
            var initializer = (InlineList)GetChild(1);

            // If a dimension was specified, check it matches the initializer length
            if (_dimensions[0] != null)
            {
                ITypedValue dValue = _dimensions[0].GetTypedValue(state);
                int i = ExpressionUtils.ToInt(typeConverter, dValue);

                if (i != initializer.ChildCount)
                {
                    throw new SpelEvaluationException(StartPosition, SpelMessage.InitializerLengthIncorrect);
                }
            }

            // Build the array and populate it
            int arraySize = initializer.ChildCount;
            newArray = Array.CreateInstance(componentType, arraySize);

            if (arrayTypeCode == SpelTypeCode.Object)
            {
                PopulateReferenceTypeArray(state, newArray, typeConverter, initializer, componentType);
            }
            else if (arrayTypeCode == SpelTypeCode.Boolean)
            {
                PopulateBooleanArray(state, newArray, typeConverter, initializer);
            }
            else if (arrayTypeCode == SpelTypeCode.Byte)
            {
                PopulateByteArray(state, newArray, typeConverter, initializer);
            }
            else if (arrayTypeCode == SpelTypeCode.Sbyte)
            {
                PopulateSByteArray(state, newArray, typeConverter, initializer);
            }
            else if (arrayTypeCode == SpelTypeCode.Char)
            {
                PopulateCharArray(state, newArray, typeConverter, initializer);
            }
            else if (arrayTypeCode == SpelTypeCode.Double)
            {
                PopulateDoubleArray(state, newArray, typeConverter, initializer);
            }
            else if (arrayTypeCode == SpelTypeCode.Float)
            {
                PopulateFloatArray(state, newArray, typeConverter, initializer);
            }
            else if (arrayTypeCode == SpelTypeCode.Int)
            {
                PopulateIntArray(state, newArray, typeConverter, initializer);
            }
            else if (arrayTypeCode == SpelTypeCode.Uint)
            {
                PopulateUIntArray(state, newArray, typeConverter, initializer);
            }
            else if (arrayTypeCode == SpelTypeCode.Long)
            {
                PopulateLongArray(state, newArray, typeConverter, initializer);
            }
            else if (arrayTypeCode == SpelTypeCode.Ulong)
            {
                PopulateULongArray(state, newArray, typeConverter, initializer);
            }
            else if (arrayTypeCode == SpelTypeCode.Short)
            {
                PopulateShortArray(state, newArray, typeConverter, initializer);
            }
            else if (arrayTypeCode == SpelTypeCode.Ushort)
            {
                PopulateUShortArray(state, newArray, typeConverter, initializer);
            }
            else
            {
                throw new InvalidOperationException(arrayTypeCode.Name);
            }
        }

        return new TypedValue(newArray);
    }

    private void PopulateReferenceTypeArray(ExpressionState state, object newArray, ITypeConverter typeConverter, InlineList initializer, Type componentType)
    {
        object[] newObjectArray = (object[])newArray;

        for (int i = 0; i < newObjectArray.Length; i++)
        {
            ISpelNode elementNode = initializer.GetChild(i);
            object arrayEntry = elementNode.GetValue(state);
            newObjectArray[i] = typeConverter.ConvertValue(arrayEntry, arrayEntry?.GetType(), componentType);
        }
    }

    private void PopulateByteArray(ExpressionState state, object newArray, ITypeConverter typeConverter, InlineList initializer)
    {
        byte[] newByteArray = (byte[])newArray;

        for (int i = 0; i < newByteArray.Length; i++)
        {
            ITypedValue typedValue = initializer.GetChild(i).GetTypedValue(state);
            newByteArray[i] = ExpressionUtils.ToByte(typeConverter, typedValue);
        }
    }

    private void PopulateSByteArray(ExpressionState state, object newArray, ITypeConverter typeConverter, InlineList initializer)
    {
        sbyte[] newByteArray = (sbyte[])newArray;

        for (int i = 0; i < newByteArray.Length; i++)
        {
            ITypedValue typedValue = initializer.GetChild(i).GetTypedValue(state);
            newByteArray[i] = ExpressionUtils.ToSByte(typeConverter, typedValue);
        }
    }

    private void PopulateFloatArray(ExpressionState state, object newArray, ITypeConverter typeConverter, InlineList initializer)
    {
        float[] newFloatArray = (float[])newArray;

        for (int i = 0; i < newFloatArray.Length; i++)
        {
            ITypedValue typedValue = initializer.GetChild(i).GetTypedValue(state);
            newFloatArray[i] = ExpressionUtils.ToFloat(typeConverter, typedValue);
        }
    }

    private void PopulateDoubleArray(ExpressionState state, object newArray, ITypeConverter typeConverter, InlineList initializer)
    {
        double[] newDoubleArray = (double[])newArray;

        for (int i = 0; i < newDoubleArray.Length; i++)
        {
            ITypedValue typedValue = initializer.GetChild(i).GetTypedValue(state);
            newDoubleArray[i] = ExpressionUtils.ToDouble(typeConverter, typedValue);
        }
    }

    private void PopulateShortArray(ExpressionState state, object newArray, ITypeConverter typeConverter, InlineList initializer)
    {
        short[] newShortArray = (short[])newArray;

        for (int i = 0; i < newShortArray.Length; i++)
        {
            ITypedValue typedValue = initializer.GetChild(i).GetTypedValue(state);
            newShortArray[i] = ExpressionUtils.ToShort(typeConverter, typedValue);
        }
    }

    private void PopulateUShortArray(ExpressionState state, object newArray, ITypeConverter typeConverter, InlineList initializer)
    {
        ushort[] newShortArray = (ushort[])newArray;

        for (int i = 0; i < newShortArray.Length; i++)
        {
            ITypedValue typedValue = initializer.GetChild(i).GetTypedValue(state);
            newShortArray[i] = ExpressionUtils.ToUShort(typeConverter, typedValue);
        }
    }

    private void PopulateLongArray(ExpressionState state, object newArray, ITypeConverter typeConverter, InlineList initializer)
    {
        long[] newLongArray = (long[])newArray;

        for (int i = 0; i < newLongArray.Length; i++)
        {
            ITypedValue typedValue = initializer.GetChild(i).GetTypedValue(state);
            newLongArray[i] = ExpressionUtils.ToLong(typeConverter, typedValue);
        }
    }

    private void PopulateULongArray(ExpressionState state, object newArray, ITypeConverter typeConverter, InlineList initializer)
    {
        ulong[] newLongArray = (ulong[])newArray;

        for (int i = 0; i < newLongArray.Length; i++)
        {
            ITypedValue typedValue = initializer.GetChild(i).GetTypedValue(state);
            newLongArray[i] = ExpressionUtils.ToULong(typeConverter, typedValue);
        }
    }

    private void PopulateCharArray(ExpressionState state, object newArray, ITypeConverter typeConverter, InlineList initializer)
    {
        char[] newCharArray = (char[])newArray;

        for (int i = 0; i < newCharArray.Length; i++)
        {
            ITypedValue typedValue = initializer.GetChild(i).GetTypedValue(state);
            newCharArray[i] = ExpressionUtils.ToChar(typeConverter, typedValue);
        }
    }

    private void PopulateBooleanArray(ExpressionState state, object newArray, ITypeConverter typeConverter, InlineList initializer)
    {
        bool[] newBooleanArray = (bool[])newArray;

        for (int i = 0; i < newBooleanArray.Length; i++)
        {
            ITypedValue typedValue = initializer.GetChild(i).GetTypedValue(state);
            newBooleanArray[i] = ExpressionUtils.ToBoolean(typeConverter, typedValue);
        }
    }

    private void PopulateIntArray(ExpressionState state, object newArray, ITypeConverter typeConverter, InlineList initializer)
    {
        int[] newIntArray = (int[])newArray;

        for (int i = 0; i < newIntArray.Length; i++)
        {
            ITypedValue typedValue = initializer.GetChild(i).GetTypedValue(state);
            newIntArray[i] = ExpressionUtils.ToInt(typeConverter, typedValue);
        }
    }

    private void PopulateUIntArray(ExpressionState state, object newArray, ITypeConverter typeConverter, InlineList initializer)
    {
        uint[] newIntArray = (uint[])newArray;

        for (int i = 0; i < newIntArray.Length; i++)
        {
            ITypedValue typedValue = initializer.GetChild(i).GetTypedValue(state);
            newIntArray[i] = ExpressionUtils.ToUInt(typeConverter, typedValue);
        }
    }
}
