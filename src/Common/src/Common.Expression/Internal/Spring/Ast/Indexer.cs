// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using Steeltoe.Common.Expression.Internal.Spring.Support;

namespace Steeltoe.Common.Expression.Internal.Spring.Ast;

public class Indexer : SpelNode
{
    private static readonly MethodInfo ListGetItemMethod = typeof(IList).GetMethods().Single(m => m.Name == "get_Item");
    private static readonly MethodInfo DictionaryGetItemMethod = typeof(IDictionary).GetMethods().Single(m => m.Name == "get_Item");

    // These fields are used when the indexer is being used as a property read accessor.
    // If the name and target type match these cached values then the cachedReadAccessor
    // is used to read the property. If they do not match, the correct accessor is
    // discovered and then cached for later use.
    private string _cachedReadName;

    private Type _cachedReadTargetType;

    private IPropertyAccessor _cachedReadAccessor;

    // These fields are used when the indexer is being used as a property write accessor.
    // If the name and target type match these cached values then the cachedWriteAccessor
    // is used to write the property. If they do not match, the correct accessor is
    // discovered and then cached for later use.
    private string _cachedWriteName;

    private Type _cachedWriteTargetType;

    private IPropertyAccessor _cachedWriteAccessor;

    private IndexedType _indexedType;

    public Indexer(int startPos, int endPos, SpelNode expr)
        : base(startPos, endPos, expr)
    {
    }

    public override ITypedValue GetValueInternal(ExpressionState state)
    {
        return GetValueRef(state).GetValue();
    }

    public override void SetValue(ExpressionState state, object newValue)
    {
        GetValueRef(state).SetValue(newValue);
    }

    public override bool IsWritable(ExpressionState state)
    {
        return true;
    }

    public override bool IsCompilable()
    {
        if (_indexedType == IndexedType.Array)
        {
            return exitTypeDescriptor != null;
        }

        if (_indexedType == IndexedType.List)
        {
            return children[0].IsCompilable();
        }

        if (_indexedType == IndexedType.Map)
        {
            return children[0] is PropertyOrFieldReference || children[0].IsCompilable();
        }

        if (_indexedType == IndexedType.Object)
        {
            // If the string name is changing the accessor is clearly going to change (so no compilation possible)
            return _cachedReadAccessor is ReflectivePropertyAccessor.OptimalPropertyAccessor && GetChild(0) is StringLiteral;
        }

        return false;
    }

    public override void GenerateCode(ILGenerator gen, CodeFlow cf)
    {
        TypeDescriptor descriptor = cf.LastDescriptor();

        if (descriptor == null)
        {
            CodeFlow.LoadTarget(gen);
        }

        if (_indexedType == IndexedType.Array)
        {
            Type arrayType = exitTypeDescriptor.Value.MakeArrayType();
            gen.Emit(OpCodes.Castclass, arrayType);
            SpelNode child = children[0];
            cf.EnterCompilationScope();
            child.GenerateCode(gen, cf);
            cf.ExitCompilationScope();
            gen.Emit(GetLdElemInsn(exitTypeDescriptor.Value));
        }
        else if (_indexedType == IndexedType.List)
        {
            gen.Emit(OpCodes.Castclass, typeof(IList));
            cf.EnterCompilationScope();
            children[0].GenerateCode(gen, cf);
            cf.ExitCompilationScope();
            gen.Emit(OpCodes.Callvirt, ListGetItemMethod);
        }
        else if (_indexedType == IndexedType.Map)
        {
            gen.Emit(OpCodes.Castclass, typeof(IDictionary));

            // Special case when the key is an unquoted string literal that will be parsed as
            // a property/field reference
            if (children[0] is PropertyOrFieldReference reference)
            {
                string mapKeyName = reference.Name;
                gen.Emit(OpCodes.Ldstr, mapKeyName);
            }
            else
            {
                cf.EnterCompilationScope();
                children[0].GenerateCode(gen, cf);
                cf.ExitCompilationScope();
            }

            gen.Emit(OpCodes.Callvirt, DictionaryGetItemMethod);
        }
        else if (_indexedType == IndexedType.Object)
        {
            if (_cachedReadAccessor is not ReflectivePropertyAccessor.OptimalPropertyAccessor accessor)
            {
                throw new InvalidOperationException("No cached read accessor");
            }

            var method = accessor.Member as MethodInfo;
            var field = accessor.Member as FieldInfo;
            bool isStatic = method != null ? method.IsStatic : field.IsStatic;

            Type targetType = accessor.Member.DeclaringType;

            if (!isStatic && (descriptor == null || targetType != descriptor.Value))
            {
                gen.Emit(OpCodes.Castclass, targetType);
            }

            if (method != null)
            {
                gen.Emit(isStatic ? OpCodes.Call : OpCodes.Callvirt, method);
            }
            else
            {
                gen.Emit(isStatic ? OpCodes.Ldsfld : OpCodes.Ldfld, field);
            }
        }

        cf.PushDescriptor(exitTypeDescriptor);
    }

    public override string ToStringAst()
    {
        var sj = new List<string>();

        for (int i = 0; i < ChildCount; i++)
        {
            sj.Add(GetChild(i).ToStringAst());
        }

        return $"[{string.Join(",", sj)}]";
    }

    protected internal override IValueRef GetValueRef(ExpressionState state)
    {
        ITypedValue context = state.GetActiveContextObject();
        object target = context.Value;
        Type targetDescriptor = context.TypeDescriptor;
        ITypedValue indexValue;
        object index;

        // This first part of the if clause prevents a 'double dereference' of the property (SPR-5847)
        if (target is IDictionary && children[0] is PropertyOrFieldReference reference1)
        {
            PropertyOrFieldReference reference = reference1;
            index = reference.Name;
            indexValue = new TypedValue(index);
        }
        else
        {
            // In case the map key is unqualified, we want it evaluated against the root object
            // so temporarily push that on whilst evaluating the key
            try
            {
                state.PushActiveContextObject(state.RootContextObject);
                indexValue = children[0].GetValueInternal(state);
                index = indexValue.Value;

                if (index == null)
                {
                    throw new InvalidOperationException("No index");
                }
            }
            finally
            {
                state.PopActiveContextObject();
            }
        }

        // Raise a proper exception in case of a null target
        if (target == null)
        {
            throw new SpelEvaluationException(StartPosition, SpelMessage.CannotIndexIntoNullValue);
        }

        // At this point, we need a TypeDescriptor for a non-null target object
        if (targetDescriptor == null)
        {
            throw new InvalidOperationException("No type descriptor");
        }

        // Indexing into a Map
        if (target is IDictionary dictionary)
        {
            object key = index;
            Type mapKeyType = ReflectionHelper.GetMapKeyTypeDescriptor(targetDescriptor);

            if (mapKeyType != null)
            {
                key = state.ConvertValue(key, mapKeyType);
            }

            _indexedType = IndexedType.Map;
            return new MapIndexingValueRef(this, state.TypeConverter, dictionary, key, targetDescriptor);
        }

        // If the object is something that looks indexable by an integer,
        // attempt to treat the index value as a number
        if (target is Array || target is IList || target is string)
        {
            int idx = (int)state.ConvertValue(index, typeof(int));

            if (target is Array)
            {
                _indexedType = IndexedType.Array;
                return new ArrayIndexingValueRef(this, state.TypeConverter, target, idx, targetDescriptor);
            }

            if (target is IList list)
            {
                _indexedType = IndexedType.List;

                return new CollectionIndexingValueRef(this, list, idx, targetDescriptor, state.TypeConverter, state.Configuration.AutoGrowCollections,
                    state.Configuration.MaximumAutoGrowSize);
            }

            _indexedType = IndexedType.String;
            return new StringIndexingLValue(this, (string)target, idx, targetDescriptor);
        }

        // Try and treat the index value as a property of the context object
        // Could call the conversion service to convert the value to a String
        Type valueType = indexValue.TypeDescriptor;

        if (valueType != null && typeof(string) == valueType)
        {
            _indexedType = IndexedType.Object;
            return new PropertyIndexingValueRef(this, target, (string)index, state.EvaluationContext, targetDescriptor);
        }

        throw new SpelEvaluationException(StartPosition, SpelMessage.IndexingNotSupportedForType, targetDescriptor);
    }

    private void CheckAccess(int arrayLength, int index)
    {
        if (index >= arrayLength)
        {
            throw new SpelEvaluationException(StartPosition, SpelMessage.ArrayIndexOutOfBounds, arrayLength, index);
        }
    }

    private T ConvertValue<T>(ITypeConverter converter, object value)
    {
        Type targetType = typeof(T);
        object result = converter.ConvertValue(value, value == null ? typeof(object) : value.GetType(), targetType);

        if (result is not T)
        {
            throw new InvalidOperationException($"Failed conversion result for index [{value}]");
        }

        return (T)result;
    }

    private object AccessArrayElement(object ctx, int idx)
    {
        Type arrayComponentType = ctx.GetType().GetElementType();

        if (arrayComponentType == typeof(bool))
        {
            bool[] array = (bool[])ctx;
            CheckAccess(array.Length, idx);
            exitTypeDescriptor = TypeDescriptor.Z;
            return array[idx];
        }

        if (arrayComponentType == typeof(byte))
        {
            byte[] array = (byte[])ctx;
            CheckAccess(array.Length, idx);

            exitTypeDescriptor = TypeDescriptor.B;
            return array[idx];
        }

        if (arrayComponentType == typeof(char))
        {
            char[] array = (char[])ctx;
            CheckAccess(array.Length, idx);

            exitTypeDescriptor = TypeDescriptor.C;
            return array[idx];
        }

        if (arrayComponentType == typeof(double))
        {
            double[] array = (double[])ctx;
            CheckAccess(array.Length, idx);

            exitTypeDescriptor = TypeDescriptor.D;
            return array[idx];
        }

        if (arrayComponentType == typeof(float))
        {
            float[] array = (float[])ctx;
            CheckAccess(array.Length, idx);

            exitTypeDescriptor = TypeDescriptor.F;
            return array[idx];
        }

        if (arrayComponentType == typeof(int))
        {
            int[] array = (int[])ctx;
            CheckAccess(array.Length, idx);

            exitTypeDescriptor = TypeDescriptor.I;
            return array[idx];
        }

        if (arrayComponentType == typeof(long))
        {
            long[] array = (long[])ctx;
            CheckAccess(array.Length, idx);

            exitTypeDescriptor = TypeDescriptor.J;
            return array[idx];
        }

        if (arrayComponentType == typeof(short))
        {
            short[] array = (short[])ctx;
            CheckAccess(array.Length, idx);

            exitTypeDescriptor = TypeDescriptor.S;
            return array[idx];
        }
        else
        {
            object[] array = (object[])ctx;
            CheckAccess(array.Length, idx);
            object retValue = array[idx];

            exitTypeDescriptor = CodeFlow.ToDescriptor(arrayComponentType);
            return retValue;
        }
    }

    private void SetArrayElement(ITypeConverter converter, object ctx, int idx, object newValue, Type arrayComponentType)
    {
        if (arrayComponentType == typeof(bool))
        {
            bool[] array = (bool[])ctx;
            CheckAccess(array.Length, idx);
            array[idx] = ConvertValue<bool>(converter, newValue);
        }
        else if (arrayComponentType == typeof(byte))
        {
            byte[] array = (byte[])ctx;
            CheckAccess(array.Length, idx);
            array[idx] = ConvertValue<byte>(converter, newValue);
        }
        else if (arrayComponentType == typeof(char))
        {
            char[] array = (char[])ctx;
            CheckAccess(array.Length, idx);
            array[idx] = ConvertValue<char>(converter, newValue);
        }
        else if (arrayComponentType == typeof(double))
        {
            double[] array = (double[])ctx;
            CheckAccess(array.Length, idx);
            array[idx] = ConvertValue<double>(converter, newValue);
        }
        else if (arrayComponentType == typeof(float))
        {
            float[] array = (float[])ctx;
            CheckAccess(array.Length, idx);
            array[idx] = ConvertValue<float>(converter, newValue);
        }
        else if (arrayComponentType == typeof(int))
        {
            int[] array = (int[])ctx;
            CheckAccess(array.Length, idx);
            array[idx] = ConvertValue<int>(converter, newValue);
        }
        else if (arrayComponentType == typeof(long))
        {
            long[] array = (long[])ctx;
            CheckAccess(array.Length, idx);
            array[idx] = ConvertValue<long>(converter, newValue);
        }
        else if (arrayComponentType == typeof(short))
        {
            short[] array = (short[])ctx;
            CheckAccess(array.Length, idx);
            array[idx] = ConvertValue<short>(converter, newValue);
        }
        else
        {
            object[] array = (object[])ctx;
            CheckAccess(array.Length, idx);
            array[idx] = ConvertValue<object>(converter, newValue);
        }
    }

    private enum IndexedType
    {
        Array,
        List,
        Map,
        String,
        Object
    }

    private sealed class ArrayIndexingValueRef : IValueRef
    {
        private readonly ITypeConverter _typeConverter;
        private readonly object _array;
        private readonly int _index;
        private readonly Type _typeDescriptor;
        private readonly Indexer _indexer;

        public bool IsWritable => true;

        public ArrayIndexingValueRef(Indexer indexer, ITypeConverter typeConverter, object array, int index, Type typeDescriptor)
        {
            _indexer = indexer;
            _typeConverter = typeConverter;
            _array = array;
            _index = index;
            _typeDescriptor = typeDescriptor;
        }

        public ITypedValue GetValue()
        {
            object arrayElement = _indexer.AccessArrayElement(_array, _index);
            Type type = arrayElement == null ? _typeDescriptor : arrayElement.GetType();
            return new TypedValue(arrayElement, type);
        }

        public void SetValue(object newValue)
        {
            Type elementType = _typeDescriptor.GetElementType();

            if (elementType == null)
            {
                throw new InvalidOperationException("No element type");
            }

            _indexer.SetArrayElement(_typeConverter, _array, _index, newValue, elementType);
        }
    }

    private sealed class MapIndexingValueRef : IValueRef
    {
        private readonly Indexer _indexer;

        private readonly ITypeConverter _typeConverter;

        private readonly IDictionary _map;

        private readonly object _key;

        private readonly Type _mapEntryDescriptor;

        public bool IsWritable => true;

        public MapIndexingValueRef(Indexer indexer, ITypeConverter typeConverter, IDictionary map, object key, Type mapEntryDescriptor)
        {
            _indexer = indexer;
            _typeConverter = typeConverter;
            _map = map;
            _key = key;
            _mapEntryDescriptor = mapEntryDescriptor;
        }

        public ITypedValue GetValue()
        {
            object value = _map[_key];
            _indexer.exitTypeDescriptor = CodeFlow.ToDescriptor(typeof(object));
            return new TypedValue(value, ReflectionHelper.GetMapValueTypeDescriptor(_mapEntryDescriptor, value));
        }

        public void SetValue(object newValue)
        {
            Type mapValType = ReflectionHelper.GetMapValueTypeDescriptor(_mapEntryDescriptor);

            if (mapValType != null)
            {
                newValue = _typeConverter.ConvertValue(newValue, newValue == null ? typeof(object) : newValue.GetType(), mapValType);
            }

            _map[_key] = newValue;
        }
    }

    private sealed class PropertyIndexingValueRef : IValueRef
    {
        private readonly object _targetObject;

        private readonly string _name;

        private readonly IEvaluationContext _evaluationContext;

        private readonly Type _targetObjectTypeDescriptor;
        private readonly Indexer _indexer;

        public bool IsWritable => true;

        public PropertyIndexingValueRef(Indexer indexer, object targetObject, string value, IEvaluationContext evaluationContext,
            Type targetObjectTypeDescriptor)
        {
            _indexer = indexer;
            _targetObject = targetObject;
            _name = value;
            _evaluationContext = evaluationContext;
            _targetObjectTypeDescriptor = targetObjectTypeDescriptor;
        }

        public ITypedValue GetValue()
        {
            Type targetObjectRuntimeClass = _indexer.GetObjectType(_targetObject);

            try
            {
                if (_indexer._cachedReadName != null && _indexer._cachedReadName == _name && _indexer._cachedReadTargetType != null &&
                    _indexer._cachedReadTargetType == targetObjectRuntimeClass)
                {
                    // It is OK to use the cached accessor
                    IPropertyAccessor accessor = _indexer._cachedReadAccessor;

                    if (accessor == null)
                    {
                        throw new InvalidOperationException("No cached read accessor");
                    }

                    return accessor.Read(_evaluationContext, _targetObject, _name);
                }

                List<IPropertyAccessor> accessorsToTry = AstUtils.GetPropertyAccessorsToTry(targetObjectRuntimeClass, _evaluationContext.PropertyAccessors);

                foreach (IPropertyAccessor acc in accessorsToTry)
                {
                    IPropertyAccessor accessor = acc;

                    if (accessor.CanRead(_evaluationContext, _targetObject, _name))
                    {
                        if (accessor is ReflectivePropertyAccessor accessor1)
                        {
                            accessor = accessor1.CreateOptimalAccessor(_evaluationContext, _targetObject, _name);
                        }

                        _indexer._cachedReadAccessor = accessor;
                        _indexer._cachedReadName = _name;
                        _indexer._cachedReadTargetType = targetObjectRuntimeClass;

                        if (accessor is ReflectivePropertyAccessor.OptimalPropertyAccessor optimalAccessor)
                        {
                            MemberInfo member = optimalAccessor.Member;
                            _indexer.exitTypeDescriptor = CodeFlow.ToDescriptor(member is MethodInfo info ? info.ReturnType : ((FieldInfo)member).FieldType);
                        }

                        return accessor.Read(_evaluationContext, _targetObject, _name);
                    }
                }
            }
            catch (AccessException ex)
            {
                throw new SpelEvaluationException(_indexer.StartPosition, ex, SpelMessage.IndexingNotSupportedForType, _targetObjectTypeDescriptor.ToString());
            }

            throw new SpelEvaluationException(_indexer.StartPosition, SpelMessage.IndexingNotSupportedForType, _targetObjectTypeDescriptor.ToString());
        }

        public void SetValue(object newValue)
        {
            Type contextObjectClass = _indexer.GetObjectType(_targetObject);

            try
            {
                if (_indexer._cachedWriteName != null && _indexer._cachedWriteName == _name && _indexer._cachedWriteTargetType != null &&
                    _indexer._cachedWriteTargetType == contextObjectClass)
                {
                    // It is OK to use the cached accessor
                    IPropertyAccessor accessor = _indexer._cachedWriteAccessor;

                    if (accessor == null)
                    {
                        throw new InvalidOperationException("No cached write accessor");
                    }

                    accessor.Write(_evaluationContext, _targetObject, _name, newValue);
                    return;
                }

                List<IPropertyAccessor> accessorsToTry = AstUtils.GetPropertyAccessorsToTry(contextObjectClass, _evaluationContext.PropertyAccessors);

                foreach (IPropertyAccessor acc in accessorsToTry)
                {
                    IPropertyAccessor accessor = acc;

                    if (accessor.CanWrite(_evaluationContext, _targetObject, _name))
                    {
                        _indexer._cachedWriteName = _name;
                        _indexer._cachedWriteTargetType = contextObjectClass;
                        _indexer._cachedWriteAccessor = accessor;
                        accessor.Write(_evaluationContext, _targetObject, _name, newValue);
                        return;
                    }
                }
            }
            catch (AccessException ex)
            {
                throw new SpelEvaluationException(_indexer.StartPosition, ex, SpelMessage.ExceptionDuringPropertyWrite, _name, ex.Message);
            }
        }
    }

    private sealed class CollectionIndexingValueRef : IValueRef
    {
        private readonly IList _collection;
        private readonly int _index;
        private readonly Type _collectionEntryDescriptor;
        private readonly ITypeConverter _typeConverter;
        private readonly bool _growCollection;
        private readonly int _maximumSize;
        private readonly Indexer _indexer;

        public bool IsWritable => true;

        public CollectionIndexingValueRef(Indexer indexer, IList collection, int index, Type collectionEntryDescriptor, ITypeConverter typeConverter,
            bool growCollection, int maximumSize)
        {
            _indexer = indexer;
            _collection = collection;
            _index = index;
            _collectionEntryDescriptor = collectionEntryDescriptor;
            _typeConverter = typeConverter;
            _growCollection = growCollection;
            _maximumSize = maximumSize;
        }

        public ITypedValue GetValue()
        {
            GrowCollectionIfNecessary();

            if (_collection != null)
            {
                object o = _collection[_index];
                _indexer.exitTypeDescriptor = CodeFlow.ToDescriptor(typeof(object));
                return new TypedValue(o, ReflectionHelper.GetElementTypeDescriptor(_collectionEntryDescriptor, o));
            }

            throw new InvalidOperationException($"Failed to find indexed element {_index}: {_collection}");
        }

        public void SetValue(object newValue)
        {
            GrowCollectionIfNecessary();

            if (_collection != null)
            {
                IList list = _collection;
                Type elemTypeDesc = ReflectionHelper.GetElementTypeDescriptor(_collectionEntryDescriptor);

                if (elemTypeDesc != null)
                {
                    newValue = _typeConverter.ConvertValue(newValue, newValue == null ? typeof(object) : newValue.GetType(), elemTypeDesc);
                }

                list[_index] = newValue;
            }
            else
            {
                throw new SpelEvaluationException(_indexer.StartPosition, SpelMessage.IndexingNotSupportedForType, _collectionEntryDescriptor.ToString());
            }
        }

        private void GrowCollectionIfNecessary()
        {
            if (_index >= _collection.Count)
            {
                if (!_growCollection)
                {
                    throw new SpelEvaluationException(_indexer.StartPosition, SpelMessage.CollectionIndexOutOfBounds, _collection.Count, _index);
                }

                if (_index >= _maximumSize)
                {
                    throw new SpelEvaluationException(_indexer.StartPosition, SpelMessage.UnableToGrowCollection);
                }

                Type elemTypeDesc = ReflectionHelper.GetElementTypeDescriptor(_collectionEntryDescriptor);

                if (elemTypeDesc == null)
                {
                    throw new SpelEvaluationException(_indexer.StartPosition, SpelMessage.UnableToGrowCollectionUnknownElementType);
                }

                try
                {
                    int newElements = _index - _collection.Count;

                    while (newElements >= 0)
                    {
                        // Insert a default value if the element type does not have a default constructor.
                        _collection.Add(GetDefaultValue(elemTypeDesc));
                        newElements--;
                    }
                }
                catch (Exception ex)
                {
                    throw new SpelEvaluationException(_indexer.StartPosition, ex, SpelMessage.UnableToGrowCollection);
                }
            }
        }

        private object GetDefaultValue(Type elemTypeDesc)
        {
            if (elemTypeDesc == typeof(string))
            {
                return string.Empty;
            }

            if (elemTypeDesc == typeof(int))
            {
                return 0;
            }

            if (elemTypeDesc == typeof(short))
            {
                return (short)0;
            }

            if (elemTypeDesc == typeof(long))
            {
                return 0L;
            }

            if (elemTypeDesc == typeof(uint))
            {
                return 0U;
            }

            if (elemTypeDesc == typeof(ushort))
            {
                return (ushort)0;
            }

            if (elemTypeDesc == typeof(ulong))
            {
                return 0UL;
            }

            if (elemTypeDesc == typeof(byte))
            {
                return (byte)0;
            }

            if (elemTypeDesc == typeof(sbyte))
            {
                return (sbyte)0;
            }

            if (elemTypeDesc == typeof(char))
            {
                return (char)0;
            }

            return Activator.CreateInstance(elemTypeDesc);
        }
    }

    private sealed class StringIndexingLValue : IValueRef
    {
        private readonly string _target;

        private readonly int _index;

        private readonly Type _typeDescriptor;
        private readonly Indexer _indexer;

        public bool IsWritable => true;

        public StringIndexingLValue(Indexer indexer, string target, int index, Type typeDescriptor)
        {
            _indexer = indexer;
            _target = target;
            _index = index;
            _typeDescriptor = typeDescriptor;
        }

        public ITypedValue GetValue()
        {
            if (_index >= _target.Length)
            {
                throw new SpelEvaluationException(_indexer.StartPosition, SpelMessage.StringIndexOutOfBounds, _target.Length, _index);
            }

            return new TypedValue(_target[_index].ToString(CultureInfo.InvariantCulture));
        }

        public void SetValue(object newValue)
        {
            throw new SpelEvaluationException(_indexer.StartPosition, SpelMessage.IndexingNotSupportedForType, _typeDescriptor.ToString());
        }
    }
}
