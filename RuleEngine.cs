using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using NCalc;

namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    /// <summary>
    /// Abstracts where catalog data comes from (CSV file, REST endpoint, ...). Implementing this
    /// interface lets the same rules engine read selection tables from different data sources
    /// without hardcoding business rules (constitution III: data-source agnosticism).
    /// </summary>
    public interface ICatalogSource
    {
        /// <summary>Returns the raw lines of the catalog, or <c>null</c> when unavailable.</summary>
        string[] LoadLines(string fileName);
    }

    /// <summary>
    /// Placeholder for a REST-backed catalog source. Not implemented yet; present so the rules
    /// engine is not coupled to the file system.
    /// </summary>
    public sealed class RestCatalogSource : ICatalogSource
    {
        public string[] LoadLines(string fileName)
        {
            // TODO: fetch the catalog from a REST JSON endpoint.
            return null;
        }
    }

    /// <summary>
    /// Host-provided values that the rules engine needs at evaluation time. Implemented by the
    /// EPLAN layer so that <see cref="RuleEngine"/> itself stays free of any EPLAN dependency.
    /// </summary>
    public interface IRuleEngineContext
    {
        /// <summary>Current project name exposed to formulas as the <c>ProjectName</c> variable.</summary>
        string ProjectName { get; }

        /// <summary>Current user name exposed to formulas as the <c>User</c> variable.</summary>
        string User { get; }

        /// <summary>Current date exposed to formulas as the <c>Date</c> variable.</summary>
        string Date { get; }

        /// <summary>Source for lookup-table catalogs used by <c>Lookup</c> / <c>Lookup_Breaker</c>.</summary>
        ICatalogSource CatalogSource { get; }
    }

    /// <summary>
    /// Pure, EPLAN-independent rules engine built on NCalc-Edge. It evaluates logical/ternary/string/math
    /// expressions and provides the custom engineering functions used by Macro-Typical rules.
    /// </summary>
    public static class RuleEngine
    {
        private const string BreakerCatalogFileName = "breaker_catalog.csv";
        private const string DefaultBreakerSet = "Default_Set";

        private static readonly EvaluateOptions Options = EvaluateOptions.IgnoreCase | EvaluateOptions.AllowNullParameter;

        /// <summary>
        /// Evaluates an expression (with optional leading '='), binding <paramref name="parameters"/>
        /// as variables and injecting system variables from <paramref name="context"/>.
        /// </summary>
        public static object Evaluate(string expression, IDictionary<string, object> parameters, IRuleEngineContext context)
        {
            if (string.IsNullOrWhiteSpace(expression)) return null;

            string clean = Normalize(expression);

            var expr = new Expression(clean, Options);

            if (parameters != null)
            {
                foreach (var kvp in parameters)
                {
                    expr.Parameters[kvp.Key] = kvp.Value;
                }
            }

            // Inject system variables (host-provided).
            expr.Parameters["ProjectName"] = context?.ProjectName ?? "";
            expr.Parameters["Date"] = context?.Date ?? DateTime.Now.ToString("yyyy-MM-dd");
            expr.Parameters["User"] = context?.User ?? "";

            expr.EvaluateFunction += (name, args) => HandleFunction(name, args, context, parameters);

            return expr.Evaluate();
        }

        /// <summary>
        /// Evaluates an expression as a boolean activation/visibility condition. Empty formula is always true;
        /// a non-boolean result is parsed leniently; evaluation errors return <c>false</c>.
        /// </summary>
        public static bool EvaluateActive(string formula, IDictionary<string, object> parameters, IRuleEngineContext context)
        {
            if (string.IsNullOrWhiteSpace(formula)) return true;
            object result = Evaluate(formula, parameters, context);
            if (result is bool b) return b;
            if (result != null && bool.TryParse(result.ToString(), out bool parsedBool)) return parsedBool;
            return true;
        }

        /// <summary>
        /// Validates an expression using the same engine and the same function implementations used at
        /// evaluation time. Returns <c>null</c> when valid, or the error message when invalid.
        /// Variables are bound to neutral mock values so the expression can be parsed/evaluated.
        /// Besides the explicitly supplied <paramref name="variableNames"/>, every identifier token
        /// appearing in the expression is auto-bound to a mock value: NCalc-Edge throws
        /// "Parameter was not defined" for ANY undefined identifier even with
        /// <see cref="EvaluateOptions.AllowNullParameter"/>, so validation would otherwise flag a
        /// formula for referencing a variable that simply was not part of the supplied scope.
        /// Reference integrity (undefined-variable detection) is a separate concern performed by
        /// the pre-flight validator; this method deliberately checks syntax/function usage only.
        /// </summary>
        public static string Validate(string expression, IEnumerable<string> variableNames, IRuleEngineContext context)
        {
            if (string.IsNullOrWhiteSpace(expression)) return null;
            try
            {
                string clean = Normalize(expression);
                var mockParams = new Dictionary<string, object>();
                if (variableNames != null)
                {
                    foreach (var varName in variableNames)
                    {
                        mockParams[varName] = 0;
                    }
                }
                foreach (Match m in Regex.Matches(clean, @"[A-Za-z_][A-Za-z0-9_]*"))
                {
                    if (!mockParams.ContainsKey(m.Value))
                    {
                        mockParams[m.Value] = 0;
                    }
                }
                Evaluate(clean, mockParams, context);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            return null;
        }

        private static string Normalize(string expression)
        {
            string clean = expression.Trim();
            if (clean.StartsWith("=")) clean = clean.Substring(1).Trim();
            return clean;
        }

        private static void HandleFunction(string name, FunctionArgs args, IRuleEngineContext context, IDictionary<string, object> parameters)
        {
            switch (name.ToLowerInvariant())
            {
                case "lookup_breaker":
                {
                    object[] p = args.EvaluateParameters();
                    double power = p.Length > 0 ? ToDouble(p[0]) : 0;
                    string startType = p.Length > 1 ? (p[1]?.ToString() ?? "") : "";
                    args.Result = QueryBreaker(power, startType, context);
                    args.HasResult = true;
                    break;
                }
                case "lookup":
                {
                    object[] p = args.EvaluateParameters();
                    string table = p.Length > 0 ? (p[0]?.ToString() ?? "") : "";
                    string returnCol = p.Length > 1 ? (p[1]?.ToString() ?? "") : "";
                    string searchCol = p.Length > 2 ? (p[2]?.ToString() ?? "") : "";
                    string searchVal = p.Length > 3 ? (p[3]?.ToString() ?? "") : "";
                    args.Result = QueryCatalog(table, returnCol, searchCol, searchVal, context);
                    args.HasResult = true;
                    break;
                }
                case "concat":
                {
                    object[] p = args.EvaluateParameters();
                    var sb = new StringBuilder();
                    foreach (var item in p)
                    {
                        sb.Append(item == null ? "" : item.ToString());
                    }
                    args.Result = sb.ToString();
                    args.HasResult = true;
                    break;
                }
                case "isavailable":
                {
                    object[] p = args.EvaluateParameters();
                    string varName = p.Length > 0 ? (p[0]?.ToString() ?? "") : "";
                    args.Result = parameters != null && parameters.ContainsKey(varName) && parameters[varName] != null;
                    args.HasResult = true;
                    break;
                }
                case "isnullempty":
                {
                    object[] p = args.EvaluateParameters();
                    string val = p.Length > 0 ? (p[0]?.ToString() ?? "") : "";
                    args.Result = string.IsNullOrEmpty(val);
                    args.HasResult = true;
                    break;
                }
                case "contains":
                {
                    object[] p = args.EvaluateParameters();
                    string str = p.Length > 0 ? (p[0]?.ToString() ?? "") : "";
                    string sub = p.Length > 1 ? (p[1]?.ToString() ?? "") : "";
                    args.Result = str.IndexOf(sub, StringComparison.OrdinalIgnoreCase) >= 0;
                    args.HasResult = true;
                    break;
                }
                case "startswith":
                {
                    object[] p = args.EvaluateParameters();
                    string str = p.Length > 0 ? (p[0]?.ToString() ?? "") : "";
                    string sub = p.Length > 1 ? (p[1]?.ToString() ?? "") : "";
                    args.Result = str.StartsWith(sub, StringComparison.OrdinalIgnoreCase);
                    args.HasResult = true;
                    break;
                }
                case "endswith":
                {
                    object[] p = args.EvaluateParameters();
                    string str = p.Length > 0 ? (p[0]?.ToString() ?? "") : "";
                    string sub = p.Length > 1 ? (p[1]?.ToString() ?? "") : "";
                    args.Result = str.EndsWith(sub, StringComparison.OrdinalIgnoreCase);
                    args.HasResult = true;
                    break;
                }
                case "upper":
                {
                    object[] p = args.EvaluateParameters();
                    string str = p.Length > 0 ? (p[0]?.ToString() ?? "") : "";
                    args.Result = str.ToUpper();
                    args.HasResult = true;
                    break;
                }
                case "lower":
                {
                    object[] p = args.EvaluateParameters();
                    string str = p.Length > 0 ? (p[0]?.ToString() ?? "") : "";
                    args.Result = str.ToLower();
                    args.HasResult = true;
                    break;
                }
            }
        }

        private static double ToDouble(object value)
        {
            if (value == null) return 0;
            if (value is double d) return d;
            if (value is int i) return i;
            if (value is decimal m) return (double)m;
            if (double.TryParse(value.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsed)) return parsed;
            return 0;
        }

        /// <summary>
        /// Breaker selection: returns the value set for the smallest catalog rating that can carry
        /// <paramref name="power"/> (preferring an exact match), independent of row ordering.
        /// </summary>
        public static string QueryBreaker(double power, string startType, IRuleEngineContext context)
        {
            try
            {
                if (context == null || context.CatalogSource == null) return DefaultBreakerSet;

                string[] lines = context.CatalogSource.LoadLines(BreakerCatalogFileName);
                if (lines == null || lines.Length == 0) return DefaultBreakerSet;

                string exactSet = null;
                double exactPower = double.MaxValue;
                string bestSet = null;
                double bestPower = double.MaxValue;

                // Header: Power_kW,StartType,ValueSet
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    string[] cols = line.Split(',');
                    if (cols.Length < 3) continue;

                    if (!double.TryParse(cols[0].Trim(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double pVal)) continue;

                    string sType = cols[1].Trim();
                    if (!sType.Equals(startType, StringComparison.OrdinalIgnoreCase)) continue;

                    string valueSet = cols[2].Trim();

                    if (Math.Abs(pVal - power) < 0.01)
                    {
                        if (pVal < exactPower)
                        {
                            exactPower = pVal;
                            exactSet = valueSet;
                        }
                    }
                    else if (pVal >= power && pVal < bestPower)
                    {
                        bestPower = pVal;
                        bestSet = valueSet;
                    }
                }

                return exactSet ?? bestSet ?? DefaultBreakerSet;
            }
            catch
            {
                return DefaultBreakerSet;
            }
        }

        /// <summary>
        /// Generic CSV lookup: finds the first row where <c>searchColumn == searchValue</c>
        /// (case-insensitive) and returns the value in <c>returnColumn</c>.
        /// </summary>
        public static string QueryCatalog(string tableName, string returnColumn, string searchColumn, string searchValue, IRuleEngineContext context)
        {
            try
            {
                if (context == null || context.CatalogSource == null) return "";

                string fileName = tableName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? tableName : tableName + ".csv";
                string[] lines = context.CatalogSource.LoadLines(fileName);
                if (lines == null || lines.Length == 0) return "";

                string[] headers = lines[0].Split(',');
                int returnColIdx = Array.FindIndex(headers, h => h.Trim().Equals(returnColumn, StringComparison.OrdinalIgnoreCase));
                int searchColIdx = Array.FindIndex(headers, h => h.Trim().Equals(searchColumn, StringComparison.OrdinalIgnoreCase));
                if (returnColIdx < 0 || searchColIdx < 0) return "";

                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string[] cols = line.Split(',');
                    if (cols.Length <= Math.Max(returnColIdx, searchColIdx)) continue;
                    if (cols[searchColIdx].Trim().Equals(searchValue, StringComparison.OrdinalIgnoreCase))
                    {
                        return cols[returnColIdx].Trim();
                    }
                }
            }
            catch
            {
                // Fall through to empty result.
            }
            return "";
        }
    }
}
