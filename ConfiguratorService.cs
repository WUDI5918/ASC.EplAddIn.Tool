using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    public class ConfiguratorService
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
            Log("Info", $"å¼€å§‹ä¸ºå®è§„åˆ™ '{typical.Name}' ç”ŸæˆåŽŸç†å›¾...");

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

            if (activePage == null)
            {
                string errMsg = "é”™è¯¯ï¼šæ— æ³•èŽ·å–å½“å‰çš„ EPLAN é¡µé¢ï¼è¯·ç¡®ä¿æ‚¨å·²ç»åœ¨ EPLAN ä¸­æ‰“å¼€äº†ä¸€å¼ å›¾çº¸é¡µã€‚";
                Log("Error", errMsg);
                return errMsg;
            }

            Project activeProject = activePage.Project;
            Log("Info", $"å½“å‰æ¿€æ´»é¡¹ç›®: {activeProject.ProjectName}, æ¿€æ´»é¡µé¢: {activePage.Name}");

            // 2. Prompt user to enter values for all defined variables
            Dictionary<string, string> variableValues = new Dictionary<string, string>();
            if (typical.Variables != null && typical.Variables.Count > 0)
            {
                Log("Info", $"è¯¥è§„åˆ™åŒ…å« {typical.Variables.Count} ä¸ªé…ç½®å˜é‡ï¼Œæ­£åœ¨å±•ç¤ºå‚æ•°è¾“å…¥è¡¨å•...");
                bool? dialogResult = null;
                TypicalVariablesInputDialog inputDlg = null;
                
                inputDlg = new TypicalVariablesInputDialog(typical.Variables, typical.Name);
                dialogResult = inputDlg.ShowDialog();
                
                if (dialogResult != true)
                {
                    Log("Warning", "ç”¨æˆ·å–æ¶ˆäº†å‚æ•°é…ç½®ï¼Œä¸­æ­¢ç»˜å›¾æµç¨‹ã€‚");
                    return "å·²å–æ¶ˆç”Ÿæˆã€‚";
                }
                
                variableValues = inputDlg.VariableValues;
            }

            // Build typed parameter dictionary for NCalc
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
                        Log("Info", $"å‚æ•°è®¾å®š: {variable.Name} = {typedVal} (ç±»åž‹: {variable.Type})");
                    }
                    else
                    {
                        parameters[variable.Name] = null;
                        Log("Info", $"å‚æ•°è®¾å®š: {variable.Name} = null");
                    }
                }
            }

            // Load database to pass to GenerateTypicalInternal if needed
            ConfiguratorData db = new ConfiguratorData();
            try
            {
                string dbPath = Path.Combine(@"c:\Users\62789\source\repos\ASC.EplAddIn.Tool", "configurator_data.json");
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

            var initialPages = new HashSet<string>(activeProject.Pages.Select(p => p.Name));

            using (Transaction transaction = new TransactionManager().CreateTransaction())
            {
                try
                {
                    Eplan.EplApi.Base.Progress progress = new Eplan.EplApi.Base.Progress("æ­£åœ¨è‡ªåŠ¨ç”ŸæˆåŽŸç†å›¾...");
                    progress.ShowImmediately();

                    GenerateTypicalInternal(typical, db, parameters, activeProject, activePage, ref placedCount, ref placeholderUpdates, ref lastEmpPage);

                    progress.EndPart();
                    transaction.Commit();
                    
                    string successMsg = $"ç”Ÿæˆå®Œæˆï¼æˆåŠŸæ’å…¥äº† {placedCount} ä¸ªå®æ¨¡æ¿ï¼Œæ›´æ–°å¹¶åº”ç”¨äº† {placeholderUpdates} å¤„å ä½ç¬¦å˜é‡ã€‚";
                    Log("Success", successMsg);
                    return successMsg;
                }
                catch (Exception ex)
                {
                    transaction.Abort();
                    try
                    {
                        var pagesToDelete = activeProject.Pages.Where(p => !initialPages.Contains(p.Name)).ToList();
                        foreach (var p in pagesToDelete) p.Remove();
                    }
                    catch { }
                    string errMsg = $"é”™è¯¯ï¼šç”Ÿæˆå¤±è´¥ï¼Œé¡¹ç›®å·²å›žæ»šï¼\nå¼‚å¸¸ä¿¡æ¯: {ex.Message}";
                    Log("Error", errMsg);
                    throw;
                }
            }
        }

        // ==================== VALUE / FORMULA EVALUATION (NCalc) ====================

        public static object EvaluateExpression(string expressionStr, Dictionary<string, object> parameters)
        {
            if (string.IsNullOrWhiteSpace(expressionStr)) return null;

            try
            {
                string clean = expressionStr.Trim().TrimStart('=');
                
                NCalc.Expression e = new NCalc.Expression(clean);

                // Bind parameters
                foreach (var kvp in parameters)
                {
                    e.Parameters[kvp.Key] = kvp.Value;
                }

                // Register custom engineering functions
                e.EvaluateFunction += (name, args) =>
                {
                    if (name.Equals("Lookup_Breaker", StringComparison.OrdinalIgnoreCase))
                    {
                        double power = Convert.ToDouble(args.Parameters[0].Evaluate());
                        string startType = args.Parameters[1].Evaluate()?.ToString() ?? "";
                        args.Result = QueryBreakerCatalog(power, startType);
                    }
                    else if (name.Equals("Concat", StringComparison.OrdinalIgnoreCase))
                    {
                        string result = "";
                        foreach (var p in args.Parameters)
                        {
                            result += p.Evaluate()?.ToString();
                        }
                        args.Result = result;
                    }
                };

                return e.Evaluate();
            }
            catch (Exception ex)
            {
                throw new Exception($"è¡¨è¾¾å¼ '{expressionStr}' è§£æžå¤±è´¥: {ex.Message}", ex);
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
            catch
            {
                return true; // Fallback to true if evaluation fails
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
            catch
            {
                return formula; // Fallback to raw string
            }
        }

        // ==================== CSV CATALOG LOOKUP ====================

        private static string QueryBreakerCatalog(double power, string startType)
        {
            try
            {
                string catalogPath = Path.Combine(@"c:\Users\62789\source\repos\ASC.EplAddIn.Tool", "breaker_catalog.csv");
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

        // ==================== PAGE CREATION HELPER ====================

        private static Page GetOrCreatePage(Project project, List<MappingEntry> structures, Page fallbackPage, Dictionary<string, object> parameters, LibrarySettings settings)
        {
            if (structures == null || structures.Count == 0) return fallbackPage;

            try
            {
                string functionalAssignment = (settings == null || settings.AvailableStructureIdentifiers.Contains("==")) ? (structures.FirstOrDefault(s => s.Key == "==")?.Value ?? "") : "";
                string functionDesignation = (settings == null || settings.AvailableStructureIdentifiers.Contains("=")) ? (structures.FirstOrDefault(s => s.Key == "=")?.Value ?? "") : "";
                string locationDesignation = (settings == null || settings.AvailableStructureIdentifiers.Contains("+")) ? (structures.FirstOrDefault(s => s.Key == "+")?.Value ?? "") : "";
                string userDefined = (settings == null || settings.AvailableStructureIdentifiers.Contains("UserDefined")) ? (structures.FirstOrDefault(s => s.Key == "UserDefined")?.Value ?? "") : "";
                string pageName = (settings == null || settings.AvailableStructureIdentifiers.Contains("PageName")) ? (structures.FirstOrDefault(s => s.Key == "PageName")?.Value ?? "") : "";

                // Evaluate structure properties with formula engine
                functionalAssignment = EvaluateValueFormula(functionalAssignment, parameters);
                functionDesignation = EvaluateValueFormula(functionDesignation, parameters);
                locationDesignation = EvaluateValueFormula(locationDesignation, parameters);
                userDefined = EvaluateValueFormula(userDefined, parameters);
                pageName = EvaluateValueFormula(pageName, parameters);

                if (string.IsNullOrWhiteSpace(pageName))
                {
                    return fallbackPage;
                }

                // Format the page name: e.g. "=G1+L1/101"
                string fullPageName = "";
                if (!string.IsNullOrWhiteSpace(functionalAssignment)) fullPageName += "==" + functionalAssignment.Trim();
                if (!string.IsNullOrWhiteSpace(functionDesignation)) fullPageName += "=" + functionDesignation.Trim();
                if (!string.IsNullOrWhiteSpace(locationDesignation)) fullPageName += "+" + locationDesignation.Trim();
                if (!string.IsNullOrWhiteSpace(userDefined)) fullPageName += "#" + userDefined.Trim();
                fullPageName += "/" + pageName.Trim();

                // Find existing page
                foreach (Page p in project.Pages)
                {
                    if (p.Name.Equals(fullPageName, StringComparison.OrdinalIgnoreCase))
                    {
                        return p;
                    }
                }

                // Create page if it doesn't exist
                Page page = new Page();
                PagePropertyList pagePropList = new PagePropertyList();
                if (!string.IsNullOrWhiteSpace(functionalAssignment)) pagePropList.DESIGNATION_FUNCTIONALASSIGNMENT = functionalAssignment.Trim();
                if (!string.IsNullOrWhiteSpace(functionDesignation)) pagePropList.DESIGNATION_PLANT = functionDesignation.Trim();
                if (!string.IsNullOrWhiteSpace(locationDesignation)) pagePropList.DESIGNATION_LOCATION = locationDesignation.Trim();
                if (!string.IsNullOrWhiteSpace(userDefined)) pagePropList.DESIGNATION_USERDEFINED = userDefined.Trim();
                if (!string.IsNullOrWhiteSpace(pageName)) pagePropList.PAGE_COUNTER = pageName.Trim();

                page.Create(project, DocumentTypeManager.DocumentType.Circuit, pagePropList);
                return page;
            }
            catch
            {
                return fallbackPage;
            }
        }

        // ==================== VARIABLE USER INPUT DIALOG ====================

        private static Dictionary<string, string> PromptForVariables(List<ConfigurationVariable> variables)
        {
            Dictionary<string, string> values = new Dictionary<string, string>();
            foreach (var variable in variables)
            {
                string val = ShowInputBox(
                    $"è¯·è¾“å…¥å˜é‡ [{variable.Name}] çš„å€¼ï¼š\n(å˜é‡æ˜¾ç¤ºå: {variable.DisplayName})",
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
                Title = "é…ç½®å˜é‡ input - " + title,
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
                Content = "ç¡®å®š (OK)",
                Width = 90,
                Height = 28,
                HorizontalAlignment = HorizontalAlignment.Right,
                IsDefault = true,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(205, 55, 55)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0)
            };

            btn.Click += (s, e) => {
                result = txt.Text;
                prompt.DialogResult = true;
                prompt.Close();
            };

            sp.Children.Add(lbl);
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

        public static void LogDebug(string message)
        {
            try
            {
                string logFile = @"c:\Users\62789\source\repos\ASC.EplAddIn.Tool\debug_log.txt";
                File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\r\n");
            }
            catch { }
        }

        public static List<EplanMacroInfo> GetActiveProjectMacros()
        {
            LogDebug("--- GetActiveProjectMacros Invoked ---");
            var list = new List<EplanMacroInfo>();
            try
            {
                ProjectManager projectManager = new ProjectManager();
                projectManager.LockProjectByDefault = false;
                LogDebug("Created ProjectManager and set LockProjectByDefault to false.");
                
                Project[] openProjects = projectManager.OpenProjects;
                if (openProjects == null)
                {
                    LogDebug("openProjects is null!");
                    return list;
                }
                
                LogDebug($"openProjects length: {openProjects.Length}");
                
                foreach (Project project in openProjects)
                {
                    try
                    {
                        string projName = project.ProjectName;
                        LogDebug($"ProjectName: {projName}, FullName: {project.ProjectFullName}");
                        
                        int pageCount = 0;
                        foreach (Page page in project.Pages)
                        {
                            pageCount++;

                            // Fetch page placements only once to prevent expensive COM calls in nested loops
                            List<Eplan.EplApi.DataModel.Placement> allPlacementsList = null;
                            try
                            {
                                allPlacementsList = page.AllPlacements.OfType<Eplan.EplApi.DataModel.Placement>().ToList();
                            }
                            catch (Exception placementsEx)
                            {
                                LogDebug($"  Error traversing placements on page '{page.Name}': {placementsEx.Message}");
                                allPlacementsList = new List<Eplan.EplApi.DataModel.Placement>();
                            }

                            var allPlaceholders = allPlacementsList.OfType<Eplan.EplApi.DataModel.Graphics.PlaceHolder>().ToList();
                            var macroBoxes = allPlacementsList.OfType<Eplan.EplApi.DataModel.MacroBox>().ToList();
                            int totalBoxes = macroBoxes.Count;

                            // 1. Process page macro
                            try
                            {
                                var props = page.Properties;
                                string path = GetSafeProperty(props, Eplan.EplApi.DataModel.Properties.Page.PAGE_MACRO_NAME);
                                if (string.IsNullOrWhiteSpace(path))
                                {
                                    path = GetSafeProperty(props, 11910); // PAGE_MACRO
                                }

                                // Collect placeholders
                                if (!string.IsNullOrWhiteSpace(path))
                                {
                                    var placeholdersList = new List<string>();
                                    try
                                    {
                                        foreach (var ph in allPlaceholders)
                                        {
                                            if (ph != null && !string.IsNullOrEmpty(ph.Name))
                                            {
                                                string phName = ph.Name.Trim();
                                                if (phName.StartsWith("#<") && phName.EndsWith(">"))
                                                {
                                                    string varKey = phName.Substring(2, phName.Length - 3).Trim();
                                                    if (!string.IsNullOrEmpty(varKey))
                                                    {
                                                        placeholdersList.Add(varKey);
                                                    }
                                                }
                                                else if (ph.VariableNames != null)
                                                {
                                                    foreach (var varName in ph.VariableNames)
                                                    {
                                                        if (!string.IsNullOrEmpty(varName))
                                                        {
                                                            if (string.Equals(phName, "Page_Property", StringComparison.OrdinalIgnoreCase) ||
                                                                phName.StartsWith("Page_Property", StringComparison.OrdinalIgnoreCase))
                                                            {
                                                                placeholdersList.Add($"{phName} : {varName}");
                                                            }
                                                            else
                                                            {
                                                                placeholdersList.Add($"{phName}={varName}");
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception phEx)
                                    {
                                        LogDebug($"    Failed to query page placeholders: {phEx.Message}");
                                    }

                                    string pageDesc = GetSafeProperty(props, Eplan.EplApi.DataModel.Properties.Page.PAGE_MACRO_DESCRIPTION);
                                    if (string.IsNullOrEmpty(pageDesc))
                                    {
                                        pageDesc = GetSafeProperty(props, 11011);
                                    }

                                    list.Add(new EplanMacroInfo
                                    {
                                        ProjectName = projName,
                                        Name = page.Name,
                                        Description = pageDesc,
                                        MacroPath = path,
                                        IsPageMacro = true,
                                        VariantIndex = 0,
                                        RepresentationType = 1,
                                        Placeholders = placeholdersList
                                    });
                                }
                            }
                            catch (Exception pageEx)
                            {
                                LogDebug($"  Error on page '{page.Name}': {pageEx.Message}");
                            }

                            // 2. Traverse macro boxes
                            try
                            {
                                foreach (var box in macroBoxes)
                                {
                                    try
                                    {
                                        var boxProps = box.Properties;
                                        string path = GetSafeProperty(boxProps, Eplan.EplApi.DataModel.Properties.MacroBox.MACROBOX_MACRO);
                                        string name = GetSafeProperty(boxProps, Eplan.EplApi.DataModel.Properties.MacroBox.MACROBOX_NAME);
                                        string desc = GetSafeProperty(boxProps, Eplan.EplApi.DataModel.Properties.MacroBox.MACROBOX_MACRO_DESCRIPTION);
                                        int variant = GetSafeIntProperty(boxProps, Eplan.EplApi.DataModel.Properties.MacroBox.MACROBOX_VARIANT, 0);
                                        int repType = GetSafeIntProperty(boxProps, 23007, 1); // MACROBOX_REPRESENTATION_TYPE

                                        if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(path))
                                        {
                                            name = Path.GetFileNameWithoutExtension(path);
                                        }

                                        if (!string.IsNullOrEmpty(path))
                                        {
                                            LogDebug($"  MacroBox found: Name='{name}', Path='{path}', Variant={variant}, RepType={repType}");
                                            
                                            var placeholdersList = new List<string>();
                                            try
                                            {
                                                var boxPlaceholders = new List<Eplan.EplApi.DataModel.Graphics.PlaceHolder>();
                                                
                                                // 1. Try referenced placements
                                                var referenced = box.ReferencedPlacements;
                                                if (referenced != null)
                                                {
                                                    foreach (var p in referenced)
                                                    {
                                                        if (p is Eplan.EplApi.DataModel.Graphics.PlaceHolder ph && !boxPlaceholders.Contains(ph))
                                                        {
                                                            boxPlaceholders.Add(ph);
                                                        }
                                                    }
                                                }

                                                // 2. Try geometric containment
                                                double startX = 0, startY = 0, width = 0, height = 0;
                                                bool hasRect = false;
                                                
                                                string sx = GetSafeProperty(boxProps, Eplan.EplApi.DataModel.Properties.MacroBox.RECTANGLE_START_X);
                                                string sy = GetSafeProperty(boxProps, Eplan.EplApi.DataModel.Properties.MacroBox.RECTANGLE_START_Y);
                                                string w = GetSafeProperty(boxProps, Eplan.EplApi.DataModel.Properties.MacroBox.RECTANGLE_WIDTH);
                                                string h = GetSafeProperty(boxProps, Eplan.EplApi.DataModel.Properties.MacroBox.RECTANGLE_HEIGHT);

                                                hasRect = double.TryParse(sx, out startX) &&
                                                          double.TryParse(sy, out startY) &&
                                                          double.TryParse(w, out width) &&
                                                          double.TryParse(h, out height);

                                                if (hasRect && width != 0 && height != 0)
                                                {
                                                    double minX = Math.Min(startX, startX + width);
                                                    double maxX = Math.Max(startX, startX + width);
                                                    double minY = Math.Min(startY, startY + height);
                                                    double maxY = Math.Max(startY, startY + height);

                                                    foreach (var ph in allPlaceholders)
                                                    {
                                                        if (ph != null && !boxPlaceholders.Contains(ph))
                                                        {
                                                            var loc = ph.Location;
                                                            if (loc.X >= minX && loc.X <= maxX && loc.Y >= minY && loc.Y <= maxY)
                                                            {
                                                                boxPlaceholders.Add(ph);
                                                            }
                                                        }
                                                    }
                                                }

                                                // 3. Try page-wide fallback if box is the only macro box on the page
                                                if (boxPlaceholders.Count == 0)
                                                {
                                                    if (totalBoxes == 1)
                                                    {
                                                        foreach (var ph in allPlaceholders)
                                                        {
                                                            if (ph != null && !boxPlaceholders.Contains(ph))
                                                            {
                                                                boxPlaceholders.Add(ph);
                                                            }
                                                        }
                                                    }
                                                }

                                                // Process collected placeholders
                                                foreach (var ph in boxPlaceholders)
                                                {
                                                    if (ph != null && !string.IsNullOrEmpty(ph.Name))
                                                    {
                                                        string phName = ph.Name.Trim();
                                                        if (phName.StartsWith("#<") && phName.EndsWith(">"))
                                                        {
                                                            string varKey = phName.Substring(2, phName.Length - 3).Trim();
                                                            if (!string.IsNullOrEmpty(varKey))
                                                            {
                                                                placeholdersList.Add(varKey);
                                                            }
                                                        }
                                                        else if (ph.VariableNames != null)
                                                        {
                                                            foreach (var varName in ph.VariableNames)
                                                            {
                                                                if (!string.IsNullOrEmpty(varName))
                                                                {
                                                                    if (string.Equals(phName, "Page_Property", StringComparison.OrdinalIgnoreCase) ||
                                                                        phName.StartsWith("Page_Property", StringComparison.OrdinalIgnoreCase))
                                                                    {
                                                                        placeholdersList.Add($"{phName} : {varName}");
                                                                    }
                                                                    else
                                                                    {
                                                                        placeholdersList.Add($"{phName}={varName}");
                                                                    }
                                                                }
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                            catch (Exception refEx)
                                            {
                                                LogDebug($"    Failed to query box placeholders: {refEx.Message}");
                                            }

                                            list.Add(new EplanMacroInfo
                                            {
                                                ProjectName = projName,
                                                Name = name,
                                                Description = desc,
                                                MacroPath = path,
                                                IsPageMacro = false,
                                                VariantIndex = variant,
                                                RepresentationType = repType,
                                                Placeholders = placeholdersList
                                            });
                                        }
                                    }
                                    catch (Exception boxEx)
                                    {
                                        LogDebug($"    Error processing MacroBox: {boxEx.Message}");
                                    }
                                }
                            }
                            catch (Exception placementsEx)
                            {
                                LogDebug($"  Error traversing placements on page: {placementsEx.Message}");
                            }
                        }
                        LogDebug($"Project {projName} pages processed: {pageCount}");
                    }
                    catch (Exception projEx)
                    {
                        LogDebug($"Error processing project: {projEx.Message}");
                    }
                }

                // De-duplicate list by ProjectName + MacroPath + VariantIndex + RepresentationType + IsPageMacro
                list = list.GroupBy(m => new { m.ProjectName, m.MacroPath, m.VariantIndex, m.RepresentationType, m.IsPageMacro })
                           .Select(g => {
                               var first = g.First();
                               first.Placeholders = g.SelectMany(x => x.Placeholders).Distinct().ToList();
                               return first;
                           })
                           .ToList();
            }
            catch (Exception ex)
            {
                LogDebug($"Global error in GetActiveProjectMacros: {ex.Message}\n{ex.StackTrace}");
            }
            LogDebug($"GetActiveProjectMacros finished. Total macros found: {list.Count}");
            return list;
        }

        public static string GenerateConfigurator(Configurator configurator, ConfiguratorData db, Dictionary<string, Dictionary<string, object>> groupParameters)
        {
            GenerationLogs.Clear();
            Log("Info", $"å¼€å§‹ä¸ºé…ç½®å™¨ '{configurator.Name}' ç”ŸæˆåŽŸç†å›¾...");

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

            if (activePage == null)
            {
                string errMsg = "é”™è¯¯ï¼šæ— æ³•èŽ·å–å½“å‰çš„ EPLAN é¡µé¢ï¼è¯·ç¡®ä¿æ‚¨å·²ç»åœ¨ EPLAN ä¸­æ‰“å¼€äº†ä¸€å¼ å›¾çº¸é¡µã€‚";
                Log("Error", errMsg);
                return errMsg;
            }

            Project activeProject = activePage.Project;
            Log("Info", $"å½“å‰æ¿€æ´»é¡¹ç›®: {activeProject.ProjectName}, æ¿€æ´»é¡µé¢: {activePage.Name}");

            int placedCount = 0;
            int placeholderUpdates = 0;
            Page lastEmpPage = null;

            var initialPages = new HashSet<string>(activeProject.Pages.Select(p => p.Name));

            using (Transaction transaction = new TransactionManager().CreateTransaction())
            {
                try
                {
                    Eplan.EplApi.Base.Progress progress = new Eplan.EplApi.Base.Progress("æ­£åœ¨æ ¹æ®é…ç½®å™¨è‡ªåŠ¨ç”ŸæˆåŽŸç†å›¾...");
                    progress.ShowImmediately();

                    if (configurator.TypicalGroupRefs != null)
                    {
                        foreach (string groupRef in configurator.TypicalGroupRefs)
                        {
                            var group = db.TypicalGroups.FirstOrDefault(g => string.Equals(g.Name, groupRef, StringComparison.OrdinalIgnoreCase));
                            if (group == null) continue;

                            // Get group parameters from groupParameters input
                            Dictionary<string, object> parameters = null;
                            if (groupParameters != null && groupParameters.TryGetValue(group.Name, out var p))
                            {
                                parameters = p;
                            }
                            else
                            {
                                // Fallback: build default parameters
                                parameters = new Dictionary<string, object>();
                                foreach (var variable in group.Variables)
                                {
                                    if (variable.Type.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
                                        parameters[variable.Name] = false;
                                    else
                                        parameters[variable.Name] = null;
                                }
                            }

                            // Generate this group recursively
                            GenerateTypicalGroup(group, db, parameters, activeProject, activePage, ref placedCount, ref placeholderUpdates, ref lastEmpPage);
                        }
                    }

                    progress.EndPart();
                    transaction.Commit();

                    string successMsg = $"ç”Ÿæˆå®Œæˆï¼æˆåŠŸæ’å…¥äº† {placedCount} ä¸ªå®æ¨¡æ¿ï¼Œæ›´æ–°å¹¶åº”ç”¨äº† {placeholderUpdates} å¤„å ä½ç¬¦å˜é‡ã€‚";
                    Log("Success", successMsg);
                    return successMsg;
                }
                catch (Exception ex)
                {
                    transaction.Abort();
                    try
                    {
                        var pagesToDelete = activeProject.Pages.Where(p => !initialPages.Contains(p.Name)).ToList();
                        foreach (var p in pagesToDelete) p.Remove();
                    }
                    catch { }
                    string errMsg = $"é”™è¯¯ï¼šç”Ÿæˆå¤±è´¥ï¼Œé¡¹ç›®å·²å›žæ»šï¼\nå¼‚å¸¸ä¿¡æ¯: {ex.Message}";
                    Log("Error", errMsg);
                    throw;
                }
            }
        }

        public static void GenerateTypicalGroup(
            TypicalGroup group, 
            ConfiguratorData db, 
            Dictionary<string, object> parameters, 
            Project activeProject, 
            Page activePage, 
            ref int placedCount, 
            ref int placeholderUpdates, 
            ref Page lastEmpPage)
        {
            if (group.Instances == null) return;

            foreach (var instance in group.Instances)
            {
                // 1. Evaluate IsActiveFormula using parent parameters
                bool isActive = EvaluateActiveFormula(instance.IsActiveFormula, parameters);
                Log("Info", $"è¯„ä¼°å®žä¾‹ '{instance.TargetName}' æ¿€æ´»çŠ¶æ€: {isActive} (æ¡ä»¶: '{instance.IsActiveFormula}')");
                if (!isActive)
                {
                    continue; // Skip inactive instance
                }

                // 2. Build child parameters using variable mappings
                object childObj = null;
                List<ConfigurationVariable> childVars = null;
                if (instance.Type.Equals("MacroTypical", StringComparison.OrdinalIgnoreCase))
                {
                    var child = db.MacroTypicals.FirstOrDefault(t => string.Equals(t.Name, instance.TargetName, StringComparison.OrdinalIgnoreCase));
                    childObj = child;
                    childVars = child?.Variables;
                }
                else if (instance.Type.Equals("TypicalGroup", StringComparison.OrdinalIgnoreCase))
                {
                    var child = db.TypicalGroups.FirstOrDefault(g => string.Equals(g.Name, instance.TargetName, StringComparison.OrdinalIgnoreCase));
                    childObj = child;
                    childVars = child?.Variables;
                }

                if (childObj == null) continue;

                var childParameters = new Dictionary<string, object>();
                if (childVars != null)
                {
                    foreach (var variable in childVars)
                    {
                        var mapping = instance.VariableMappings?.FirstOrDefault(m => string.Equals(m.Key, variable.Name, StringComparison.OrdinalIgnoreCase));
                        if (mapping != null && !string.IsNullOrWhiteSpace(mapping.Value))
                        {
                            string formula = mapping.Value.Trim();
                            object evaluatedVal = null;
                            if (formula.StartsWith("="))
                            {
                                evaluatedVal = EvaluateExpression(formula, parameters);
                            }
                            else
                            {
                                evaluatedVal = formula; // Static value
                            }
                            
                            // Convert evaluated value to child's variable type
                            object typedVal = evaluatedVal;
                            Log("Info", $"  æ˜ å°„å˜é‡: {variable.Name} = '{typedVal}' (å…¬å¼: '{mapping.Value}')");
                            if (evaluatedVal != null)
                            {
                                string strVal = evaluatedVal.ToString();
                                if (variable.Type.Equals("Integer", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (int.TryParse(strVal, out int intVal)) typedVal = intVal;
                                }
                                else if (variable.Type.Equals("Double", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (double.TryParse(strVal, out double doubleVal)) typedVal = doubleVal;
                                }
                                else if (variable.Type.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (bool.TryParse(strVal, out bool boolVal)) typedVal = boolVal;
                                }
                            }
                            childParameters[variable.Name] = typedVal;
                        }
                        else
                        {
                            // Default value on missing mapping
                            if (variable.Type.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
                                childParameters[variable.Name] = false;
                            else
                                childParameters[variable.Name] = null;
                        }
                    }
                }

                // 3. Recurse or generate child
                if (childObj is TypicalGroup childGroup)
                {
                    GenerateTypicalGroup(childGroup, db, childParameters, activeProject, activePage, ref placedCount, ref placeholderUpdates, ref lastEmpPage);
                }
                else if (childObj is MacroTypical childTypical)
                {
                    GenerateTypicalInternal(childTypical, db, childParameters, activeProject, activePage, ref placedCount, ref placeholderUpdates, ref lastEmpPage);
                }
            }
        }

        public static void GenerateTypicalInternal(
            MacroTypical typical, 
            ConfiguratorData db, 
            Dictionary<string, object> parameters, 
            Project activeProject, 
            Page activePage, 
            ref int placedCount, 
            ref int placeholderUpdates, 
            ref Page lastEmpPage)
        {
            if (typical.Elements == null) return;

            foreach (var element in typical.Elements)
            {
                // Evaluate active condition
                bool isActive = EvaluateActiveFormula(element.IsActiveFormula, parameters);
                Log("Info", $"è¯„ä¼°å®å…ƒç´  '{element.MacroPath}' æ¿€æ´»çŠ¶æ€: {isActive} (æ¡ä»¶: '{element.IsActiveFormula}')");
                if (!isActive)
                {
                    continue; // Skip inactive macro element
                }

                // Resolve macro path
                string rawPath = element.MacroPath;
                if (string.IsNullOrWhiteSpace(rawPath)) continue;

                string resolvedPath = ResolveMacroPath(rawPath);
                Log("Info", $"è§£æžå®æ¨¡æ¿è·¯å¾„: '{resolvedPath}'");
                if (!System.IO.File.Exists(resolvedPath))
                {
                    Log("Error", $"å®æ¨¡æ¿æ–‡ä»¶ä¸å­˜åœ¨: '{resolvedPath}'");
                    throw new FileNotFoundException($"å®æ¨¡æ¿æ–‡ä»¶æœªæ‰¾åˆ°ï¼š{resolvedPath}");
                }

                bool isEmp = resolvedPath.EndsWith(".emp", StringComparison.OrdinalIgnoreCase);
                List<Page> pagesToProcess = new List<Page>();

                // Resolve target page based on structure mappings (only for window macros)
                Page targetPage = null;
                if (!isEmp)
                {
                    targetPage = GetOrCreatePage(activeProject, element.StructureMappings, lastEmpPage ?? activePage, parameters, db?.Settings);
                    if (targetPage == null) targetPage = lastEmpPage ?? activePage;
                }

                // Insert Macro
                if (isEmp)
                {
                    // Insert Page Macro
                    Log("Info", $"æ­£åœ¨æ’å…¥é¡µå®: '{resolvedPath}' åˆ°é¡¹ç›®");
                    
                    // Track pages before insertion
                    var oldPageNames = new HashSet<string>(activeProject.Pages.Select(p => p.Name));
                    
                    Insert oInsert = new Insert();
                    oInsert.PageMacro(resolvedPath, activePage, activeProject, true);
                    placedCount++;
                    Log("Success", $"æˆåŠŸæ’å…¥é¡µå®: '{resolvedPath}'");

                    // Find and rename newly inserted pages according to structure mappings
                    var newPages = activeProject.Pages.Where(p => !oldPageNames.Contains(p.Name)).ToList();
                    pagesToProcess.AddRange(newPages);

                    if (newPages.Count > 0)
                    {
                        Log("Info", $"æ£€æµ‹åˆ° {newPages.Count} ä¸ªæ–°é¡µå®é¡µé¢ï¼Œæ­£åœ¨åº”ç”¨ç»“æž„æ ‡è¯†ç¬¦...");
                        
                        string functionalAssignment = (db?.Settings == null || db.Settings.AvailableStructureIdentifiers.Contains("==")) ? (element.StructureMappings.FirstOrDefault(s => s.Key == "==")?.Value ?? "") : "";
                        string functionDesignation = (db?.Settings == null || db.Settings.AvailableStructureIdentifiers.Contains("=")) ? (element.StructureMappings.FirstOrDefault(s => s.Key == "=")?.Value ?? "") : "";
                        string locationDesignation = (db?.Settings == null || db.Settings.AvailableStructureIdentifiers.Contains("+")) ? (element.StructureMappings.FirstOrDefault(s => s.Key == "+")?.Value ?? "") : "";
                        string userDefined = (db?.Settings == null || db.Settings.AvailableStructureIdentifiers.Contains("UserDefined")) ? (element.StructureMappings.FirstOrDefault(s => s.Key == "UserDefined")?.Value ?? "") : "";
                        string pageNameBase = (db?.Settings == null || db.Settings.AvailableStructureIdentifiers.Contains("PageName")) ? (element.StructureMappings.FirstOrDefault(s => s.Key == "PageName")?.Value ?? "") : "";

                        functionalAssignment = EvaluateValueFormula(functionalAssignment, parameters);
                        functionDesignation = EvaluateValueFormula(functionDesignation, parameters);
                        locationDesignation = EvaluateValueFormula(locationDesignation, parameters);
                        userDefined = EvaluateValueFormula(userDefined, parameters);
                        pageNameBase = EvaluateValueFormula(pageNameBase, parameters);

                        for (int i = 0; i < newPages.Count; i++)
                        {
                            var p = newPages[i];
                            string targetPageName = pageNameBase;
                            if (newPages.Count > 1)
                            {
                                if (int.TryParse(pageNameBase, out int startNum))
                                {
                                    targetPageName = (startNum + i).ToString();
                                }
                                else
                                {
                                    targetPageName = pageNameBase + "_" + (i + 1);
                                }
                            }

                            Log("Info", $"  é‡å‘½åæ–°é¡µé¢: '{p.Name}' -> =={functionalAssignment}={functionDesignation}+{locationDesignation}#{userDefined}/{targetPageName}");
                            
                            try
                            {
                                if (!string.IsNullOrWhiteSpace(functionalAssignment)) 
                                    p.Properties[Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_FUNCTIONALASSIGNMENT] = functionalAssignment.Trim();
                                if (!string.IsNullOrWhiteSpace(functionDesignation)) 
                                    p.Properties[Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_PLANT] = functionDesignation.Trim();
                                if (!string.IsNullOrWhiteSpace(locationDesignation)) 
                                    p.Properties[Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_LOCATION] = locationDesignation.Trim();
                                if (!string.IsNullOrWhiteSpace(userDefined)) 
                                    p.Properties[Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_USERDEFINED] = userDefined.Trim();
                                if (!string.IsNullOrWhiteSpace(targetPageName))
                                    p.Properties[Eplan.EplApi.DataModel.Properties.Page.PAGE_COUNTER] = targetPageName.Trim();
                            }
                            catch (Exception renameEx)
                            {
                                Log("Warning", $"  é‡å‘½åé¡µé¢ {p.Name} å¤±è´¥: {renameEx.Message}");
                            }

                            // Record last page for sequential window macros
                            lastEmpPage = p;
                        }
                    }
                }
                else
                {
                    // Insert Window Macro
                    double scale = 1.0;
                    if (db?.Settings?.Unit != null && db.Settings.Unit.Equals("Imperial", StringComparison.OrdinalIgnoreCase))
                    {
                        scale = 25.4; // 1 inch = 25.4 mm
                    }

                    Insert oInsert = new Insert();
                    PointD insertPoint;
                    if (element.PositionMode != null && element.PositionMode.Equals("Sequential", StringComparison.OrdinalIgnoreCase))
                    {
                        double lastMaxX = 0.0;
                        var placements = targetPage.AllPlacements;
                        if (placements != null && placements.Length > 0)
                        {
                            foreach (var plc in placements)
                            {
                                if (plc != null && plc.Location.X > lastMaxX)
                                {
                                    lastMaxX = plc.Location.X;
                                }
                            }
                        }
                        // Place sequential macro relative to the right boundary with element.X as spacing offset (scaled)
                        insertPoint = new PointD(lastMaxX + (element.X * scale), element.Y * scale);
                    }
                    else
                    {
                        insertPoint = new PointD(element.X * scale, element.Y * scale);
                    }

                    Log("Info", $"æ­£åœ¨æ’å…¥çª—å£å®: '{resolvedPath}' (å˜ä½“: {element.VariantIndex}, å®šä½: {element.PositionMode}, X: {insertPoint.X:F2}, Y: {insertPoint.Y:F2})");
                    StorableObject[] insertedObjects = oInsert.WindowMacro(
                        resolvedPath,
                        element.VariantIndex,
                        targetPage,
                        insertPoint,
                        Insert.MoveKind.Relative
                    );
                    if (insertedObjects != null && insertedObjects.Length > 0)
                    {
                        placedCount++;
                        Log("Success", $"æˆåŠŸæ’å…¥çª—å£å®: '{resolvedPath}'");
                    }
                    else
                    {
                        Log("Warning", $"æ’å…¥çª—å£å®æ²¡æœ‰è¿”å›žä»»ä½•StorableObjects: '{resolvedPath}'");
                    }

                    if (targetPage != null)
                    {
                        pagesToProcess.Add(targetPage);
                    }
                }

                // Apply placeholders and value sets on all processed pages
                if (element.VariableMappings != null && element.VariableMappings.Count > 0 && pagesToProcess.Count > 0)
                {
                    foreach (Page page in pagesToProcess)
                    {
                        try
                        {
                            var placeHolders = page.AllPlacements.OfType<Eplan.EplApi.DataModel.Graphics.PlaceHolder>();
                            foreach (var placeHolder in placeHolders)
                            {
                                try
                                {
                                    string phName = placeHolder.Name;
                                    if (string.IsNullOrEmpty(phName)) continue;

                                    string activeRecord = "ConfiguredRecord";
                                    var recordsList = placeHolder.GetRecordNames();
                                    if (recordsList != null && recordsList.Count > 0)
                                    {
                                        activeRecord = recordsList[0];
                                    }

                                    // Handle preset TargetValueSet mapping (if exists)
                                    var valSetMapping = element.VariableMappings.FirstOrDefault(m =>
                                        m.Key.Equals("TargetValueSet", StringComparison.OrdinalIgnoreCase) ||
                                        m.Key.Equals(phName, StringComparison.OrdinalIgnoreCase) ||
                                        m.Key.Equals($"{phName} : TargetValueSet", StringComparison.OrdinalIgnoreCase) ||
                                        m.Key.Equals($"{phName}=TargetValueSet", StringComparison.OrdinalIgnoreCase));

                                    if (valSetMapping != null && !string.IsNullOrWhiteSpace(valSetMapping.Value))
                                    {
                                        string targetSetName = EvaluateValueFormula(valSetMapping.Value, parameters);
                                        Log("Info", $"  å ä½ç¬¦ '{phName}' è¯„ä¼°å€¼é›†å…¬å¼: '{valSetMapping.Value}' -> '{targetSetName}'");
                                        if (!string.IsNullOrWhiteSpace(targetSetName))
                                        {
                                            if (recordsList != null && recordsList.Contains(targetSetName))
                                            {
                                                placeHolder.ApplyRecord(targetSetName);
                                                activeRecord = targetSetName;
                                                placeholderUpdates++;
                                                Log("Success", $"  å·²æˆåŠŸåº”ç”¨å€¼é›†: '{targetSetName}' åˆ°å ä½ç¬¦ '{phName}'");
                                            }
                                            else
                                            {
                                                Log("Warning", $"  å€¼é›† '{targetSetName}' åœ¨å ä½ç¬¦ '{phName}' ä¸­ä¸å­˜åœ¨");
                                            }
                                        }
                                    }

                                    bool isSpecial = phName.StartsWith("#<") && phName.EndsWith(">");
                                    bool hasCustomValues = false;

                                    if (isSpecial)
                                    {
                                        string varKey = phName.Substring(2, phName.Length - 3).Trim();
                                        var mapping = element.VariableMappings.FirstOrDefault(m =>
                                            m.Key.Equals(varKey, StringComparison.OrdinalIgnoreCase));

                                        if (mapping != null && !string.IsNullOrWhiteSpace(mapping.Value))
                                        {
                                            string evaluatedValue = EvaluateValueFormula(mapping.Value, parameters);

                                            if (recordsList == null || !recordsList.Contains(activeRecord))
                                            {
                                                placeHolder.AddRecord(activeRecord);
                                            }

                                            Eplan.EplApi.Base.MultiLangString mlsValue = new Eplan.EplApi.Base.MultiLangString();
                                            mlsValue.SetAsString(evaluatedValue);

                                            if (placeHolder.VariableNames != null)
                                            {
                                                foreach (string varName in placeHolder.VariableNames)
                                                {
                                                    placeHolder.SetValue(activeRecord, varName, mlsValue);
                                                    Log("Success", $"  æ›´æ–°å ä½ç¬¦ '{phName}' å˜é‡ '{varName}' -> '{evaluatedValue}' (å€¼é›†: '{activeRecord}')");
                                                }
                                            }
                                            hasCustomValues = true;
                                        }
                                    }
                                    else
                                    {
                                        if (placeHolder.VariableNames != null)
                                        {
                                            foreach (string varName in placeHolder.VariableNames)
                                            {
                                                string mappingKeyColon = $"{phName} : {varName}";
                                                string mappingKeyEqual = $"{phName}={varName}";
                                                var mapping = element.VariableMappings.FirstOrDefault(m =>
                                                    m.Key.Equals(mappingKeyColon, StringComparison.OrdinalIgnoreCase) ||
                                                    m.Key.Equals(mappingKeyEqual, StringComparison.OrdinalIgnoreCase));

                                                if (mapping != null && !string.IsNullOrWhiteSpace(mapping.Value))
                                                {
                                                    string evaluatedValue = EvaluateValueFormula(mapping.Value, parameters);

                                                    if (recordsList == null || !recordsList.Contains(activeRecord))
                                                    {
                                                        placeHolder.AddRecord(activeRecord);
                                                    }

                                                    Eplan.EplApi.Base.MultiLangString mlsValue = new Eplan.EplApi.Base.MultiLangString();
                                                    mlsValue.SetAsString(evaluatedValue);
                                                    placeHolder.SetValue(activeRecord, varName, mlsValue);
                                                    hasCustomValues = true;
                                                }
                                            }
                                        }
                                    }

                                    if (hasCustomValues)
                                    {
                                        placeHolder.ApplyRecord(activeRecord);
                                        placeholderUpdates++;
                                    }
                                }
                                catch (Exception phEx)
                                {
                                    Log("Error", $"  æ›´æ–°å ä½ç¬¦ '{placeHolder.Name}' å¤±è´¥: {phEx.Message}");
                                }
                            }
                        }
                        catch (Exception pageEx)
                        {
                            Log("Error", $"  å¤„ç†é¡µé¢å ä½ç¬¦ '{page.Name}' å¤±è´¥: {pageEx.Message}");
                        }
                    }
                }
            }
        }

        public static List<string> ExtractPlaceholdersFromMacroFile(string macroPath, int variantIndex)
        {
            List<string> placeholdersList = new List<string>();
            try
            {
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
                if (activePage == null)
                {
                    LogDebug("ExtractPlaceholdersFromMacroFile: No active page found.");
                    return placeholdersList;
                }

                Project activeProject = activePage.Project;
                string resolvedPath = ResolveMacroPath(macroPath);
                if (!System.IO.File.Exists(resolvedPath))
                {
                    LogDebug($"ExtractPlaceholdersFromMacroFile: File not found {resolvedPath}");
                    return placeholdersList;
                }

                bool isEmp = resolvedPath.EndsWith(".emp", StringComparison.OrdinalIgnoreCase);

                using (Transaction transaction = new TransactionManager().CreateTransaction())
                {
                    try
                    {
                        if (isEmp)
                        {
                            var oldPageNames = new HashSet<string>(activeProject.Pages.Select(p => p.Name));
                            Insert oInsert = new Insert();
                            oInsert.PageMacro(resolvedPath, activePage, activeProject, true);
                            
                            foreach (Page p in activeProject.Pages)
                            {
                                if (!oldPageNames.Contains(p.Name))
                                {
                                    var phs = p.AllPlacements.OfType<Eplan.EplApi.DataModel.Graphics.PlaceHolder>();
                                    foreach (var ph in phs)
                                    {
                                        AddPlaceholderToList(ph, placeholdersList);
                                    }
                                }
                            }
                        }
                        else
                        {
                            Page tempPage = new Page();
                            PagePropertyList pagePropList = new PagePropertyList();
                            pagePropList.PAGE_COUNTER = "TEMP_EXTRACT_9999";
                            tempPage.Create(activeProject, DocumentTypeManager.DocumentType.Circuit, pagePropList);

                            Insert oInsert = new Insert();
                            PointD insertPoint = new PointD(0, 0);
                            oInsert.WindowMacro(resolvedPath, variantIndex, tempPage, insertPoint, Insert.MoveKind.Relative);

                            var phs = tempPage.AllPlacements.OfType<Eplan.EplApi.DataModel.Graphics.PlaceHolder>();
                            foreach (var ph in phs)
                            {
                                AddPlaceholderToList(ph, placeholdersList);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"Error placing macro in ExtractPlaceholdersFromMacroFile: {ex.Message}\n{ex.StackTrace}");
                    }
                    finally
                    {
                        transaction.Abort(); // ALWAYS ROLLBACK!
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Global error in ExtractPlaceholdersFromMacroFile: {ex.Message}\n{ex.StackTrace}");
            }
            return placeholdersList.Distinct().ToList();
        }

        private static void AddPlaceholderToList(Eplan.EplApi.DataModel.Graphics.PlaceHolder ph, List<string> list)
        {
            if (ph == null || string.IsNullOrEmpty(ph.Name)) return;
            string phName = ph.Name.Trim();
            if (phName.StartsWith("#<") && phName.EndsWith(">"))
            {
                string varKey = phName.Substring(2, phName.Length - 3).Trim();
                if (!string.IsNullOrEmpty(varKey))
                {
                    list.Add(varKey);
                }
            }
            else if (ph.VariableNames != null)
            {
                foreach (var varName in ph.VariableNames)
                {
                    if (!string.IsNullOrEmpty(varName))
                    {
                        if (string.Equals(phName, "Page_Property", StringComparison.OrdinalIgnoreCase) ||
                            phName.StartsWith("Page_Property", StringComparison.OrdinalIgnoreCase))
                        {
                            list.Add($"{phName} : {varName}");
                        }
                        else
                        {
                            list.Add($"{phName}={varName}");
                        }
                    }
                }
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

                NCalc.Expression e = new NCalc.Expression(clean);
                if (e.HasErrors())
                {
                    return e.Error;
                }

                var matches = System.Text.RegularExpressions.Regex.Matches(clean, @"[a-zA-Z_][a-zA-Z0-9_]*");
                var keywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "and", "or", "not", "true", "false", "null", "if", "in",
                    "Abs", "Acos", "Asin", "Atan", "Ceiling", "Cos", "Exp", "Floor", 
                    "IEEERemainder", "Log", "Log10", "Max", "Min", "Pow", "Round", 
                    "Sign", "Sin", "Sqrt", "Tan", "Truncate", "Concat", "Lookup_Breaker"
                };

                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    string word = match.Value;
                    if (keywords.Contains(word)) continue;
                    
                    if (availableVariables == null || !availableVariables.Contains(word))
                    {
                        return $"æœªå®šä¹‰çš„å˜é‡: '{word}'";
                    }
                }
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
    }

    public class EplanMacroInfo
    {
        public string ProjectName { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string MacroPath { get; set; }
        public bool IsPageMacro { get; set; }
        public int VariantIndex { get; set; }
        public int RepresentationType { get; set; }
        public List<string> Placeholders { get; set; } = new List<string>();
    }
}