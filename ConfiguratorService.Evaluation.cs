using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Page = Eplan.EplApi.DataModel.Page;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.HEServices;

namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    public partial class ConfiguratorService
    {
        // ==================== VALUE / FORMULA EVALUATION (Jint) ====================

        /// <summary>
        /// Preprocess NCalc-style logical operators (and/or/not) to JavaScript equivalents (&&/||/!).
        /// Uses word-boundary regex to avoid replacing inside string literals.
        /// </summary>
        internal static string PreprocessNCalcSyntax(string expression)
        {
            // Replace NCalc logical operators with JS equivalents (case-insensitive, word boundaries)
            expression = Regex.Replace(expression, @"\band\b", "&&", RegexOptions.IgnoreCase);
            expression = Regex.Replace(expression, @"\bor\b", "||", RegexOptions.IgnoreCase);
            expression = Regex.Replace(expression, @"\bnot\b", "!", RegexOptions.IgnoreCase);
            return expression;
        }

        private static void RegisterMathFunctions(Jint.Engine engine)
        {
            engine.SetValue("Round", new Func<double, double>(Math.Round));
            engine.SetValue("Abs", new Func<double, double>(Math.Abs));
            engine.SetValue("Max", new Func<double, double, double>(Math.Max));
            engine.SetValue("Min", new Func<double, double, double>(Math.Min));
            engine.SetValue("Pow", new Func<double, double, double>(Math.Pow));
            engine.SetValue("Sqrt", new Func<double, double>(Math.Sqrt));
            engine.SetValue("Log", new Func<double, double>(Math.Log));
            engine.SetValue("Sin", new Func<double, double>(Math.Sin));
            engine.SetValue("Cos", new Func<double, double>(Math.Cos));
            engine.SetValue("Tan", new Func<double, double>(Math.Tan));
        }

        private static Jint.Engine CreateEngine(Dictionary<string, object> parameters)
        {
            var engine = new Jint.Engine();
            // Register custom engineering functions
            engine.SetValue("Lookup_Breaker", new Func<double, string, string>((power, startType) => QueryBreakerCatalog(power, startType ?? "")));
            engine.SetValue("Lookup", new Func<string, string, string, string, string>((tableName, returnCol, searchCol, searchVal) => QueryGenericCatalog(tableName ?? "", returnCol ?? "", searchCol ?? "", searchVal ?? "")));
            // Concat: defined as JS function to support variable arguments (2+)
            engine.Execute(@"
                function Concat() {
                    var result = '';
                    for (var i = 0; i < arguments.length; i++) {
                        var a = arguments[i];
                        result += (a === null || a === undefined) ? '' : String(a);
                    }
                    return result;
                }
            ");
            engine.SetValue("IsAvailable", new Func<string, bool>((varName) => parameters != null && parameters.ContainsKey(varName) && parameters[varName] != null));
            engine.SetValue("IsNullEmpty", new Func<string, bool>((val) => string.IsNullOrEmpty(val)));

            // Backwards compatibility functions
            engine.SetValue("Contains", new Func<string, string, bool>((str, sub) => (str ?? "").IndexOf(sub ?? "", StringComparison.OrdinalIgnoreCase) >= 0));
            engine.SetValue("StartsWith", new Func<string, string, bool>((str, sub) => (str ?? "").StartsWith(sub ?? "", StringComparison.OrdinalIgnoreCase)));
            engine.SetValue("EndsWith", new Func<string, string, bool>((str, sub) => (str ?? "").EndsWith(sub ?? "", StringComparison.OrdinalIgnoreCase)));
            engine.SetValue("Upper", new Func<string, string>((str) => (str ?? "").ToUpper()));
            engine.SetValue("Lower", new Func<string, string>((str) => (str ?? "").ToLower()));

            // Since 'If' is a reserved keyword in JS, we provide a capitalized 'If' wrapper
            engine.SetValue("If", new Func<bool, object, object, object>((cond, t, f) => cond ? t : f));

            // Register NCalc-compatible math functions
            RegisterMathFunctions(engine);

            return engine;
        }

        public static object EvaluateExpression(string expressionStr, Dictionary<string, object> parameters)
        {
            if (string.IsNullOrWhiteSpace(expressionStr)) return null;

            try
            {
                string clean = expressionStr.Trim();
                if (clean.StartsWith("=")) clean = clean.Substring(1).Trim();

                // Preprocess NCalc-style syntax (and/or/not) to JS equivalents
                clean = PreprocessNCalcSyntax(clean);

                // Create a fresh engine instance per call to avoid cross-call variable leakage
                var engine = CreateEngine(parameters);

                // Inject dynamic EPLAN system variables
                string projectName = !string.IsNullOrEmpty(CachedProjectName) ? CachedProjectName : "AutoConfig_Project";
                engine.SetValue("ProjectName", projectName);
                engine.SetValue("Date", DateTime.Now.ToString("yyyy-MM-dd"));
                engine.SetValue("User", Environment.UserName);

                // Bind parameters
                foreach (var kvp in parameters)
                {
                    engine.SetValue(kvp.Key, kvp.Value);
                }

                try
                {
                    var result = engine.Evaluate(clean);
                    return result.ToObject();
                }
                finally
                {
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"表达式 '{expressionStr}' 解析失败: {ex.Message}", ex);
            }
        }

        public static bool EvaluateActiveFormula(string formula, Dictionary<string, object> parameters)
        {
            if (string.IsNullOrWhiteSpace(formula)) return true;
            try
            {
                object result = EvaluateExpression(formula, parameters);
                if (result is bool b) return b;
                if (result != null && bool.TryParse(result.ToString(), out bool parsedBool))
                {
                    return parsedBool;
                }
                return true;
            }
            catch (Exception ex)
            {
                Log("Warning", $"激活条件公式评估异常 '{formula}': {ex.Message}，默认回退为禁用(false)");
                return false;
            }
        }

        private static string EvaluateValueFormula(string formula, Dictionary<string, object> parameters)
        {
            if (string.IsNullOrWhiteSpace(formula)) return "";
            if (!formula.StartsWith("=")) return formula; // Static value
            try
            {
                object result = EvaluateExpression(formula, parameters);
                return result?.ToString() ?? "";
            }
            catch (Exception ex)
            {
                Log("Warning", $"值计算公式评估异常 '{formula}': {ex.Message}，回退使用原始公式字符串");
                return formula;
            }
        }

        // ==================== CSV CATALOG LOOKUP ====================

        private static readonly Dictionary<string, (string[] Lines, DateTime Timestamp)> _genericCatalogCache = new Dictionary<string, (string[], DateTime)>(StringComparer.OrdinalIgnoreCase);

        public static string GetDatabaseDirectory()
        {
            // Prefer assembly directory when data files exist there (dev/debug)
            try
            {
                string asmDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(asmDir))
                {
                    // Only use asmDir if the expected files are actually there
                    if (File.Exists(Path.Combine(asmDir, "configurator_data.json")) ||
                        File.Exists(Path.Combine(asmDir, "breaker_catalog.csv")))
                    {
                        return asmDir;
                    }
                }
            }
            catch { }
            // Fallback to user-writable AppData directory
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dataDir = Path.Combine(appData, "ASC.EplAddIn.Tool");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }
            return dataDir;
        }

        private static string[] LoadGenericCatalogCached(string catalogPath)
        {
            lock (_catalogLock) // shared with ConfiguratorService.cs
            {
                DateTime lastWrite = File.Exists(catalogPath) ? File.GetLastWriteTimeUtc(catalogPath) : DateTime.MinValue;
                if (!_genericCatalogCache.TryGetValue(catalogPath, out var cache) || lastWrite > cache.Timestamp)
                {
                    if (File.Exists(catalogPath))
                    {
                        var lines = File.ReadAllLines(catalogPath);
                        _genericCatalogCache[catalogPath] = (lines, lastWrite);
                        return lines;
                    }
                    else
                    {
                        _genericCatalogCache[catalogPath] = (new string[0], DateTime.MinValue);
                        return new string[0];
                    }
                }
                return cache.Lines;
            }
        }

        private static string QueryGenericCatalog(string tableName, string returnColumn, string searchColumn, string searchValue)
        {
            try
            {
                string fileName = tableName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? tableName : tableName + ".csv";
                string dbDir = GetDatabaseDirectory();
                if (string.IsNullOrEmpty(dbDir)) return "";
                string catalogPath = Path.Combine(dbDir, fileName);
                
                if (!File.Exists(catalogPath)) return "";

                string[] lines = LoadGenericCatalogCached(catalogPath);
                if (lines.Length == 0) return "";

                string headerLine = lines[0];
                string[] headers = headerLine.Split(',');

                int returnColIdx = Array.FindIndex(headers, h => h.Trim().Equals(returnColumn, StringComparison.OrdinalIgnoreCase));
                int searchColIdx = Array.FindIndex(headers, h => h.Trim().Equals(searchColumn, StringComparison.OrdinalIgnoreCase));

                if (returnColIdx < 0 || searchColIdx < 0) return "";

                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string[] cols = line.Split(',');

                    if (cols.Length > Math.Max(returnColIdx, searchColIdx))
                    {
                        if (cols[searchColIdx].Trim().Equals(searchValue, StringComparison.OrdinalIgnoreCase))
                        {
                            return cols[returnColIdx].Trim();
                        }
                    }
                }
            }
            catch
            {
                // Fallback
            }
            return "";
        }

        private static string QueryBreakerCatalog(double power, string startType)
        {
            try
            {
                string catalogPath = GetCatalogPath();
                if (!File.Exists(catalogPath))
                {
                    return "Default_Set";
                }

                string[] lines = LoadCatalogCached(catalogPath);
                // Header: Power_kW,StartType,ValueSet
                for (int i = 1; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    string[] cols = line.Split(',');
                    if (cols.Length >= 3)
                    {
                        double pVal;
                        if (double.TryParse(cols[0].Trim(), out pVal))
                        {
                            string sType = cols[1].Trim();
                            string valueSet = cols[2].Trim();

                            if (sType.Equals(startType, StringComparison.OrdinalIgnoreCase))
                            {
                                if (Math.Abs(pVal - power) < 0.01 || pVal >= power)
                                {
                                    return valueSet;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Fallback on error
            }
            return "Default_Set";
        }


    }
}
