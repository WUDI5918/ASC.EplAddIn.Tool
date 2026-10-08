using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Xml.Linq;
using OfficeOpenXml;
using Newtonsoft.Json.Linq;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Gui;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;

namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    /// <summary>
    /// EPLAN Add-in Entry point implementing IEplAddIn.
    /// Handles menu and ribbon bar registration.
    /// </summary>
    public class AddInMenuModel : IEplAddIn
    {
        private const string MenuText = "AutoSchematicConfigurator";
        private const string SubMenuText = "配置生成器";
        private const string ActionName = "AutoSchematicConfiguratorAction";

        public bool OnInit()
        {
            return true;
        }

        public bool OnInitGui()
        {
            try
            {
                // Support Modern Ribbon Interface (EPLAN 2022+)
                RegisterRibbon();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化界面失败: {ex.Message}", "配置器 Add-In Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return true;
        }

        public bool OnExit()
        {
            return true;
        }

        public bool OnRegister(ref bool bRet)
        {
            bRet = true;
            try
            {
                RegisterRibbon();
            }
            catch
            {
                // 忽略非 GUI 模式下的异常
            }
            return true;
        }

        public bool OnUnregister()
        {
            try
            {
                UnregisterRibbon();
            }
            catch
            {
                // Silently ignore to avoid unregister blocking
            }
            return true;
        }

        private void RegisterRibbon()
        {
            RibbonBar ribbonBar = new RibbonBar();

            // Check if tab already exists, if not add it
            RibbonTab customTab = ribbonBar.Tabs.FirstOrDefault(t => t.Name == MenuText);
            if (customTab == null)
            {
                customTab = ribbonBar.AddTab(MenuText);
            }

            // Check if group already exists, if not add it
            RibbonCommandGroup commandGroup = customTab.CommandGroups.FirstOrDefault(g => g.Name == MenuText);
            if (commandGroup == null)
            {
                commandGroup = customTab.AddCommandGroup(MenuText);
            }

            // Check if command already exists, if not add it
            bool exists = false;
            try
            {
                if (commandGroup.Commands != null)
                {
                    exists = commandGroup.Commands.Values.Any(c => c.Text == SubMenuText);
                }
            }
            catch
            {
                // Fallback in case of API access differences
            }

            if (!exists)
            {
                commandGroup.AddCommand(SubMenuText, ActionName);
            }
        }

        private void UnregisterRibbon()
        {
            RibbonBar ribbonBar = new RibbonBar();
            RibbonTab customTab = ribbonBar.Tabs.FirstOrDefault(t => t.Name == MenuText);
            if (customTab != null)
            {
                customTab.Remove();
            }
        }
    }

    /// <summary>
    /// EPLAN Custom Action that triggers when the menu is clicked.
    /// </summary>
    public class AutoSchematicConfiguratorAction : IEplAction
    {
        private static ConfiguratorWindow _activeWindow = null;

        public bool Execute(ActionCallingContext oActionCallingContext)
        {
            try
            {
                // Capture data from existing window before closing (data migration).
                ConfiguratorData savedDb = null;
                bool savedIsDirty = false;
                if (_activeWindow != null)
                {
                    try
                    {
                        savedDb = _activeWindow.Database;
                        savedIsDirty = _activeWindow.HasUnsavedChanges;
                    }
                    catch { }

                    try
                    {
                        _activeWindow.Close();
                    }
                    catch { }
                    _activeWindow = null;
                }

                _activeWindow = new ConfiguratorWindow();

                // Restore data from previous window instance
                if (savedDb != null)
                {
                    _activeWindow.InjectDatabase(savedDb, savedIsDirty);
                }

                // Set owner to EPLAN main window to maintain proper window hierarchy
                System.Windows.Interop.WindowInteropHelper helper = new System.Windows.Interop.WindowInteropHelper(_activeWindow);
                helper.Owner = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;

                _activeWindow.Show();
                _activeWindow.Activate();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"启动配置生成器失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return true;
        }

        public void GetActionProperties(ref ActionProperties actionProperties)
        {
        }

        public bool OnRegister(ref string Name, ref int Ordinal)
        {
            Name = "AutoSchematicConfiguratorAction";
            Ordinal = 20;
            return true;
        }
    }

    /// <summary>
    /// EPLAN Custom Action for silent/command line execution of Project Builder generation.
    /// </summary>
    public class ExecuteCogineerProjectBuilderAction : IEplAction
    {
        public bool Execute(ActionCallingContext oActionCallingContext)
        {
            try
            {
                ConfiguratorService.LogDebug("--- ExecuteCogineerProjectBuilderAction Invoked ---");

                string token = "";
                oActionCallingContext.GetParameter("token", ref token);

                string libraryName = "";
                oActionCallingContext.GetParameter("libraryName", ref libraryName);

                string configuratorName = "";
                oActionCallingContext.GetParameter("configuratorName", ref configuratorName);

                string storagelocation = "";
                oActionCallingContext.GetParameter("storagelocation", ref storagelocation);

                string targetProjectName = "";
                oActionCallingContext.GetParameter("targetProjectName", ref targetProjectName);

                string template = "";
                oActionCallingContext.GetParameter("template", ref template);

                string xmlfile = "";
                oActionCallingContext.GetParameter("xmlfile", ref xmlfile);

                string importfile = "";
                oActionCallingContext.GetParameter("importfile", ref importfile);

                string overwriteStr = "";
                oActionCallingContext.GetParameter("overwrite", ref overwriteStr);
                bool overwrite = (overwriteStr == "1");

                // C2: JSON parameter file support
                string jsonfile = "";
                oActionCallingContext.GetParameter("jsonfile", ref jsonfile);

                // C4: dryrun validation mode
                string dryrunStr = "";
                oActionCallingContext.GetParameter("dryrun", ref dryrunStr);
                bool dryrun = (dryrunStr == "1");

                ConfiguratorService.LogDebug($"Params: libraryName='{libraryName}', configuratorName='{configuratorName}', targetProjectName='{targetProjectName}', xmlfile='{xmlfile}', importfile='{importfile}', jsonfile='{jsonfile}', dryrun='{dryrunStr}'");

                // C2: Load parameters from JSON file if specified
                JObject jsonParams = null;
                bool? jsonIncremental = null; // t9: optional per-run "Incremental" option
                bool? jsonAbortOnFirstError = null; // t9: optional per-run "AbortOnFirstError" option
                if (!string.IsNullOrEmpty(jsonfile) && File.Exists(jsonfile))
                {
                    ConfiguratorService.LogDebug($"Loading parameters from JSON file: {jsonfile}");
                    string jsonContent = File.ReadAllText(jsonfile);
                    jsonParams = JObject.Parse(jsonContent);

                    // Override parameters from JSON if present
                    if (jsonParams["ConfiguratorName"] != null && string.IsNullOrEmpty(configuratorName))
                        configuratorName = jsonParams["ConfiguratorName"].ToString();
                    if (jsonParams["TemplatePath"] != null && string.IsNullOrEmpty(template))
                        template = jsonParams["TemplatePath"].ToString();
                    if (jsonParams["TargetPath"] != null && string.IsNullOrEmpty(targetProjectName))
                        targetProjectName = jsonParams["TargetPath"].ToString();
                    if (jsonParams["LibraryName"] != null && string.IsNullOrEmpty(libraryName))
                        libraryName = jsonParams["LibraryName"].ToString();
                    if (jsonParams["StorageLocation"] != null && string.IsNullOrEmpty(storagelocation))
                        storagelocation = jsonParams["StorageLocation"].ToString();

                    // t9: per-run generation options (optional). Parsed here, applied immediately
                    // before GenerateConfigurator below; the dry-run path is not affected.
                    jsonIncremental = ParseOptionalBool(jsonParams, "Incremental");
                    jsonAbortOnFirstError = ParseOptionalBool(jsonParams, "AbortOnFirstError");
                    if (jsonIncremental.HasValue)
                        ConfiguratorService.LogDebug($"JSON parameter 'Incremental' = {jsonIncremental.Value}");
                    if (jsonAbortOnFirstError.HasValue)
                        ConfiguratorService.LogDebug($"JSON parameter 'AbortOnFirstError' = {jsonAbortOnFirstError.Value}");

                    ConfiguratorService.LogDebug($"JSON params loaded: configuratorName='{configuratorName}', template='{template}', targetProjectName='{targetProjectName}'");
                }
                else if (!string.IsNullOrEmpty(jsonfile))
                {
                    ConfiguratorService.LogDebug($"Error: JSON parameter file not found: '{jsonfile}'");
                    return false;
                }

                if (string.IsNullOrEmpty(libraryName) || string.IsNullOrEmpty(configuratorName))
                {
                    ConfiguratorService.LogDebug("Error: libraryName and configuratorName must be specified.");
                    return false;
                }

                string dbPath = ConfiguratorService.GetDatabasePath();
                if (!File.Exists(dbPath))
                {
                    ConfiguratorService.LogDebug($"Error: Library file not found at '{dbPath}'");
                    return false;
                }

                string json = File.ReadAllText(dbPath);
                var db = Newtonsoft.Json.JsonConvert.DeserializeObject<ConfiguratorData>(json);
                if (db == null)
                {
                    ConfiguratorService.LogDebug("Error: Failed to deserialize database.");
                    return false;
                }

                // Repair null collections and duplicate structure mappings from hand-edited JSON.
                db.Normalize();

                // Find Configurator
                var configurator = db.Configurators.FirstOrDefault(c => string.Equals(c.Name, configuratorName, StringComparison.OrdinalIgnoreCase));
                if (configurator == null)
                {
                    ConfiguratorService.LogDebug($"Error: Configurator '{configuratorName}' not found in library.");
                    return false;
                }

                // 3. Import parameter values
                string paramFile = !string.IsNullOrEmpty(xmlfile) ? xmlfile : importfile;
                var importedValues = new Dictionary<string, string>();
                if (!string.IsNullOrEmpty(paramFile) && File.Exists(paramFile))
                {
                    if (paramFile.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        importedValues = ImportXml(paramFile);
                    }
                    else if (paramFile.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                    {
                        importedValues = ImportXlsx(paramFile);
                    }
                }

                // Map imported values to group parameters
                var groupParameters = new Dictionary<string, Dictionary<string, object>>();
                if (configurator.TypicalGroupRefs != null)
                {
                    foreach (string groupRef in configurator.TypicalGroupRefs)
                    {
                        var group = db.TypicalGroups.FirstOrDefault(g => string.Equals(g.Name, groupRef, StringComparison.OrdinalIgnoreCase));
                        if (group == null) continue;

                        var parameters = new Dictionary<string, object>();
                        foreach (var variable in group.Variables ?? new List<ConfigurationVariable>())
                        {
                            object val = null;

                            // Priority 1: JSON file Parameters section
                            if (jsonParams != null && jsonParams["Parameters"] != null)
                            {
                                var jsonGroupParams = jsonParams["Parameters"] as JObject;
                                if (jsonGroupParams != null)
                                {
                                    var jsonGroup = jsonGroupParams[group.Name] as JObject;
                                    if (jsonGroup != null && jsonGroup[variable.Name] != null)
                                    {
                                        val = ConvertValueType(jsonGroup[variable.Name].ToString(), variable.Type);
                                    }
                                }
                            }

                            // Priority 2: imported file values
                            if (val == null)
                            {
                                if (importedValues.TryGetValue($"{group.Name}.{variable.Name}", out string v1))
                                {
                                    val = ConvertValueType(v1, variable.Type);
                                }
                                else if (importedValues.TryGetValue(variable.Name, out string v2))
                                {
                                    val = ConvertValueType(v2, variable.Type);
                                }
                                else
                                {
                                    val = ConvertValueType(variable.DefaultValue, variable.Type);
                                }
                            }
                            parameters[variable.Name] = val;
                        }
                        groupParameters[group.Name] = parameters;
                    }
                }

                // C4: dryrun mode — validate only, no generation
                if (dryrun)
                {
                    ConfiguratorService.LogDebug("Dryrun mode: validating parameters...");
                    ConfiguratorService.Log("Info", $"[DryRun] 开始验证配置器 '{configuratorName}' 的参数...");

                    bool preflightOk = RunPreflightAndLog(db, configurator, groupParameters, "[DryRun] ", configuratorName);
                    if (preflightOk)
                    {
                        ConfiguratorService.LogDebug("Dryrun validation completed successfully.");
                    }

                    // C3: Write generation logs to file in silent mode (both pass and fail paths)
                    ConfiguratorService.WriteGenerationLogToFile();
                    return preflightOk;
                }

                // t11: the real generation path runs the same pre-flight validation before it
                // opens/creates or overwrites the target project, so a configurator with errors
                // can never mutate the project.
                if (!RunPreflightAndLog(db, configurator, groupParameters, "", configuratorName))
                {
                    ConfiguratorService.LogDebug("Preflight failed; generation aborted without touching the project.");
                    // C3: Write logs to file in silent mode
                    ConfiguratorService.WriteGenerationLogToFile();
                    return false;
                }

                // 2. Open or Create target project
                ProjectManager pm = new ProjectManager();
                pm.LockProjectByDefault = false;
                Project project = null;

                if (!string.IsNullOrEmpty(targetProjectName))
                {
                    foreach (Project openProj in pm.OpenProjects)
                    {
                        if (string.Equals(openProj.ProjectName, targetProjectName, StringComparison.OrdinalIgnoreCase) && !overwrite)
                        {
                            project = openProj;
                            break;
                        }
                    }

                    if (project == null && !string.IsNullOrEmpty(storagelocation))
                    {
                        string projectPath = Path.Combine(storagelocation, targetProjectName + ".elk");
                        if (File.Exists(projectPath))
                        {
                            if (overwrite)
                            {
                                if (string.IsNullOrEmpty(template) || !File.Exists(template))
                                {
                                    ConfiguratorService.LogDebug("Error: overwrite requires a valid template to recreate the project.");
                                    return false;
                                }

                                try
                                {
                                    foreach (Project openProj in pm.OpenProjects)
                                    {
                                        if (string.Equals(openProj.ProjectName, targetProjectName, StringComparison.OrdinalIgnoreCase))
                                        {
                                            openProj.Close();
                                            break;
                                        }
                                    }

                                    string dirPath = projectPath.Substring(0, projectPath.Length - 4) + ".edb";
                                    if (Directory.Exists(dirPath)) Directory.Delete(dirPath, true);
                                    if (File.Exists(projectPath)) File.Delete(projectPath);
                                }
                                catch (Exception dex)
                                {
                                    ConfiguratorService.LogDebug($"Error: Failed to delete existing project for overwrite: {dex.Message}");
                                    return false;
                                }
                            }
                            else
                            {
                                project = pm.OpenProject(projectPath);
                            }
                        }

                        if (project == null)
                        {
                            if (!string.IsNullOrEmpty(template) && File.Exists(template))
                            {
                                project = pm.CreateProject(projectPath, template);
                            }
                            else if (File.Exists(projectPath))
                            {
                                project = pm.OpenProject(projectPath);
                            }
                            else
                            {
                                ConfiguratorService.LogDebug($"Error: Project '{projectPath}' does not exist and no template was specified.");
                                return false;
                            }
                        }
                    }
                }

                if (project == null)
                {
                    SelectionSet selection = new SelectionSet();
                    if (selection.OpenedPages != null && selection.OpenedPages.Length > 0)
                    {
                        project = selection.OpenedPages[0].Project;
                    }
                }

                if (project == null)
                {
                    ConfiguratorService.LogDebug("Error: No target EPLAN project found or created.");
                    return false;
                }

                // 4. Execute generation in a safe transaction. Note (t11): no page is created here.
                // When the target project has no pages, GenerateConfigurator creates a default
                // template page itself inside its own Transaction (see
                // ConfiguratorService.GenerateConfigurator), so creating one here would both
                // duplicate that logic and run outside any transaction.

                // t9: apply per-run generation options from the JSON parameter file (absent keys fall
                // back to the safe defaults). GenerateConfigurator restores both switches to their safe
                // defaults in its finally block, so UI-triggered generations never inherit these values.
                ConfiguratorService.IncrementalGeneration = jsonIncremental ?? false;
                ConfiguratorService.AbortOnFirstError = jsonAbortOnFirstError ?? true;
                ConfiguratorService.LogDebug($"Generation options applied: IncrementalGeneration={ConfiguratorService.IncrementalGeneration}, AbortOnFirstError={ConfiguratorService.AbortOnFirstError}");

                ConfiguratorService.LogDebug("Starting Configurator generation...");
                string result = ConfiguratorService.GenerateConfigurator(configurator, db, groupParameters);
                ConfiguratorService.LogDebug($"Generation finished: {result}");

                // C3: Write generation logs to file in silent mode (no MessageBox or GenerationLogWindow)
                ConfiguratorService.WriteGenerationLogToFile();

                return true;
            }
            catch (Exception ex)
            {
                ConfiguratorService.LogDebug($"Exception in ExecuteCogineerProjectBuilderAction: {ex.Message}\n{ex.StackTrace}");
                // C3: Write logs even on failure
                ConfiguratorService.WriteGenerationLogToFile();
                return false;
            }
            finally
            {
                // t9: restore the generation options to their safe defaults on every exit path
                // (normal completion, early return before GenerateConfigurator's own finally,
                // or exception), so a per-run option set above never leaks into a later
                // UI-triggered generation. Idempotent with the entry-point finally blocks.
                ConfiguratorService.ResetGenerationOptions();
            }
        }

        public void GetActionProperties(ref ActionProperties actionProperties)
        {
        }

        public bool OnRegister(ref string Name, ref int Ordinal)
        {
            Name = "ExecuteCogineerProjectBuilderAction";
            Ordinal = 30;
            return true;
        }

        /// <summary>
        /// Runs pre-flight validation (<see cref="PreflightValidator"/>) and logs every issue with its
        /// severity, prefixed with <paramref name="prefix"/>. Returns true when the run may proceed
        /// (no errors); false when errors were found or validation itself failed, in which case the
        /// caller must abort without generating. Shared by the dry-run mode and the real generation
        /// path (t11).
        /// </summary>
        private static bool RunPreflightAndLog(
            ConfiguratorData db,
            Configurator configurator,
            Dictionary<string, Dictionary<string, object>> groupParameters,
            string prefix,
            string configuratorName)
        {
            try
            {
                var report = PreflightValidator.ValidateConfigurator(db, configurator, groupParameters);
                foreach (var issue in report.Issues)
                {
                    string level = issue.Severity == PreflightSeverity.Error ? "Error"
                        : (issue.Severity == PreflightSeverity.Warning ? "Warning" : "Info");
                    ConfiguratorService.Log(level, $"{prefix}{issue.Message}");
                }

                if (report.HasErrors)
                {
                    int errorCount = report.Issues.Count(i => i.Severity == PreflightSeverity.Error);
                    ConfiguratorService.Log("Error", $"{prefix}预检失败，共 {errorCount} 个错误。");
                    return false;
                }

                ConfiguratorService.Log("Success", $"{prefix}配置器 '{configuratorName}' 预检通过！");
                return true;
            }
            catch (Exception valEx)
            {
                ConfiguratorService.Log("Error", $"{prefix}参数验证失败: {valEx.Message}");
                ConfiguratorService.LogDebug($"{prefix}Preflight validation failed: {valEx.Message}");
                return false;
            }
        }

        /// <summary>
        /// Reads an optional boolean value from the JSON parameter object. Accepts JSON booleans
        /// (true/false) and bool strings ("true"/"false"/"1"/"0"). Returns null when the key is
        /// absent or not parseable, so callers fall back to the safe defaults (t9).
        /// </summary>
        private static bool? ParseOptionalBool(JObject jsonParams, string key)
        {
            if (jsonParams == null || jsonParams[key] == null) return null;
            var token = jsonParams[key];
            if (token.Type == JTokenType.Boolean) return token.Value<bool>();
            if (bool.TryParse(token.ToString(), out bool parsed)) return parsed;
            ConfiguratorService.LogDebug($"JSON parameter '{key}' 值 '{token}' 不是合法布尔值，使用默认值。");
            return null;
        }

        private object ConvertValueType(string rawVal, string type)
        {
            if (string.IsNullOrEmpty(rawVal)) return null;
            if (type.Equals("Integer", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(rawVal, out int intVal)) return intVal;
            }
            else if (type.Equals("Double", StringComparison.OrdinalIgnoreCase))
            {
                if (double.TryParse(rawVal, out double doubleVal)) return doubleVal;
            }
            else if (type.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
            {
                if (bool.TryParse(rawVal, out bool boolVal)) return boolVal;
            }
            return rawVal;
        }

        private Dictionary<string, string> ImportXml(string filePath)
        {
            var values = new Dictionary<string, string>();
            try
            {
                var doc = XDocument.Load(filePath);
                var root = doc.Root;
                if (root != null && root.Name.LocalName == "Configuration")
                {
                    var varsElement = root.Element("ConfigurationVariables");
                    if (varsElement != null)
                    {
                        foreach (var el in varsElement.Elements("ConfigurationVariable"))
                        {
                            string name = el.Attribute("name")?.Value ?? el.Name.LocalName;
                            string val = el.Value;
                            if (!string.IsNullOrEmpty(name)) values[name] = val;
                        }
                    }

                    foreach (var instanceEl in root.Elements("TypicalInstance"))
                    {
                        string groupName = instanceEl.Attribute("name")?.Value ?? "";
                        foreach (var inst in instanceEl.Elements("Instance"))
                        {
                            var subVars = inst.Element("ConfigurationVariables");
                            if (subVars != null)
                            {
                                foreach (var el in subVars.Elements("ConfigurationVariable"))
                                {
                                    string name = el.Attribute("name")?.Value ?? el.Name.LocalName;
                                    string val = el.Value;
                                    if (!string.IsNullOrEmpty(name))
                                    {
                                        values[name] = val;
                                        if (!string.IsNullOrEmpty(groupName))
                                        {
                                            values[$"{groupName}.{name}"] = val;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return values;
        }

        private Dictionary<string, string> ImportXlsx(string filePath)
        {
            var values = new Dictionary<string, string>();
            try
            {
                using (var package = new ExcelPackage(new FileInfo(filePath)))
                {
                    var sheet = package.Workbook.Worksheets.FirstOrDefault();
                    if (sheet != null)
                    {
                        int rowCount = sheet.Dimension?.End.Row ?? 0;
                        string currentGroup = "";
                        List<string> groupHeaders = new List<string>();

                        for (int row = 2; row <= rowCount; row++)
                        {
                            string colA = sheet.Cells[row, 1].Text?.Trim() ?? "";
                            string colB = sheet.Cells[row, 2].Text?.Trim() ?? "";

                            if (string.IsNullOrEmpty(colA) && string.IsNullOrEmpty(colB))
                            {
                                currentGroup = "";
                                groupHeaders.Clear();
                                continue;
                            }

                            if (string.IsNullOrEmpty(currentGroup) && !string.IsNullOrEmpty(colA) && string.IsNullOrEmpty(colB))
                            {
                                int colCount = sheet.Dimension.End.Column;
                                bool isHeader = false;
                                for (int col = 2; col <= colCount; col++)
                                {
                                    if (!string.IsNullOrEmpty(sheet.Cells[row, col].Text))
                                    {
                                        isHeader = true;
                                        break;
                                    }
                                }

                                if (isHeader)
                                {
                                    currentGroup = colA;
                                    for (int col = 2; col <= colCount; col++)
                                    {
                                        groupHeaders.Add(sheet.Cells[row, col].Text?.Trim() ?? "");
                                    }
                                    continue;
                                }
                            }

                            if (!string.IsNullOrEmpty(currentGroup))
                            {
                                int colCount = sheet.Dimension.End.Column;
                                for (int col = 2; col <= Math.Min(colCount, groupHeaders.Count + 1); col++)
                                {
                                    string header = groupHeaders[col - 2];
                                    string val = sheet.Cells[row, col].Text?.Trim() ?? "";
                                    if (!string.IsNullOrEmpty(header))
                                    {
                                        values[header] = val;
                                        values[$"{currentGroup}.{header}"] = val;
                                    }
                                }
                            }
                            else
                            {
                                string varName = colA;
                                if (colA.Contains("{") && colA.Contains("}"))
                                {
                                    int start = colA.LastIndexOf('{');
                                    int end = colA.LastIndexOf('}');
                                    if (end > start)
                                    {
                                        varName = colA.Substring(start + 1, end - start - 1).Trim();
                                    }
                                }
                                if (!string.IsNullOrEmpty(varName))
                                {
                                    values[varName] = colB;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return values;
        }
    }
}

