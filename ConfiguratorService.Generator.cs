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
    public partial class ConfiguratorService
    {
        private static Dictionary<string, (double MinX, double MinY, double MaxX, double MaxY)> _pageBoundingBoxes = new Dictionary<string, (double, double, double, double)>(StringComparer.OrdinalIgnoreCase);

        public static void CreateAndActivateProject(string templatePath, string targetPath)
        {
            try
            {
                Log("Info", $"准备基于模板 '{templatePath}' 创建新项目: '{targetPath}'");
                ProjectManager pm = new ProjectManager();
                
                // If project is already open, try to close it
                var openProjects = pm.OpenProjects;
                foreach (Project p in openProjects)
                {
                    if (p.ProjectFullName.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                    {
                        Log("Warning", $"目标项目已经在工作区中打开，准备关闭: {p.ProjectName}");
                        p.Close();
                    }
                }
                
                // Note: According to EPLAN API, CreateProject might take just the path or use Action calling.
                // Assuming standard EPLAN API: CreateProject(string strProjectFile, string strTemplateFile)
                // If it fails, fallback to ActionCalling
                Project newProject = null;
                try
                {
                    newProject = pm.CreateProject(targetPath, templatePath);
                }
                catch (Exception createEx)
                {
                    Log("Warning", $"CreateProject直接调用失败: {createEx.Message}, 尝试使用 Action...");
                    var am = new Eplan.EplApi.ApplicationFramework.ActionManager();
                    var action = am.FindAction("XPrjActionProjectCreate");
                    if (action != null)
                    {
                        var ctx = new Eplan.EplApi.ApplicationFramework.ActionCallingContext();
                        ctx.AddParameter("ProjectName", targetPath);
                        ctx.AddParameter("Template", templatePath);
                        action.Execute(ctx);
                        
                        // Find the newly created project
                        openProjects = pm.OpenProjects;
                        foreach (Project p in openProjects)
                        {
                            if (p.ProjectFullName.Equals(targetPath, StringComparison.OrdinalIgnoreCase))
                            {
                                newProject = p;
                                break;
                            }
                        }
                    }
                }

                if (newProject != null)
                {
                    Log("Success", $"新项目创建成功并已激活: {newProject.ProjectName}");
                }
                else
                {
                    throw new Exception("创建项目失败或无法在工作区中找到该项目。");
                }
            }
            catch (Exception ex)
            {
                Log("Error", $"创建新项目失败: {ex.Message}");
                throw new Exception($"创建新项目失败: {ex.Message}", ex);
            }
        }

        public static string GenerateConfigurator(Configurator configurator, ConfiguratorData db, Dictionary<string, Dictionary<string, object>> groupParameters)
        {
            GenerationLogs.Clear();
            Log("Info", $"开始为配置器 '{configurator.Name}' 生成原理图...");

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

            int placedCount = 0;
            int placeholderUpdates = 0;
            Page lastEmpPage = null;

            ClearedPages.Clear();
            _pageBoundingBoxes.Clear();

            var initialPages = new HashSet<string>(activeProject.Pages.Select(p => p.Name));

            using (Transaction transaction = new TransactionManager().CreateTransaction())
            {
                try
                {
                    Eplan.EplApi.Base.Progress progress = new Eplan.EplApi.Base.Progress("正在根据配置器自动生成原理图...");
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

            // R4: Validate top-level group's own variables
            ValidateVariables(group.Name, group.Variables, parameters);

            foreach (var instance in group.Instances)
            {
                // 1. Evaluate IsActiveFormula using parent parameters
                bool isActive = EvaluateActiveFormula(instance.IsActiveFormula, parameters);
                Log("Info", $"评估实例 '{instance.TargetName}' 激活状态: {isActive} (条件: '{instance.IsActiveFormula}')");
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
                    var evalContext = new Dictionary<string, object>(parameters ?? new Dictionary<string, object>());
                    foreach (var variable in childVars)
                    {
                        var mapping = instance.VariableMappings?.FirstOrDefault(m => string.Equals(m.Key, variable.Name, StringComparison.OrdinalIgnoreCase));
                        object evaluatedVal = null;
                        if (mapping != null && !string.IsNullOrWhiteSpace(mapping.Value))
                        {
                            string formula = mapping.Value.Trim();
                            if (formula.StartsWith("="))
                            {
                                evaluatedVal = EvaluateExpression(formula, evalContext);
                            }
                            else
                            {
                                evaluatedVal = formula; // Static value
                            }
                        }
                        else
                        {
                            // Fallback to the variable's DefaultValue
                            string defaultValue = variable.DefaultValue ?? "";
                            if (defaultValue.StartsWith("="))
                            {
                                evaluatedVal = EvaluateExpression(defaultValue, evalContext);
                            }
                            else
                            {
                                evaluatedVal = defaultValue;
                            }
                        }

                        // Convert evaluated value to child's variable type
                        object typedVal = ConvertToVariableType(evaluatedVal, variable.Type);
                        Log("Info", $"  映射变量: {variable.Name} = '{typedVal}' (公式/默认值: '{(mapping != null ? mapping.Value : variable.DefaultValue)}')");
                        childParameters[variable.Name] = typedVal;
                        evalContext[variable.Name] = typedVal;
                    }

                    // Validate visible mandatory variables in child parameter context
                    ValidateVariables(instance.TargetName, childVars, childParameters);
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

            // Merge typical variables with default values, and validate visible mandatory ones
            var mergedParameters = new Dictionary<string, object>(parameters ?? new Dictionary<string, object>());
            if (typical.Variables != null)
            {
                foreach (var variable in typical.Variables)
                {
                    if (!mergedParameters.ContainsKey(variable.Name) || mergedParameters[variable.Name] == null)
                    {
                        string defVal = variable.DefaultValue ?? "";
                        object evaluatedVal = defVal;
                        if (defVal.StartsWith("="))
                        {
                            evaluatedVal = EvaluateExpression(defVal, mergedParameters);
                        }
                        mergedParameters[variable.Name] = ConvertToVariableType(evaluatedVal, variable.Type);
                    }
                }

                ValidateVariables(typical.Name, typical.Variables, mergedParameters);
            }
            parameters = mergedParameters;

            ExecuteScriptSteps(db.Scripts, "BeforeGenerate", parameters, LogDebug);

            foreach (var element in typical.Elements)
            {
                // Evaluate active condition
                bool isActive = EvaluateActiveFormula(element.IsActiveFormula, parameters);
                Log("Info", $"评估宏元素 '{element.MacroPath}' 激活状态: {isActive} (条件: '{element.IsActiveFormula}')");
                if (!isActive)
                {
                    continue; // Skip inactive macro element
                }

                // Resolve macro path
                string rawPath = element.MacroPath;
                if (string.IsNullOrWhiteSpace(rawPath)) continue;

                string resolvedPath = ResolveMacroPath(rawPath);
                Log("Info", $"解析宏模板路径: '{resolvedPath}'");

                // PrePlanning macro: only log configuration, skip actual insertion
                if (element.IsPrePlanning)
                {
                    LogDebug($"PrePlanning macro (placeholder only): '{resolvedPath}' — skipping actual insertion.");
                    continue;
                }

                if (!System.IO.File.Exists(resolvedPath))
                {
                    Log("Error", $"宏模板文件不存在: '{resolvedPath}'");
                    throw new FileNotFoundException($"宏模板文件未找到：{resolvedPath}");
                }

                bool isEmp = resolvedPath.EndsWith(".emp", StringComparison.OrdinalIgnoreCase);
                bool isEms = resolvedPath.EndsWith(".ems", StringComparison.OrdinalIgnoreCase);
                List<Page> pagesToProcess = new List<Page>();
                StorableObject[] insertedObjects = null;

                // Resolve target page based on structure mappings (only for window/page macros, not symbol macros)
                Page targetPage = null;
                if (!isEmp && !isEms)
                {
                    targetPage = GetOrCreatePage(activeProject, element.StructureMappings, lastEmpPage ?? activePage, parameters, db?.Settings);
                    if (targetPage == null) 
                    {
                        throw new InvalidOperationException($"无法创建或解析页面结构标识符，中止生成 (宏模板: '{resolvedPath}')");
                    }
                }

                // Insert Macro
                if (isEms)
                {
                    // Symbol macro (.ems) processing
                    // EPLAN API does not provide a direct programmatic SymbolMacro insert method.
                    // We recognize the file, log it, and skip insertion gracefully.
                    try
                    {
                        LogDebug($"Symbol macro (.ems) recognized: '{resolvedPath}'");
                        LogDebug($"Symbol macro (.ems) processing: '{resolvedPath}' — EPLAN API does not support programmatic symbol macro insertion. Skipping.");
                        Log("Info", $"符号宏 (.ems) 已识别: '{resolvedPath}'，当前 EPLAN API 不支持符号宏程序化插入，已跳过。");
                    }
                    catch (Exception emsEx)
                    {
                        LogDebug($"Symbol macro (.ems) processing error: {emsEx.Message}");
                        Log("Warning", $"符号宏处理异常: {emsEx.Message}");
                    }
                }
                else if (isEmp)
                {
                    Log("Info", $"正在插入页宏: '{resolvedPath}' 到项目");

                    using (Eplan.EplApi.DataModel.MasterData.PageMacro pm = new Eplan.EplApi.DataModel.MasterData.PageMacro())
                    {
                        pm.Open(resolvedPath, activeProject);
                        var pages = pm.Pages;
                        if (pages != null && pages.Length > 0)
                        {
                            string functionalAssignment = (db?.Settings == null || db.Settings.AvailableStructureIdentifiers == null || db.Settings.AvailableStructureIdentifiers.Contains("==")) ? (element.StructureMappings.FirstOrDefault(s => s.Key == "==")?.Value ?? "") : "";
                            string functionDesignation = (db?.Settings == null || db.Settings.AvailableStructureIdentifiers == null || db.Settings.AvailableStructureIdentifiers.Contains("=")) ? (element.StructureMappings.FirstOrDefault(s => s.Key == "=")?.Value ?? "") : "";
                            string locationDesignation = (db?.Settings == null || db.Settings.AvailableStructureIdentifiers == null || db.Settings.AvailableStructureIdentifiers.Contains("+")) ? (element.StructureMappings.FirstOrDefault(s => s.Key == "+")?.Value ?? "") : "";
                            string documentType = (db?.Settings == null || db.Settings.AvailableStructureIdentifiers == null || db.Settings.AvailableStructureIdentifiers.Contains("&")) ? (element.StructureMappings.FirstOrDefault(s => s.Key == "&")?.Value ?? "") : "";
                            string userDefined = (db?.Settings == null || db.Settings.AvailableStructureIdentifiers == null || db.Settings.AvailableStructureIdentifiers.Contains("UserDefined") || db.Settings.AvailableStructureIdentifiers.Contains("#")) ? (element.StructureMappings.FirstOrDefault(s => s.Key == "UserDefined" || s.Key == "#")?.Value ?? "") : "";
                            string pageNameBase = (db?.Settings == null || db.Settings.AvailableStructureIdentifiers == null || db.Settings.AvailableStructureIdentifiers.Contains("PageName")) ? (element.StructureMappings.FirstOrDefault(s => s.Key == "PageName")?.Value ?? "") : "";

                            functionalAssignment = EvaluateValueFormula(functionalAssignment, parameters);
                            functionDesignation = EvaluateValueFormula(functionDesignation, parameters);
                            locationDesignation = EvaluateValueFormula(locationDesignation, parameters);
                            documentType = EvaluateValueFormula(documentType, parameters);
                            userDefined = EvaluateValueFormula(userDefined, parameters);
                            pageNameBase = EvaluateValueFormula(pageNameBase, parameters);

                            for (int i = 0; i < pages.Length; i++)
                            {
                                var p = pages[i];
                                string targetPageName = pageNameBase;
                                if (pages.Length > 1)
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

                                // Delete existing page in target project to prevent overwrite collisions
                                string fullTargetPageName = FormatPageName(functionalAssignment, functionDesignation, locationDesignation, documentType, userDefined, targetPageName);

                                if (!ClearedPages.Contains(fullTargetPageName))
                                {
                                    Page existingTarget = activeProject.Pages.FirstOrDefault(pageObj => pageObj.Name.Equals(fullTargetPageName, StringComparison.OrdinalIgnoreCase));
                                    if (existingTarget != null)
                                    {
                                        Log("Info", $"  检测到目标页面已存在 '{fullTargetPageName}'，正在清理以重新生成...");
                                        try
                                        {
                                            existingTarget.Remove();
                                        }
                                        catch (Exception remEx)
                                        {
                                            Log("Warning", $"  清理页面 '{fullTargetPageName}' 失败: {remEx.Message}");
                                        }
                                    }
                                    ClearedPages.Add(fullTargetPageName);
                                }

                                try
                                {
                                    if (!string.IsNullOrWhiteSpace(functionalAssignment))
                                        p.Properties[Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_FUNCTIONALASSIGNMENT] = functionalAssignment.Trim();
                                    if (!string.IsNullOrWhiteSpace(functionDesignation))
                                        p.Properties[Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_PLANT] = functionDesignation.Trim();
                                    if (!string.IsNullOrWhiteSpace(locationDesignation))
                                        p.Properties[Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_LOCATION] = locationDesignation.Trim();
                                    if (!string.IsNullOrWhiteSpace(documentType))
                                        p.Properties[Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_DOCTYPE] = documentType.Trim();
                                    if (!string.IsNullOrWhiteSpace(userDefined))
                                        p.Properties[Eplan.EplApi.DataModel.Properties.Page.DESIGNATION_USERDEFINED] = userDefined.Trim();
                                    if (!string.IsNullOrWhiteSpace(targetPageName))
                                        p.Properties[Eplan.EplApi.DataModel.Properties.Page.PAGE_COUNTER] = targetPageName.Trim();
                                }
                                catch (Exception propEx)
                                {
                                    Log("Warning", $"  在内存中设置页宏页面属性失败: {propEx.Message}");
                                }
                            }

                            Insert oInsert = new Insert();
                            oInsert.PageMacro(pm, activePage, activeProject, true);
                            placedCount++;
                            Log("Success", $"成功插入页宏: '{resolvedPath}'");

                            // Re-find target pages in active project to apply placeholders
                            foreach (var p in pm.Pages)
                            {
                                string targetPageName = pageNameBase;
                                if (pages.Length > 1)
                                {
                                    int index = Array.IndexOf(pages, p);
                                    if (int.TryParse(pageNameBase, out int startNum))
                                    {
                                        targetPageName = (startNum + index).ToString();
                                    }
                                    else
                                    {
                                        targetPageName = pageNameBase + "_" + (index + 1);
                                    }
                                }

                                string fullTargetPageName = FormatPageName(functionalAssignment, functionDesignation, locationDesignation, documentType, userDefined, targetPageName);

                                Page insertedPage = activeProject.Pages.FirstOrDefault(pageObj => pageObj.Name.Equals(fullTargetPageName, StringComparison.OrdinalIgnoreCase));
                                if (insertedPage != null)
                                {
                                    pagesToProcess.Add(insertedPage);
                                    lastEmpPage = insertedPage;
                                }
                            }
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
                        double offsetVal = 0;
                        string offsetStr = element.Offset ?? "0";
                        if (offsetStr.StartsWith("="))
                        {
                            try
                            {
                                var result = EvaluateValueFormula(offsetStr, parameters);
                                if (double.TryParse(result?.ToString(), out double formulaOffset))
                                    offsetVal = formulaOffset;
                            }
                            catch { offsetVal = 0; }
                        }
                        else if (!double.TryParse(offsetStr, out offsetVal))
                        {
                            offsetVal = 0;
                        }

                        if (_pageBoundingBoxes.TryGetValue(targetPage.Name, out var bounds))
                        {
                            string dir = element.Direction ?? "To the right";
                            if (dir.Equals("To the right", StringComparison.OrdinalIgnoreCase))
                                insertPoint = new PointD(bounds.MaxX + (offsetVal * scale), bounds.MinY);
                            else if (dir.Equals("To the left", StringComparison.OrdinalIgnoreCase))
                                insertPoint = new PointD(bounds.MinX - (offsetVal * scale), bounds.MinY);
                            else if (dir.Equals("Up", StringComparison.OrdinalIgnoreCase))
                                insertPoint = new PointD(bounds.MinX, bounds.MaxY + (offsetVal * scale));
                            else if (dir.Equals("Down", StringComparison.OrdinalIgnoreCase))
                                insertPoint = new PointD(bounds.MinX, bounds.MinY - (offsetVal * scale));
                            else
                                insertPoint = new PointD(bounds.MaxX + (offsetVal * scale), bounds.MinY);
                        }
                        else
                        {
                            // Fallback if no previous macro on this page in this run
                            insertPoint = new PointD(element.X * scale, element.Y * scale);
                        }
                    }
                    else
                    {
                        insertPoint = new PointD(element.X * scale, element.Y * scale);
                    }

                    // Apply XFormula/YFormula overrides if present
                    double elementX = insertPoint.X;
                    double elementY = insertPoint.Y;
                    if (!string.IsNullOrEmpty(element.XFormula))
                    {
                        try
                        {
                            var result = EvaluateValueFormula(element.XFormula, parameters);
                            if (double.TryParse(result?.ToString(), out double formulaX))
                                elementX = formulaX;
                        }
                        catch { /* formula invalid, keep original X */ }
                    }
                    if (!string.IsNullOrEmpty(element.YFormula))
                    {
                        try
                        {
                            var result = EvaluateValueFormula(element.YFormula, parameters);
                            if (double.TryParse(result?.ToString(), out double formulaY))
                                elementY = formulaY;
                        }
                        catch { /* formula invalid, keep original Y */ }
                    }
                    insertPoint = new PointD(elementX, elementY);

                    Insert.MoveKind moveKind = Insert.MoveKind.Relative;
                    if (element.PositionMode != null && element.PositionMode.Equals("Absolute", StringComparison.OrdinalIgnoreCase))
                    {
                        moveKind = Insert.MoveKind.Absolute;
                    }

                    if (element.RepresentationType > 0)
                    {
                        Eplan.EplApi.DataModel.MasterData.WindowMacro.Enums.RepresentationType repType =
                            (Eplan.EplApi.DataModel.MasterData.WindowMacro.Enums.RepresentationType)element.RepresentationType;

                        Log("Info", $"正在插入窗口宏: '{resolvedPath}' (变体: {element.VariantIndex}, 表示类型: {repType}, 定位: {element.PositionMode}, X: {insertPoint.X:F2}, Y: {insertPoint.Y:F2})");
                        insertedObjects = oInsert.WindowMacro(
                            resolvedPath,
                            repType,
                            element.VariantIndex,
                            targetPage,
                            insertPoint,
                            moveKind
                        );
                    }
                    else
                    {
                        Log("Info", $"正在插入窗口宏 (默认表示类型): '{resolvedPath}' (变体: {element.VariantIndex}, 定位: {element.PositionMode}, X: {insertPoint.X:F2}, Y: {insertPoint.Y:F2})");
                        insertedObjects = oInsert.WindowMacro(
                            resolvedPath,
                            element.VariantIndex,
                            targetPage,
                            insertPoint,
                            moveKind
                        );
                    }
                    if (insertedObjects != null && insertedObjects.Length > 0)
                    {
                        placedCount++;
                        Log("Success", $"成功插入窗口宏: '{resolvedPath}'");

                        // Calculate Bounding Box of inserted objects
                        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                        bool hasBounds = false;
                        foreach (var obj in insertedObjects)
                        {
                            if (obj is Placement plc)
                            {
                                if (plc.Location.X < minX) minX = plc.Location.X;
                                if (plc.Location.Y < minY) minY = plc.Location.Y;
                                if (plc.Location.X > maxX) maxX = plc.Location.X;
                                if (plc.Location.Y > maxY) maxY = plc.Location.Y;
                                hasBounds = true;
                            }
                        }

                        if (hasBounds)
                        {
                            if (_pageBoundingBoxes.TryGetValue(targetPage.Name, out var existingBounds))
                            {
                                // Expand bounds if already exists (or just overwrite? Usually we overwrite so the next one attaches to THIS one)
                                _pageBoundingBoxes[targetPage.Name] = (minX, minY, maxX, maxY);
                            }
                            else
                            {
                                _pageBoundingBoxes[targetPage.Name] = (minX, minY, maxX, maxY);
                            }
                        }
                    }
                    else
                    {
                        Log("Warning", $"插入窗口宏没有返回任何StorableObjects: '{resolvedPath}'");
                    }

                    if (targetPage != null)
                    {
                        pagesToProcess.Add(targetPage);
                    }
                }

                // Apply placeholders and value sets on all processed pages/objects
                if (element.VariableMappings != null && element.VariableMappings.Count > 0 && !isEms)
                {
                    IEnumerable<PlaceHolder> placeHoldersToProcess = null;
                    if (isEmp)
                    {
                        var listPhs = new List<PlaceHolder>();
                        foreach (Page page in pagesToProcess)
                        {
                            try
                            {
                                listPhs.AddRange(page.AllPlacements.OfType<PlaceHolder>());
                            }
                            catch { }
                        }
                        placeHoldersToProcess = listPhs;
                    }
                    else
                    {
                        if (insertedObjects != null)
                        {
                            var listPhs = new List<PlaceHolder>();
                            FindPlaceholders(insertedObjects, listPhs);
                            placeHoldersToProcess = listPhs;
                        }
                    }

                    if (placeHoldersToProcess != null)
                    {
                        foreach (var placeHolder in placeHoldersToProcess)
                        {
                            try
                            {
                                string phName = placeHolder.Name;
                                if (string.IsNullOrEmpty(phName)) continue;

                                string activeRecord = "ConfiguredRecord";
                                var recordsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                var recordsArray = placeHolder.GetRecordNames();
                                if (recordsArray != null)
                                {
                                    foreach (var r in recordsArray)
                                    {
                                        if (!string.IsNullOrEmpty(r)) recordsSet.Add(r);
                                    }
                                }
                                if (recordsSet.Count > 0)
                                {
                                    activeRecord = recordsSet.First();
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
                                    Log("Info", $"  占位符 '{phName}' 评估值集公式: '{valSetMapping.Value}' -> '{targetSetName}'");
                                    if (!string.IsNullOrWhiteSpace(targetSetName))
                                    {
                                        if (recordsSet.Contains(targetSetName))
                                        {
                                            placeHolder.ApplyRecord(targetSetName);
                                            activeRecord = targetSetName;
                                            placeholderUpdates++;
                                            Log("Success", $"  已成功应用值集: '{targetSetName}' 到占位符 '{phName}'");
                                        }
                                        else
                                        {
                                            Log("Warning", $"  值集 '{targetSetName}' 在占位符 '{phName}' 中不存在");
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

                                        if (!recordsSet.Contains(activeRecord))
                                        {
                                            placeHolder.AddRecord(activeRecord);
                                            recordsSet.Add(activeRecord);
                                        }

                                        Eplan.EplApi.Base.MultiLangString mlsValue = new Eplan.EplApi.Base.MultiLangString();
                                        mlsValue.SetAsString(evaluatedValue);

                                        if (placeHolder.VariableNames != null)
                                        {
                                            foreach (string varName in placeHolder.VariableNames)
                                            {
                                                placeHolder.SetValue(activeRecord, varName, mlsValue);
                                                Log("Success", $"  更新占位符 '{phName}' 变量 '{varName}' -> '{evaluatedValue}' (值集: '{activeRecord}')");
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
                                            var mapping = FindPlaceholderMapping(phName, varName, element.VariableMappings);

                                            if (mapping != null && !string.IsNullOrWhiteSpace(mapping.Value))
                                            {
                                                string evaluatedValue = EvaluateValueFormula(mapping.Value, parameters);

                                                if (!recordsSet.Contains(activeRecord))
                                                {
                                                    placeHolder.AddRecord(activeRecord);
                                                    recordsSet.Add(activeRecord);
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
                                Log("Error", $"  更新占位符 '{placeHolder.Name}' 失败: {phEx.Message}");
                            }
                        }
                    }
                }
            }

            ExecuteScriptSteps(db.Scripts, "AfterGenerate", parameters, LogDebug);
        }

        private static void FindPlaceholders(StorableObject[] objects, List<PlaceHolder> result)
        {
            if (objects == null || result == null) return;
            foreach (var obj in objects)
            {
                if (obj == null) continue;
                if (obj is PlaceHolder ph)
                {
                    result.Add(ph);
                }
                else if (obj is Eplan.EplApi.DataModel.Group grp)
                {
                    var subPlacements = grp.SubPlacements;
                    if (subPlacements != null && subPlacements.Length > 0)
                    {
                        FindPlaceholders(subPlacements, result);
                    }
                }
            }
        }

        private static object ConvertToVariableType(object value, string targetType)
        {
            if (value == null) return null;
            if (string.Equals(targetType, "Boolean", StringComparison.OrdinalIgnoreCase))
            {
                if (value is bool b) return b;
                if (bool.TryParse(value.ToString(), out bool parsedBool)) return parsedBool;
                return false;
            }
            if (string.Equals(targetType, "Integer", StringComparison.OrdinalIgnoreCase))
            {
                try { return Convert.ToInt32(value); }
                catch { }
                if (int.TryParse(value.ToString(), out int parsedInt)) return parsedInt;
                return 0;
            }
            if (string.Equals(targetType, "Double", StringComparison.OrdinalIgnoreCase))
            {
                try { return Convert.ToDouble(value); }
                catch { }
                if (double.TryParse(value.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double parsedDouble)) return parsedDouble;
                if (double.TryParse(value.ToString(), out double parsedDoubleLocal)) return parsedDoubleLocal;
                return 0.0;
            }
            return value.ToString();
        }

    }
}
