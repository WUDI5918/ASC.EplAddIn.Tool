using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Page = Eplan.EplApi.DataModel.Page;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.HEServices;

namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } // "Info", "Success", "Warning", "Error"
        public string Message { get; set; }
    }

    public partial class ConfiguratorService
    {
        public static List<LogEntry> GenerationLogs { get; } = new List<LogEntry>();
        private static readonly object _generationLogsLock = new object();
        public static event Action<LogEntry> OnLogAdded;

        /// <summary>
        /// Thread-safe clear of the generation log collection.
        /// </summary>
        public static void ClearGenerationLogs()
        {
            lock (_generationLogsLock)
            {
                GenerationLogs.Clear();
            }
        }

        /// <summary>
        /// Returns a consistent snapshot of the generation logs for enumeration,
        /// avoiding "collection modified during enumeration" exceptions.
        /// </summary>
        public static List<LogEntry> SnapshotGenerationLogs()
        {
            lock (_generationLogsLock)
            {
                return GenerationLogs.ToList();
            }
        }

        private static string[] _cachedCatalogLines = null;
        private static DateTime _cachedCatalogTimestamp = DateTime.MinValue;
        private static readonly object _catalogLock = new object();

        public static void Log(string level, string message)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message
            };
            lock (_generationLogsLock)
            {
                GenerationLogs.Add(entry);
            }
            LogDebug($"[{level}] {message}");
            OnLogAdded?.Invoke(entry);
        }
        /// <summary>
        /// Prompts user for variable values, then executes macro placement and placeholder mapping for a Macro-Typical under a safe transaction.
        /// </summary>
        /// <param name="typical">The macro typical to generate.</param>
        /// <param name="db">The in-memory rule library to generate against (the UI passes its
        /// unsaved <c>_db</c> so edits take effect immediately). When <c>null</c>, the library is
        /// loaded from disk to keep other callers working.</param>
        public static string GenerateTypical(MacroTypical typical, ConfiguratorData db = null)
        {
            ClearGenerationLogs();
            Log("Info", $"开始为宏规则 '{typical.Name}' 生成原理图...");

            // 1. Get EPLAN Context
            SelectionSet selection = new SelectionSet();
            Page activePage = null;
            Page[] openedPages = selection.OpenedPages;
            if (openedPages != null && openedPages.Length > 0)
            {
                activePage = openedPages[0];
            }
            if (activePage == null)
            {
                Page[] selectedPages = selection.GetSelectedPages();
                activePage = (selectedPages != null && selectedPages.Length > 0) ? selectedPages[0] : null;
            }

            Project activeProject = null;
            if (activePage != null)
            {
                activeProject = activePage.Project;
            }
            else
            {
                activeProject = selection.GetCurrentProject(false);
            }

            if (activeProject == null)
            {
                string errMsg = "错误：无法获取当前的 EPLAN 活动项目！请确保您已经在 EPLAN 中打开或选中了一个项目。";
                Log("Error", errMsg);
                return errMsg;
            }

            if (activePage == null)
            {
                activePage = activeProject.Pages.FirstOrDefault();
                if (activePage == null)
                {
                    // If the project is completely empty, create a default template page
                    using (Transaction t = new TransactionManager().CreateTransaction())
                    {
                        activePage = new Page();
                        PagePropertyList propList = new PagePropertyList();
                        propList.PAGE_COUNTER = "1";
                        activePage.Create(activeProject, DocumentTypeManager.DocumentType.Circuit, propList);
                        t.Commit();
                    }
                }
            }

            Log("Info", $"当前激活项目: {activeProject.ProjectName}, 激活页面: {activePage.Name}");

            // 2. Prompt user to enter values for all defined variables
            Dictionary<string, string> variableValues = new Dictionary<string, string>();
            if (typical.Variables != null && typical.Variables.Count > 0)
            {
                Log("Info", $"该规则包含 {typical.Variables.Count} 个配置变量，正在展示参数输入表单...");
                bool? dialogResult = null;
                TypicalVariablesInputDialog inputDlg = null;

                inputDlg = new TypicalVariablesInputDialog(typical.Variables, typical.Name);
                dialogResult = inputDlg.ShowDialog();

                if (dialogResult != true)
                {
                    Log("Warning", "用户取消了参数配置，中止绘图流程。");
                    return "已取消生成。";
                }

                variableValues = inputDlg.VariableValues;
            }

            // Build typed parameter dictionary for Jint
            Dictionary<string, object> parameters = new Dictionary<string, object>();
            if (typical.Variables != null)
            {
                foreach (var variable in typical.Variables)
                {
                    if (variableValues.TryGetValue(variable.Name, out string val))
                    {
                        object typedVal = val;
                        if (variable.Type.Equals("Integer", StringComparison.OrdinalIgnoreCase))
                        {
                            if (int.TryParse(val, out int intVal)) typedVal = intVal;
                        }
                        else if (variable.Type.Equals("Double", StringComparison.OrdinalIgnoreCase))
                        {
                            if (double.TryParse(val, out double doubleVal)) typedVal = doubleVal;
                        }
                        else if (variable.Type.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
                        {
                            if (bool.TryParse(val, out bool boolVal)) typedVal = boolVal;
                        }
                        parameters[variable.Name] = typedVal;
                        Log("Info", $"参数设定: {variable.Name} = {typedVal} (类型: {variable.Type})");
                    }
                    else
                    {
                        parameters[variable.Name] = null;
                        Log("Info", $"参数设定: {variable.Name} = null");
                    }
                }
            }

            // Use the caller-provided in-memory library (the UI passes its unsaved _db so edits
            // take effect immediately); otherwise fall back to the on-disk library so other
            // callers keep working. A failed disk read is logged, never silently swallowed into
            // an empty library.
            if (db == null)
            {
                db = new ConfiguratorData();
                try
                {
                    string dbPath = GetDatabasePath();
                    if (File.Exists(dbPath))
                    {
                        string json = File.ReadAllText(dbPath);
                        db = Newtonsoft.Json.JsonConvert.DeserializeObject<ConfiguratorData>(json) ?? new ConfiguratorData();
                    }
                    else
                    {
                        Log("Warning", $"规则库文件不存在: '{dbPath}'，将以空库继续生成。");
                    }
                }
                catch (Exception dbEx)
                {
                    Log("Warning", $"读取规则库失败: {dbEx.Message}，将以空库继续生成。");
                    LogDebug($"读取规则库失败: {dbEx.Message}\n{dbEx.StackTrace}");
                }
            }

            int placedCount = 0;
            int placeholderUpdates = 0;
            Page lastEmpPage = null;

            ClearedPages.Clear();
            _pageBoundingBoxes.Clear();

            var initialPages = new HashSet<string>(activeProject.Pages.Select(p => p.Name));

            using (Transaction transaction = new TransactionManager().CreateTransaction())
            using (LockingStep lockingStep = new LockingStep())
            {
                try
                {
                    Eplan.EplApi.Base.Progress progress = new Eplan.EplApi.Base.Progress("正在自动生成原理图...");
                    progress.ShowImmediately();

                    GenerateTypicalInternal(typical, db, parameters, activeProject, activePage, ref placedCount, ref placeholderUpdates, ref lastEmpPage);

                    progress.EndPart();
                    transaction.Commit();

                    string successMsg = $"生成完成！成功插入了 {placedCount} 个宏模板，更新并应用了 {placeholderUpdates} 处占位符变量。";
                    Log("Success", successMsg);
                    return successMsg;
                }
                catch (Exception ex)
                {
                    transaction.Abort();
                    string errMsg = $"错误：生成失败，项目已回滚！\n异常信息: {ex.Message}";
                    Log("Error", errMsg);
                    throw;
                }
                finally
                {
                    // t9: restore per-run generation options to their safe defaults so a value
                    // set by the silent action (JSON "Incremental"/"AbortOnFirstError") never
                    // leaks into later UI-triggered generations.
                    ResetGenerationOptions();
                }
            }
        }

        private static string GetSafeProperty(Eplan.EplApi.DataModel.StorableObjectPropertyList props, Eplan.EplApi.DataModel.AnyPropertyId prop)
        {
            try
            {
                if (props.Exists(prop))
                {
                    try
                    {
                        return props[prop].ToString();
                    }
                    catch
                    {
                        try
                        {
                            return props[prop, 1].ToString();
                        }
                        catch
                        {
                            return props[prop, 0].ToString();
                        }
                    }
                }
            }
            catch { }
            return "";
        }

        private static int GetSafeIntProperty(Eplan.EplApi.DataModel.StorableObjectPropertyList props, Eplan.EplApi.DataModel.AnyPropertyId prop, int defaultValue = 0)
        {
            try
            {
                if (props.Exists(prop))
                {
                    try
                    {
                        return props[prop].ToInt();
                    }
                    catch
                    {
                        try
                        {
                            return props[prop, 0].ToInt();
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return defaultValue;
        }

        private static double GetSafeDoubleProperty(Eplan.EplApi.DataModel.StorableObjectPropertyList props, Eplan.EplApi.DataModel.AnyPropertyId prop, double defaultValue = 0.0)
        {
            try
            {
                if (props.Exists(prop))
                {
                    try
                    {
                        return props[prop].ToDouble();
                    }
                    catch
                    {
                        try
                        {
                            return props[prop, 0].ToDouble();
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return defaultValue;
        }

        private static readonly ConcurrentQueue<string> _logBuffer = new ConcurrentQueue<string>();
        private static volatile bool _logFlushScheduled = false;
        private static string _cachedDataDirectory;
        private static string _cachedLogPath;

        public static void LogDebug(string message)
        {
            _logBuffer.Enqueue($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}");

            if (!_logFlushScheduled)
            {
                _logFlushScheduled = true;
                Task.Run(async () =>
                {
                    await Task.Delay(500); // 等待批量积累
                    FlushLogBuffer();
                    _logFlushScheduled = false;
                });
            }
        }

        private static void FlushLogBuffer()
        {
            var messages = new List<string>();
            while (_logBuffer.TryDequeue(out string msg))
            {
                messages.Add(msg);
            }
            if (messages.Count > 0)
            {
                try
                {
                    string logFile = GetLogPath();
                    if (!string.IsNullOrEmpty(logFile))
                    {
                        File.AppendAllText(logFile, string.Join(Environment.NewLine, messages) + Environment.NewLine);
                    }
                }
                catch { }
            }
        }

        public static string ValidateExpression(string expressionStr, List<string> availableVariables)
        {
            if (string.IsNullOrWhiteSpace(expressionStr)) return null;
            if (!expressionStr.Trim().StartsWith("=")) return null; // Constant

            // Delegate to the same NCalc engine used at evaluation time so that validation
            // and generation never diverge.
            return RuleEngine.Validate(expressionStr, availableVariables, _ruleEngineContext);
        }

        /// <summary>
        /// Validates any formula (with or without a leading '='). Returns null when valid, or the
        /// error message when invalid. Used by pre-flight validation.
        /// </summary>
        public static string ValidateFormula(string formula, IEnumerable<string> variableNames)
        {
            if (string.IsNullOrWhiteSpace(formula)) return null;
            return RuleEngine.Validate(formula, variableNames, _ruleEngineContext);
        }

        public static string ResolveMacroPath(string macroPath)
        {
            if (string.IsNullOrWhiteSpace(macroPath)) return macroPath;

            string resolvedPath = PathMap.SubstitutePath(macroPath);
            if (System.IO.File.Exists(resolvedPath))
            {
                return resolvedPath;
            }

            // Fallback: if not found and is relative (non-rooted and doesn't start with $), prepend $(MD_MACROS)\
            if (!Path.IsPathRooted(macroPath) && !macroPath.StartsWith("$"))
            {
                string fallback = PathMap.SubstitutePath($"$(MD_MACROS)\\{macroPath}");
                if (System.IO.File.Exists(fallback))
                {
                    return fallback;
                }
            }

            return resolvedPath;
        }

        private static string[] LoadCatalogCached(string catalogPath)
        {
            lock (_catalogLock)
            {
                DateTime lastWrite = File.GetLastWriteTimeUtc(catalogPath);
                if (_cachedCatalogLines == null || lastWrite > _cachedCatalogTimestamp)
                {
                    if (File.Exists(catalogPath))
                    {
                        _cachedCatalogLines = File.ReadAllLines(catalogPath);
                        _cachedCatalogTimestamp = lastWrite;
                    }
                    else
                    {
                        _cachedCatalogLines = null;
                    }
                }
                return _cachedCatalogLines;
            }
        }

        /// <summary>
        /// Returns the user-writable data directory under %APPDATA%/ASC.EplAddIn.Tool.
        /// Creates the directory if it does not exist.
        /// </summary>
        private static string GetDataDirectory()
        {
            if (_cachedDataDirectory != null) return _cachedDataDirectory;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dataDir = Path.Combine(appData, "ASC.EplAddIn.Tool");
            if (!Directory.Exists(dataDir))
            {
                Directory.CreateDirectory(dataDir);
            }
            _cachedDataDirectory = dataDir;
            return dataDir;
        }

        /// <summary>
        /// Tries to copy a data file from the assembly directory to the AppData data directory
        /// if the target does not yet exist (first-run scenario).
        /// </summary>
        private static void EnsureDataFileCopied(string fileName)
        {
            try
            {
                string asmDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(asmDir)) return;
                string source = Path.Combine(asmDir, fileName);
                string target = Path.Combine(GetDataDirectory(), fileName);
                if (File.Exists(source) && !File.Exists(target))
                {
                    File.Copy(source, target, overwrite: false);
                }
            }
            catch { }
        }

        public static string GetDatabasePath()
        {
            // Prefer assembly directory for dev/debug scenarios (file exists there)
            try
            {
                string asmDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(asmDir))
                {
                    string path = Path.Combine(asmDir, "configurator_data.json");
                    if (File.Exists(path)) return path;
                }
            }
            catch { }
            // First-run: copy from assembly dir if available
            EnsureDataFileCopied("configurator_data.json");
            return Path.Combine(GetDataDirectory(), "configurator_data.json");
        }

        public static string GetCatalogPath()
        {
            try
            {
                string asmDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(asmDir))
                {
                    string path = Path.Combine(asmDir, "breaker_catalog.csv");
                    if (File.Exists(path)) return path;
                }
            }
            catch { }
            EnsureDataFileCopied("breaker_catalog.csv");
            return Path.Combine(GetDataDirectory(), "breaker_catalog.csv");
        }

        public static string GetLogPath()
        {
            if (_cachedLogPath != null) return _cachedLogPath;
            // Log file always goes to AppData — never write to Program Files
            _cachedLogPath = Path.Combine(GetDataDirectory(), "debug_log.txt");
            return _cachedLogPath;
        }

        /// <summary>
        /// Writes the current GenerationLogs to a dedicated generation log file.
        /// Used in silent mode to persist generation results without showing UI.
        /// </summary>
        public static void WriteGenerationLogToFile(string customFileName = null)
        {
            try
            {
                string logDir = GetDataDirectory();
                string fileName = customFileName ?? "generation_log.txt";
                string logPath = Path.Combine(logDir, fileName);

                var lines = new List<string>();
                lines.Add($"=== Generation Log — {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                foreach (var entry in SnapshotGenerationLogs())
                {
                    lines.Add($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] [{entry.Level}] {entry.Message}");
                }
                lines.Add("=== End of Generation Log ===");
                lines.Add("");

                File.AppendAllText(logPath, string.Join(Environment.NewLine, lines));
                LogDebug($"Generation log written to: {logPath}");
            }
            catch (Exception ex)
            {
                LogDebug($"Failed to write generation log: {ex.Message}");
            }
        }

        public static string FormatPageName(string functionalAssignment, string functionDesignation, string locationDesignation, string documentType, string userDefined, string pageName)
        {
            string fullPageName = "";
            if (!string.IsNullOrWhiteSpace(functionalAssignment)) fullPageName += "==" + functionalAssignment.Trim();
            if (!string.IsNullOrWhiteSpace(functionDesignation)) fullPageName += "=" + functionDesignation.Trim();
            if (!string.IsNullOrWhiteSpace(locationDesignation)) fullPageName += "+" + locationDesignation.Trim();
            if (!string.IsNullOrWhiteSpace(documentType)) fullPageName += "&" + documentType.Trim();
            if (!string.IsNullOrWhiteSpace(userDefined)) fullPageName += "#" + userDefined.Trim();
            fullPageName += "/" + (pageName ?? "").Trim();
            return fullPageName;
        }

        public static string FormatPageName(PageStructure structure)
        {
            if (structure == null) return "";
            return FormatPageName(
                structure.FunctionalAssignment,
                structure.FunctionDesignation,
                structure.LocationDesignation,
                structure.DocumentType,
                structure.UserDefined,
                structure.PageName);
        }

        public static MappingEntry FindPlaceholderMapping(string phName, string varName, List<MappingEntry> mappings)
        {
            if (mappings == null || string.IsNullOrEmpty(phName) || string.IsNullOrEmpty(varName))
            {
                return null;
            }

            string colonWithSpaces = $"{phName} : {varName}";
            string colonNoSpaces = $"{phName}:{varName}";
            string equalSign = $"{phName}={varName}";
            string dotSeparator = $"{phName}.{varName}";

            return mappings.FirstOrDefault(m =>
                m.Key != null && (
                m.Key.Equals(colonWithSpaces, StringComparison.OrdinalIgnoreCase) ||
                m.Key.Equals(colonNoSpaces, StringComparison.OrdinalIgnoreCase) ||
                m.Key.Equals(equalSign, StringComparison.OrdinalIgnoreCase) ||
                m.Key.Equals(dotSeparator, StringComparison.OrdinalIgnoreCase) ||
                m.Key.Equals(varName, StringComparison.OrdinalIgnoreCase)
                ));
        }

        public static void ValidateVariables(string contextName, List<ConfigurationVariable> variables, Dictionary<string, object> parameters)
        {
            if (variables == null) return;
            var safeParameters = parameters ?? new Dictionary<string, object>();
            foreach (var variable in variables)
            {
                if (variable.IsMandatory)
                {
                    bool isVarVisible = true;
                    if (!string.IsNullOrWhiteSpace(variable.VisibilityFormula))
                    {
                        isVarVisible = EvaluateActiveFormula(variable.VisibilityFormula, safeParameters);
                    }
                    var val = safeParameters.ContainsKey(variable.Name) ? safeParameters[variable.Name] : null;
                    if (isVarVisible && (val == null || string.IsNullOrWhiteSpace(val.ToString())))
                    {
                        throw new ArgumentException($"必填参数 '{variable.Name}' (在规则/组 '{contextName}' 中) 不能为空值！");
                    }
                }
            }
        }
    }

    public class EplanMacroInfo
    {
        public string ProjectName { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string MacroPath { get; set; }
        public bool IsPageMacro { get; set; }
        public bool IsSymbolMacro { get; set; }
        public int VariantIndex { get; set; }
        public int RepresentationType { get; set; }
        public List<string> Placeholders { get; set; } = new List<string>();
    }
}
