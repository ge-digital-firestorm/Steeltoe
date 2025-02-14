// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections;
using Steeltoe.Common.Expression.Internal;
using Steeltoe.Common.Expression.Internal.Spring;
using Steeltoe.Common.Expression.Internal.Spring.Standard;
using Steeltoe.Common.Expression.Internal.Spring.Support;
using Steeltoe.Common.Expression.Test.Spring.TestResources;
using Xunit;

namespace Steeltoe.Common.Expression.Test.Spring;

public sealed class ExpressionLanguageScenarioTests : AbstractExpressionTests
{
    [Fact]
    public void TestScenario_UsingStandardInfrastructure()
    {
        try
        {
            // Create a parser
            var parser = new SpelExpressionParser();

            // Parse an expression
            IExpression expr = parser.ParseRaw("new String('hello world')");

            // Evaluate it using a 'standard' context
            object value1 = expr.GetValue();

            // They are reusable
            object value2 = expr.GetValue();

            Assert.Equal(value1, value2);
            Assert.Equal("hello world", value2);
            Assert.IsType<string>(value1);
        }
        catch (SpelEvaluationException ex)
        {
            throw new SystemException(ex.Message, ex);
        }
        catch (ParseException ex)
        {
            throw new SystemException(ex.Message, ex);
        }
    }

    [Fact]
    public void TestScenario_DefiningVariablesThatWillBeAccessibleInExpressions()
    {
        // Create a parser
        var parser = new SpelExpressionParser();

        // Use the standard evaluation context
        var ctx = new StandardEvaluationContext();
        ctx.SetVariable("favouriteColour", "blue");

        var primes = new List<int>
        {
            2,
            3,
            5,
            7,
            11,
            13,
            17
        };

        ctx.SetVariable("primes", primes);

        IExpression expr = parser.ParseRaw("#favouriteColour");
        object value = expr.GetValue(ctx);
        Assert.Equal("blue", value);

        expr = parser.ParseRaw("#primes[1]");
        value = expr.GetValue(ctx);
        Assert.Equal(3, value);

        // all prime numbers > 10 from the list (using selection ?{...})
        expr = parser.ParseRaw("#primes.?[#this>10]");
        value = expr.GetValue(ctx);
        var asList = value as IList;
        Assert.Equal(3, asList.Count);
        Assert.Equal(11, asList[0]);
        Assert.Equal(13, asList[1]);
        Assert.Equal(17, asList[2]);
    }

    [Fact]
    public void TestScenario_UsingADifferentRootContextObject()
    {
        // Create a parser
        var parser = new SpelExpressionParser();

        // Use the standard evaluation context
        var ctx = new StandardEvaluationContext();

        var tc = new TestClass
        {
            Property = 42,
            Str = "wibble"
        };

        ctx.SetRootObject(tc);

        // read it, set it, read it again
        IExpression expr = parser.ParseRaw("Str");
        object value = expr.GetValue(ctx);
        Assert.Equal("wibble", value);
        expr = parser.ParseRaw("Str");
        expr.SetValue(ctx, "wobble");
        expr = parser.ParseRaw("Str");
        value = expr.GetValue(ctx);
        Assert.Equal("wobble", value);

        // or using assignment within the expression
        expr = parser.ParseRaw("Str='wabble'");
        value = expr.GetValue(ctx);
        Assert.Equal("wabble", value);
        expr = parser.ParseRaw("Str");
        value = expr.GetValue(ctx);
        Assert.Equal("wabble", value);

        // private property will be accessed through getter()
        expr = parser.ParseRaw("Property");
        value = expr.GetValue(ctx);
        Assert.Equal(42, value);

        // ... and set through setter
        expr = parser.ParseRaw("Property=4");
        value = expr.GetValue(ctx);
        Assert.Equal(4, value);
        expr = parser.ParseRaw("Property");
        value = expr.GetValue(ctx);
        Assert.Equal(4, value);
    }

    [Fact]
    public void TestScenario_RegisteringJavaMethodsAsFunctionsAndCallingThem()
    {
        try
        {
            // Create a parser
            var parser = new SpelExpressionParser();

            // Use the standard evaluation context
            var ctx = new StandardEvaluationContext();

            ctx.RegisterFunction("Repeat", typeof(ExpressionLanguageScenarioTests).GetMethod(nameof(Repeat), new[]
            {
                typeof(string)
            }));

            IExpression expr = parser.ParseRaw("#Repeat('hello')");
            object value = expr.GetValue(ctx);
            Assert.Equal("hellohello", value);
        }
        catch (EvaluationException ex)
        {
            throw new SystemException(ex.Message, ex);
        }
        catch (ParseException ex)
        {
            throw new SystemException(ex.Message, ex);
        }
    }

    [Fact]
    public void TestScenario_AddingYourOwnPropertyResolvers_1()
    {
        // Create a parser
        var parser = new SpelExpressionParser();

        // Use the standard evaluation context
        var ctx = new StandardEvaluationContext();

        ctx.AddPropertyAccessor(new FruitColorAccessor());
        IExpression expr = parser.ParseRaw("Orange");
        object value = expr.GetValue(ctx);
        Assert.Equal(Color.Orange, value);
        var ex = Assert.Throws<SpelEvaluationException>(() => expr.SetValue(ctx, Color.Blue));
        Assert.Equal(SpelMessage.PropertyOrFieldNotWritableOnNull, ex.MessageCode);
    }

    [Fact]
    public void TestScenario_AddingYourOwnPropertyResolvers_2()
    {
        // Create a parser
        var parser = new SpelExpressionParser();

        // Use the standard evaluation context
        var ctx = new StandardEvaluationContext();

        ctx.AddPropertyAccessor(new VegetableColorAccessor());
        IExpression expr = parser.ParseRaw("Pea");
        object value = expr.GetValue(ctx);
        Assert.Equal(Color.Green, value);
        var ex = Assert.Throws<SpelEvaluationException>(() => expr.SetValue(ctx, Color.Blue));
        Assert.Equal(SpelMessage.PropertyOrFieldNotWritableOnNull, ex.MessageCode);
    }

    public static string Repeat(string s)
    {
        return s + s;
    }

    public sealed class FruitColorAccessor : IPropertyAccessor
    {
        private static readonly Dictionary<string, Color> PropertyMap = new();

        static FruitColorAccessor()
        {
            PropertyMap.Add("Banana", Color.Yellow);
            PropertyMap.Add("Apple", Color.Red);
            PropertyMap.Add("Orange", Color.Orange);
        }

        public IList<Type> GetSpecificTargetClasses()
        {
            return null;
        }

        public bool CanRead(IEvaluationContext context, object target, string name)
        {
            return PropertyMap.ContainsKey(name);
        }

        public ITypedValue Read(IEvaluationContext context, object target, string name)
        {
            PropertyMap.TryGetValue(name, out Color value);
            return new TypedValue(value);
        }

        public bool CanWrite(IEvaluationContext context, object target, string name)
        {
            return false;
        }

        public void Write(IEvaluationContext context, object target, string name, object newValue)
        {
        }
    }

    public sealed class VegetableColorAccessor : IPropertyAccessor
    {
        private static readonly Dictionary<string, Color> PropertyMap = new();

        static VegetableColorAccessor()
        {
            PropertyMap.Add("Pea", Color.Green);
            PropertyMap.Add("Carrot", Color.Orange);
        }

        public IList<Type> GetSpecificTargetClasses()
        {
            return null;
        }

        public bool CanRead(IEvaluationContext context, object target, string name)
        {
            return PropertyMap.ContainsKey(name);
        }

        public ITypedValue Read(IEvaluationContext context, object target, string name)
        {
            PropertyMap.TryGetValue(name, out Color value);
            return new TypedValue(value);
        }

        public bool CanWrite(IEvaluationContext context, object target, string name)
        {
            return false;
        }

        public void Write(IEvaluationContext context, object target, string name, object newValue)
        {
        }
    }

    public sealed class TestClass
    {
        public string Str { get; set; }

        public int Property { get; set; }
    }
}
