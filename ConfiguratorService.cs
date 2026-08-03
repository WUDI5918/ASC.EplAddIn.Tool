using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Level { get; set; } // "Info", "Success", "Warning", "Error"
        public string Message { get; set; }
    }

    public partial class ConfiguratorService
    {
        public static List<LogEntry> GenerationLogs { get; } = new List<LogEntry>();
        public static event Action<LogEntry> OnLogAdded;

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
            GenerationLogs.Add(entry);
            LogDebug($"[{level}] {message}");
            OnLogAdded?.Invoke(entry);
        }
        /// <summary>
        /// Prompts user for variable values, then executes macro placement and placeholder mapping for a Macro-Typical under a safe transaction.
        /// </summary>
        public static string GenerateTypical(MacroTypical typical)
        {
            GenerationLogs.Clear();
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

            // Load database to pass to GenerateTypicalInternal if needed
            ConfiguratorData db = new ConfiguratorData();
            try
            {
                string dbPath = GetDatabasePath();
                if (File.Exists(dbPath))
                {
                    string json = File.ReadAllText(dbPath);
                    db = Newtonsoft.Json.JsonConvert.DeserializeObject<ConfiguratorData>(json) ?? new ConfiguratorData();
                }
            }
            catch { }

            int placedCount = 0;
            int placeholderUpdates = 0;
            Page lastEmpPage = null;

            ClearedPages.Clear();

            var initialPages = new HashSet<string>(activeProject.Pages.Select(p => p.Name));

            using (Transaction transaction = new TransactionManager().CreateTransaction())
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
            }
        }

        // ==================== VARIABLE USER INPUT DIALOG ====================

        private static Dictionary<string, string> PromptForVariables(List<ConfigurationVariable> variables)
        {
            Dictionary<string, string> values = new Dictionary<string, string>();
            foreach (var variable in variables)
            {
                string val = ShowInputBox(
                    $"请输入变量 [{variable.Name}] 的值：\n(变量显示名: {variable.DisplayName})",
                    variable.Name,
                    ""
                );
                values[variable.Name] = val;
            }
            return values;
        }

        private static string ShowInputBox(string text, string title, string defaultValue)
        {
            string result = defaultValue;

            Window prompt = new Window()
            {
                Width = 420,
                Height = 160,
                Title = "配置变量 input - " + title,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 244, 246)),
                FontFamily = new FontFamily("Microsoft YaHei"),
                Topmost = true
            };

            StackPanel sp = new StackPanel { Margin = new Thickness(15) };

            TextBlock lbl = new TextBlock
            {
                Text = text,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };

            TextBox txt = new TextBox
            {
                Text = defaultValue,
                Height = 28,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };

            Button btn = new Button
            {
                Content = "确定 (OK)",
                Width = 90,
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Right,
                IsDefault = true,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(205, 55, 55)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0)
            };

            btn.Click += (s, e) =>
            {
                result = txt.Text;
                prompt.DialogResult = true;
                prompt.Close();
            };

            sp.Children.Add(lbl);
            sp.Children.Add(txt);
            sp.Children.Add(btn);
            prompt.Content = sp;
            prompt.ShowDialog();
            return result;
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
            try
            {
                string clean = expressionStr.Trim();
                if (clean.StartsWith("="))
                {
                    clean = clean.Substring(1).Trim();
                }
                else
                {
                    return null; // Constant
                }

                // Preprocess NCalc-style syntax (and/or/not) to JS equivalents
                clean = PreprocessNCalcSyntax(clean);

                // Use Jint engine to validate — same engine used for evaluation
                var validationEngine = new Jint.Engine();

                // Register custom engineering functions (same as EvaluateExpression)
                validationEngine.SetValue("Lookup_Breaker", new Func<double, string, string>((power, startType) => "Mock"));
                validationEngine.SetValue("Lookup", new Func<string, string, string, string, string>((a, b, c, d) => "Mock"));
                // Concat: defined as JS function to support variable arguments (2+), same as EvaluateExpression
                validationEngine.Execute(@"
                    function Concat() {
                        var result = '';
                        for (var i = 0; i < arguments.length; i++) {
                            var a = arguments[i];
                            result += (a === null || a === undefined) ? '' : String(a);
                        }
                        return result;
                    }
                ");
                validationEngine.SetValue("IsAvailable", new Func<string, bool>((varName) => true));
                validationEngine.SetValue("IsNullEmpty", new Func<string, bool>((val) => false));
                validationEngine.SetValue("Contains", new Func<string, string, bool>((str, sub) => false));
                validationEngine.SetValue("StartsWith", new Func<string, string, bool>((str, sub) => false));
                validationEngine.SetValue("EndsWith", new Func<string, string, bool>((str, sub) => false));
                validationEngine.SetValue("Upper", new Func<string, string>((str) => (str ?? "").ToUpper()));
                validationEngine.SetValue("Lower", new Func<string, string>((str) => (str ?? "").ToLower()));
                validationEngine.SetValue("If", new Func<bool, object, object, object>((cond, t, f) => cond ? t : f));

                // Register NCalc-compatible math functions (same as EvaluateExpression)
                RegisterMathFunctions(validationEngine);

                // Inject mock system variables
                validationEngine.SetValue("ProjectName", "Mock_Project");
                validationEngine.SetValue("Date", DateTime.Now.ToString("yyyy-MM-dd"));
                validationEngine.SetValue("User", "MockUser");

                // Bind available variables with mock values
                if (availableVariables != null)
                {
                    foreach (var varName in availableVariables)
                    {
                        validationEngine.SetValue(varName, 0);
                    }
                }

                // Try to evaluate — if it throws, the expression is invalid
                validationEngine.Evaluate(clean);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            return null;
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
                foreach (var entry in GenerationLogs)
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