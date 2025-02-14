// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using Steeltoe.Common.Expression.Internal.Spring;
using Steeltoe.Common.Expression.Internal.Spring.Ast;
using Steeltoe.Common.Expression.Internal.Spring.Standard;
using Xunit;

namespace Steeltoe.Common.Expression.Test.Spring;

public sealed class ListTests : AbstractExpressionTests
{
    private readonly Type _unmodifiableClass = typeof(ReadOnlyCollection<object>);

    [Fact]
    public void TestInlineListCreation01()
    {
        Evaluate("{1, 2, 3, 4, 5}", "[1,2,3,4,5]", _unmodifiableClass);
    }

    [Fact]
    public void TestInlineListCreation02()
    {
        Evaluate("{'abc', 'xyz'}", "[abc,xyz]", _unmodifiableClass);
    }

    [Fact]
    public void TestInlineListCreation03()
    {
        Evaluate("{}", "[]", _unmodifiableClass);
    }

    [Fact]
    public void TestInlineListCreation04()
    {
        Evaluate("{'abc'=='xyz'}", "[False]", typeof(List<object>));
    }

    [Fact]
    public void TestInlineListAndNesting()
    {
        Evaluate("{{1,2,3},{4,5,6}}", "[[1,2,3],[4,5,6]]", _unmodifiableClass);
        Evaluate("{{1,'2',3},{4,{'a','b'},5,6}}", "[[1,2,3],[4,[a,b],5,6]]", _unmodifiableClass);
    }

    [Fact]
    public void TestInlineListError()
    {
        ParseAndCheckError("{'abc'", SpelMessage.Ood);
    }

    [Fact]
    public void TestRelOperatorsIs02()
    {
        Evaluate("{1, 2, 3, 4, 5} instanceof T(System.Collections.IList)", "True", typeof(bool));
    }

    [Fact]
    public void TestInlineListCreation05()
    {
        Evaluate("3 between {1,5}", "True", typeof(bool));
    }

    [Fact]
    public void TestInlineListCreation06()
    {
        Evaluate("8 between {1,5}", "False", typeof(bool));
    }

    [Fact]
    public void TestInlineListAndProjectionSelection()
    {
        Evaluate("{1,2,3,4,5,6}.![#this>3]", "[False,False,False,True,True,True]", typeof(List<object>));
        Evaluate("{1,2,3,4,5,6}.?[#this>3]", "[4,5,6]", typeof(List<object>));
        Evaluate("{1,2,3,4,5,6,7,8,9,10}.?[#IsEven(#this) == 'y']", "[2,4,6,8,10]", typeof(List<object>));
    }

    [Fact]
    public void TestRelOperatorsBetween01()
    {
        Evaluate("32 between {32, 42}", "True", typeof(bool));
    }

    [Fact]
    public void TestRelOperatorsBetween02()
    {
        Evaluate("'efg' between {'abc', 'xyz'}", "True", typeof(bool));
    }

    [Fact]
    public void TestRelOperatorsBetween03()
    {
        Evaluate("42 between {32, 42}", "True", typeof(bool));
    }

    [Fact]
    public void TestRelOperatorsBetween04()
    {
        Evaluate("new Decimal(1) between {new Decimal(1),new Decimal(5)}", "True", typeof(bool));
        Evaluate("new Decimal(3) between {new Decimal(1),new Decimal(5)}", "True", typeof(bool));
        Evaluate("new Decimal(5) between {new Decimal(1),new Decimal(5)}", "True", typeof(bool));
        Evaluate("new Decimal(8) between {new Decimal(1),new Decimal(5)}", "False", typeof(bool));
    }

    [Fact]
    public void TestRelOperatorsBetweenErrors02()
    {
        EvaluateAndCheckError("'abc' between {5,7}", SpelMessage.NotComparable, 6);
    }

    [Fact]
    public void TestConstantRepresentation1()
    {
        CheckConstantList("{1,2,3,4,5}", true);
        CheckConstantList("{'abc'}", true);
        CheckConstantList("{}", true);
        CheckConstantList("{#a,2,3}", false);
        CheckConstantList("{1,2,Integer.valueOf(4)}", false);
        CheckConstantList("{1,2,{#a}}", false);
    }

    [Fact]
    public void TestInlineListWriting()
    {
        // list should be unmodifiable
        Assert.Throws<NotSupportedException>(() => Evaluate("{1, 2, 3, 4, 5}[0]=6", "[1, 2, 3, 4, 5]", _unmodifiableClass));
    }

    private void CheckConstantList(string expressionText, bool expectedToBeConstant)
    {
        var parser = new SpelExpressionParser();
        var expression = (SpelExpression)parser.ParseExpression(expressionText);
        ISpelNode node = expression.Ast;
        bool condition = node is InlineList;
        Assert.True(condition);
        var inlineList = (InlineList)node;

        if (expectedToBeConstant)
        {
            Assert.True(inlineList.IsConstant);
        }
        else
        {
            Assert.False(inlineList.IsConstant);
        }
    }
}
