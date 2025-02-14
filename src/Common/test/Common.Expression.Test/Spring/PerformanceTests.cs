// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information.

using Steeltoe.Common.Expression.Internal;
using Steeltoe.Common.Expression.Internal.Spring.Standard;
using Xunit;
using Xunit.Abstractions;

namespace Steeltoe.Common.Expression.Test.Spring;

public sealed class PerformanceTests
{
    private const int Iterations = 10000;
    private static readonly bool IsDebug = bool.Parse(bool.FalseString);
    private static readonly IExpressionParser Parser = new SpelExpressionParser();
    private static readonly IEvaluationContext EContext = TestScenarioCreator.GetTestEvaluationContext();
    private readonly ITestOutputHelper _output;

    public PerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TestPerformanceOfPropertyAccess()
    {
        long startTime = 0;
        long endTime = 0;
        IExpression expr;

        // warmup
        for (int i = 0; i < Iterations; i++)
        {
            expr = Parser.ParseExpression("PlaceOfBirth.City");
            Assert.NotNull(expr);
            expr.GetValue(EContext);
        }

        startTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        for (int i = 0; i < Iterations; i++)
        {
            expr = Parser.ParseExpression("PlaceOfBirth.City");
            Assert.NotNull(expr);
            expr.GetValue(EContext);
        }

        endTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        long freshParseTime = endTime - startTime;

        if (IsDebug)
        {
            _output.WriteLine("PropertyAccess: Time for parsing and evaluation x 10000: " + freshParseTime + "ms");
        }

        expr = Parser.ParseExpression("PlaceOfBirth.City");
        Assert.NotNull(expr);
        startTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        for (int i = 0; i < Iterations; i++)
        {
            expr.GetValue(EContext);
        }

        endTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        long reuseTime = endTime - startTime;

        if (IsDebug)
        {
            _output.WriteLine("PropertyAccess: Time for just evaluation x 10000: " + reuseTime + "ms");
        }

        if (reuseTime > freshParseTime)
        {
            _output.WriteLine("Fresh parse every time, ITERATIONS iterations = " + freshParseTime + "ms");
            _output.WriteLine("Reuse SpelExpression, ITERATIONS iterations = " + reuseTime + "ms");
            throw new Exception("Should have been quicker to reuse!");
        }
    }

    [Fact]
    public void TestPerformanceOfMethodAccess()
    {
        long startTime = 0;
        long endTime = 0;
        IExpression expr;

        // warmup
        for (int i = 0; i < Iterations; i++)
        {
            expr = Parser.ParseExpression("get_PlaceOfBirth().get_City()");
            Assert.NotNull(expr);
            expr.GetValue(EContext);
        }

        startTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        for (int i = 0; i < Iterations; i++)
        {
            expr = Parser.ParseExpression("get_PlaceOfBirth().get_City()");
            Assert.NotNull(expr);
            expr.GetValue(EContext);
        }

        endTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        long freshParseTime = endTime - startTime;

        if (IsDebug)
        {
            _output.WriteLine("MethodExpression: Time for parsing and evaluation x 10000: " + freshParseTime + "ms");
        }

        expr = Parser.ParseExpression("get_PlaceOfBirth().get_City()");
        Assert.NotNull(expr);
        startTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        for (int i = 0; i < Iterations; i++)
        {
            expr.GetValue(EContext);
        }

        endTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        long reuseTime = endTime - startTime;

        if (IsDebug)
        {
            _output.WriteLine("MethodExpression: Time for just evaluation x 10000: " + reuseTime + "ms");
        }

        if (reuseTime > freshParseTime)
        {
            _output.WriteLine("Fresh parse every time, ITERATIONS iterations = " + freshParseTime + "ms");
            _output.WriteLine("Reuse SpelExpression, ITERATIONS iterations = " + reuseTime + "ms");
            throw new Exception("Should have been quicker to reuse!");
        }
    }
}
