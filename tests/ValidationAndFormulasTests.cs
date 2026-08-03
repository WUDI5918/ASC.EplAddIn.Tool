using System;
using System.Collections.Generic;
using AutoSchematicConfigurator.EplAddIn.Tool;
using Xunit;

namespace AutoSchematicConfigurator.EplAddIn.Tool.Tests
{
    public class ValidationAndFormulasTests
    {
        // ==========================================
        // TIER 1: Standard Validation & Expression Evaluation (6 tests)
        // ==========================================

        [Fact]
        public void ValidateVariables_WithMandatoryNullValue_ShouldThrowArgumentException()
        {
            var variables = new List<ConfigurationVariable>
            {
                new ConfigurationVariable { Name = "Power", IsMandatory = true, VisibilityFormula = "" }
            };
            var parameters = new Dictionary<string, object>
            {
                { "Power", null }
            };

            // This test is expected to fail against the stub because it does not throw.
            Assert.Throws<ArgumentException>(() =>
                ConfiguratorService.ValidateVariables("TestContext", variables, parameters));
        }

        [Fact]
        public void ValidateVariables_WithMandatoryEmptyValue_ShouldThrowArgumentException()
        {
            var variables = new List<ConfigurationVariable>
            {
                new ConfigurationVariable { Name = "Power", IsMandatory = true, VisibilityFormula = "" }
            };
            var parameters = new Dictionary<string, object>
            {
                { "Power", "   " }
            };

            // This test is expected to fail against the stub because it does not throw.
            Assert.Throws<ArgumentException>(() =>
                ConfiguratorService.ValidateVariables("TestContext", variables, parameters));
        }

        [Fact]
        public void ValidateVariables_WithMandatoryPopulatedValue_ShouldPass()
        {
            var variables = new List<ConfigurationVariable>
            {
                new ConfigurationVariable { Name = "Power", IsMandatory = true, VisibilityFormula = "" }
            };
            var parameters = new Dictionary<string, object>
            {
                { "Power", "15kW" }
            };

            // This should execute without throwing any exception.
            ConfiguratorService.ValidateVariables("TestContext", variables, parameters);
        }

        [Fact]
        public void EvaluateExpression_WithSimpleMath_ShouldEvaluate()
        {
            var parameters = new Dictionary<string, object>();
            var result = ConfiguratorService.EvaluateExpression("1 + 2 * 3", parameters);
            Assert.Equal(7, Convert.ToInt32(result));
        }

        [Fact]
        public void EvaluateExpression_WithLogicalOperators_ShouldEvaluate()
        {
            var parameters = new Dictionary<string, object>
            {
                { "A", true },
                { "B", false }
            };
            var result1 = ConfiguratorService.EvaluateExpression("A && B", parameters);
            var result2 = ConfiguratorService.EvaluateExpression("A || B", parameters);
            Assert.False((bool)result1);
            Assert.True((bool)result2);
        }

        [Fact]
        public void EvaluateExpression_WithStringConcat_ShouldEvaluate()
        {
            var parameters = new Dictionary<string, object>
            {
                { "Cabinet", "MCC" }
            };
            var result = ConfiguratorService.EvaluateExpression("Concat('=', Cabinet, '+AP1')", parameters);
            Assert.Equal("=MCC+AP1", result);
        }

        // ==========================================
        // TIER 2: Edge Cases & Boundaries (6 tests)
        // ==========================================

        [Fact]
        public void ValidateVariables_WithVisibilityFormulaFalse_ShouldPassEvenIfNull()
        {
            var variables = new List<ConfigurationVariable>
            {
                new ConfigurationVariable { Name = "Power", IsMandatory = true, VisibilityFormula = "false" }
            };
            var parameters = new Dictionary<string, object>
            {
                { "Power", null }
            };

            // Since visibility is false, it should pass without throwing even though value is null.
            ConfiguratorService.ValidateVariables("TestContext", variables, parameters);
        }

        [Fact]
        public void ValidateVariables_WithVisibilityFormulaTrue_ShouldThrowIfNull()
        {
            var variables = new List<ConfigurationVariable>
            {
                new ConfigurationVariable { Name = "Power", IsMandatory = true, VisibilityFormula = "true" }
            };
            var parameters = new Dictionary<string, object>
            {
                { "Power", null }
            };

            // This test is expected to fail against the stub because it does not throw.
            Assert.Throws<ArgumentException>(() =>
                ConfiguratorService.ValidateVariables("TestContext", variables, parameters));
        }

        [Fact]
        public void EvaluateExpression_WithIfFormula_ShouldEvaluate()
        {
            var parameters = new Dictionary<string, object>
            {
                { "Power", 15.0 }
            };
            var result = ConfiguratorService.EvaluateExpression("If(Power > 11.0, '32A', '16A')", parameters);
            Assert.Equal("32A", result);
        }

        [Fact]
        public void EvaluateExpression_WithStartsWithEndsWith_ShouldEvaluate()
        {
            var parameters = new Dictionary<string, object>
            {
                { "Name", "Typical_Breaker" }
            };
            var resultStart = ConfiguratorService.EvaluateExpression("StartsWith(Name, 'Typical')", parameters);
            var resultEnd = ConfiguratorService.EvaluateExpression("EndsWith(Name, 'Breaker')", parameters);
            Assert.True((bool)resultStart);
            Assert.True((bool)resultEnd);
        }

        [Fact]
        public void EvaluateExpression_WithContains_ShouldEvaluate()
        {
            var parameters = new Dictionary<string, object>
            {
                { "Name", "Typical_Breaker_Power" }
            };
            var result = ConfiguratorService.EvaluateExpression("Contains(Name, 'Breaker')", parameters);
            Assert.True((bool)result);
        }

        [Fact]
        public void EvaluateExpression_WithNullEmptyCheck_ShouldEvaluate()
        {
            var parameters = new Dictionary<string, object>
            {
                { "Var1", "" },
                { "Var2", "populated" }
            };
            var checkEmpty = ConfiguratorService.EvaluateExpression("IsNullEmpty(Var1)", parameters);
            var checkPopulated = ConfiguratorService.EvaluateExpression("IsNullEmpty(Var2)", parameters);
            Assert.True((bool)checkEmpty);
            Assert.False((bool)checkPopulated);
        }

        [Fact]
        public void EvaluateExpression_WithContainsCaseInsensitive_ShouldEvaluate()
        {
            var parameters = new Dictionary<string, object>
            {
                { "Name", "Typical_Breaker_Power" }
            };
            var result = ConfiguratorService.EvaluateExpression("Contains(Name, 'breaker')", parameters);
            Assert.True((bool)result);
        }

        [Fact]
        public void EvaluateExpression_WithInvalidExpressions_ShouldThrowException()
        {
            var parameters = new Dictionary<string, object>();

            // Jint (JS engine) fills missing params with defaults (undefined/null/0)
            // instead of throwing. So we test actual error scenarios:

            // Calling a non-existent function should throw
            Assert.Throws<Exception>(() => ConfiguratorService.EvaluateExpression("NonExistentFunction(1)", parameters));

            // Accessing property of null should throw
            Assert.Throws<Exception>(() => ConfiguratorService.EvaluateExpression("nullProp.length", parameters));

            // Syntax error should throw
            Assert.Throws<Exception>(() => ConfiguratorService.EvaluateExpression("if (", parameters));

            // Calling undefined variable as function should throw
            Assert.Throws<Exception>(() => ConfiguratorService.EvaluateExpression("undefinedFunc()", parameters));
        }

        [Fact]
        public void EvaluateExpression_WithConcatTwoArgs_ShouldEvaluate()
        {
            var parameters = new Dictionary<string, object>();
            var result = ConfiguratorService.EvaluateExpression("Concat('Hello', ' World')", parameters);
            Assert.Equal("Hello World", result);
        }

        [Fact]
        public void EvaluateExpression_WithConcatFourArgs_ShouldEvaluate()
        {
            var parameters = new Dictionary<string, object> { { "A", "1" }, { "B", "2" } };
            var result = ConfiguratorService.EvaluateExpression("Concat('=', A, '+', B)", parameters);
            Assert.Equal("=1+2", result);
        }

        [Fact]
        public void EvaluateExpression_WithSystemVariables_ShouldEvaluate()
        {
            var parameters = new Dictionary<string, object>();
            var projectName = ConfiguratorService.EvaluateExpression("ProjectName", parameters);
            Assert.NotNull(projectName);

            var date = ConfiguratorService.EvaluateExpression("Date", parameters);
            Assert.NotNull(date);

            var user = ConfiguratorService.EvaluateExpression("User", parameters);
            Assert.NotNull(user);
        }

        [Fact]
        public void EvaluateExpression_WithMathFunctions_ShouldEvaluate()
        {
            var parameters = new Dictionary<string, object>();
            var resultAbs = ConfiguratorService.EvaluateExpression("Abs(-5)", parameters);
            Assert.Equal(5.0, Convert.ToDouble(resultAbs));

            var resultMax = ConfiguratorService.EvaluateExpression("Max(3, 7)", parameters);
            Assert.Equal(7.0, Convert.ToDouble(resultMax));

            var resultMin = ConfiguratorService.EvaluateExpression("Min(3, 7)", parameters);
            Assert.Equal(3.0, Convert.ToDouble(resultMin));

            var resultSqrt = ConfiguratorService.EvaluateExpression("Sqrt(16)", parameters);
            Assert.Equal(4.0, Convert.ToDouble(resultSqrt));

            var resultPow = ConfiguratorService.EvaluateExpression("Pow(2, 3)", parameters);
            Assert.Equal(8.0, Convert.ToDouble(resultPow));

            var resultRound = ConfiguratorService.EvaluateExpression("Round(3.7)", parameters);
            Assert.Equal(4.0, Convert.ToDouble(resultRound));
        }

        [Fact]
        public void EvaluateExpression_WithNCalcAndOr_ShouldEvaluate()
        {
            var parameters = new Dictionary<string, object>
            {
                { "A", true },
                { "B", false }
            };
            // 'and' should be converted to '&&'
            var resultAnd = ConfiguratorService.EvaluateExpression("A and B", parameters);
            Assert.False((bool)resultAnd);

            // 'or' should be converted to '||'
            var resultOr = ConfiguratorService.EvaluateExpression("A or B", parameters);
            Assert.True((bool)resultOr);
        }

        [Fact]
        public void EvaluateActiveFormula_WithInvalidExpression_ShouldReturnFalse()
        {
            var parameters = new Dictionary<string, object>();
            // Invalid expression should return false instead of throwing
            var result = ConfiguratorService.EvaluateActiveFormula("invalid syntax (((", parameters);
            Assert.False(result);
        }

        [Fact]
        public void EvaluateExpression_WithAndOrNotInsideStringLiterals_ShouldNotRewriteThem()
        {
            var parameters = new Dictionary<string, object>();

            Assert.Equal("Start and Stop", ConfiguratorService.EvaluateExpression("Concat('Start and Stop')", parameters)?.ToString());
            Assert.Equal("black or white", ConfiguratorService.EvaluateExpression("Concat('black or white')", parameters)?.ToString());
            Assert.Equal("do not", ConfiguratorService.EvaluateExpression("Concat('do not')", parameters)?.ToString());
            Assert.Equal("A and B", ConfiguratorService.EvaluateExpression("Concat(\"A and B\")", parameters)?.ToString());
        }

        [Fact]
        public void EvaluateExpression_WithNCalcWordsOutsideStrings_ShouldStillConvert()
        {
            var parameters = new Dictionary<string, object>
            {
                { "A", true },
                { "B", false }
            };

            Assert.False((bool)ConfiguratorService.EvaluateExpression("A and B", parameters));
            Assert.True((bool)ConfiguratorService.EvaluateExpression("A or B", parameters));
            Assert.True((bool)ConfiguratorService.EvaluateExpression("not A == false", parameters));
            Assert.Equal("android", ConfiguratorService.EvaluateExpression("Concat('android')", parameters)?.ToString());
        }
    }
}
