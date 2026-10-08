using System;
using System.Collections.Generic;
using System.IO;

namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    public partial class ConfiguratorService
    {
        // ==================== VALUE / FORMULA EVALUATION (NCalc via RuleEngine) ====================

        // The host-side context injects EPLAN-provided values (project/user/date) and catalog file
        // access into the EPLAN-independent RuleEngine.
        private static readonly IRuleEngineContext _ruleEngineContext = new RuleEngineContextImpl();

        public static object EvaluateExpression(string expressionStr, Dictionary<string, object> parameters)
        {
            if (string.IsNullOrWhiteSpace(expressionStr)) return null;

            try
            {
                return RuleEngine.Evaluate(expressionStr, parameters, _ruleEngineContext);
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
                object result = RuleEngine.Evaluate(formula, parameters, _ruleEngineContext);
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
            if (!formula.Trim().StartsWith("=")) return formula; // Static value
            try
            {
                object result = RuleEngine.Evaluate(formula, parameters, _ruleEngineContext);
                return result?.ToString() ?? "";
            }
            catch (Exception ex)
            {
                Log("Warning", $"值计算公式评估异常 '{formula}': {ex.Message}，回退使用原始公式字符串");
                return formula;
            }
        }

        // ==================== CATALOG LOADING (host side) ====================

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
            catch (Exception ex)
            {
                // asmDir probing failed (e.g. restricted permissions / unreadable assembly path);
                // fall back to the user-writable AppData directory below.
                LogDebug($"探测程序集目录数据文件失败，回退到 AppData: {ex.Message}");
            }
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
                        try
                        {
                            var lines = File.ReadAllLines(catalogPath);
                            _genericCatalogCache[catalogPath] = (lines, lastWrite);
                            return lines;
                        }
                        catch (Exception ex)
                        {
                            // Read failure (locked/inaccessible/corrupt file): log it and return an
                            // empty array so callers keep their documented empty-result semantics.
                            // The cache entry is stamped DateTime.MinValue so a transient failure is
                            // retried on the next lookup (matches the pre-catch propagation behavior,
                            // where RuleEngine.QueryCatalog swallowed the exception and returned "").
                            LogDebug($"读取目录文件失败 '{catalogPath}': {ex.Message}");
                            _genericCatalogCache[catalogPath] = (new string[0], DateTime.MinValue);
                            return new string[0];
                        }
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

        /// <summary>
        /// Host-side <see cref="IRuleEngineContext"/> that resolves catalog files and system variables
        /// from the EPLAN add-in's data directories and static project state.
        /// </summary>
        private sealed class RuleEngineContextImpl : IRuleEngineContext, ICatalogSource
        {
            public string ProjectName => !string.IsNullOrEmpty(CachedProjectName) ? CachedProjectName : "AutoConfig_Project";

            public string User => Environment.UserName;

            public string Date => DateTime.Now.ToString("yyyy-MM-dd");

            public ICatalogSource CatalogSource => this;

            public string[] LoadLines(string fileName)
            {
                if (string.Equals(fileName, "breaker_catalog.csv", StringComparison.OrdinalIgnoreCase))
                {
                    string path = GetCatalogPath();
                    if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                    return LoadCatalogCached(path);
                }

                string dbDir = GetDatabaseDirectory();
                if (string.IsNullOrEmpty(dbDir)) return null;
                string catalogPath = Path.Combine(dbDir, fileName);
                if (!File.Exists(catalogPath)) return null;
                return LoadGenericCatalogCached(catalogPath);
            }
        }
    }
}
