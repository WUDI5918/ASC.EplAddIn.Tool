using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
    public partial class ConfiguratorService
    {
        // ==================== PAGE CREATION HELPER ====================
        public static HashSet<string> ClearedPages { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Decides whether a page found by name after a page-macro insertion is a page this run
        /// actually created, i.e. its name was NOT present in the pre-insertion snapshot
        /// (<paramref name="oldPageNames"/>). A page that already existed before the insertion
        /// (incremental mode keeps old pages; a string-vs-canonical name mismatch can also hit an
        /// old page) must not receive this element's placeholder mappings — that would silently
        /// overwrite old content (t2-H3).
        /// </summary>
        /// <param name="pageName">The full page name of the located page.</param>
        /// <param name="oldPageNames">Case-insensitive names of pages that existed before the
        /// insertion; <c>null</c> means "no snapshot" and is treated as "new" (conservative for
        /// freshly inserted pages).</param>
        public static bool IsNewlyAddedPageName(string pageName, ICollection<string> oldPageNames)
        {
            if (string.IsNullOrEmpty(pageName)) return false;
            if (oldPageNames == null) return true;
            return !oldPageNames.Contains(pageName);
        }

        private static Page GetOrCreatePage(Project project, List<MappingEntry> structures, Page fallbackPage, Dictionary<string, object> parameters, LibrarySettings settings)
        {
            if (structures == null || structures.Count == 0) return fallbackPage;

            try
            {
                var structure = PageStructure.FromStructureMappings(structures, settings);

                // Evaluate structure properties with formula engine
                structure.FunctionalAssignment = EvaluateValueFormula(structure.FunctionalAssignment, parameters);
                structure.FunctionDesignation = EvaluateValueFormula(structure.FunctionDesignation, parameters);
                structure.LocationDesignation = EvaluateValueFormula(structure.LocationDesignation, parameters);
                structure.DocumentType = EvaluateValueFormula(structure.DocumentType, parameters);
                structure.UserDefined = EvaluateValueFormula(structure.UserDefined, parameters);
                structure.PageName = EvaluateValueFormula(structure.PageName, parameters);

                if (string.IsNullOrWhiteSpace(structure.PageName))
                {
                    return fallbackPage;
                }

                string fullPageName = structure.ToFullPageName();

                // Find existing page
                Page existingPage = null;
                foreach (Page p in project.Pages)
                {
                    if (p.Name.Equals(fullPageName, StringComparison.OrdinalIgnoreCase))
                    {
                        existingPage = p;
                        break;
                    }
                }

                if (existingPage != null)
                {
                    if (IncrementalGeneration)
                    {
                        // Incremental: reuse the existing page, do not delete it.
                        return existingPage;
                    }

                    if (!ClearedPages.Contains(fullPageName))
                    {
                        Log("Info", $"检测到已存在的页面 '{fullPageName}'，正在清理以重新生成...");
                        if (!AbortOnFirstError)
                        {
                            Log("Warning", $"  跳过模式 (AbortOnFirstError=false)：页面 '{fullPageName}' 将被删除并在单个事务内提交，即使后续元素失败被跳过，该删除也不会回滚。");
                        }
                        try
                        {
                            existingPage.Remove();
                        }
                        catch (Exception remEx)
                        {
                            Log("Warning", $"  清理页面 '{fullPageName}' 失败: {remEx.Message}");
                        }
                        ClearedPages.Add(fullPageName);
                    }
                    else
                    {
                        return existingPage;
                    }
                }
                else
                {
                    ClearedPages.Add(fullPageName);
                }

                // Create page
                Page page = new Page();
                PagePropertyList pagePropList = new PagePropertyList();
                if (!string.IsNullOrWhiteSpace(structure.FunctionalAssignment)) pagePropList.DESIGNATION_FUNCTIONALASSIGNMENT = structure.FunctionalAssignment.Trim();
                if (!string.IsNullOrWhiteSpace(structure.FunctionDesignation)) pagePropList.DESIGNATION_PLANT = structure.FunctionDesignation.Trim();
                if (!string.IsNullOrWhiteSpace(structure.LocationDesignation)) pagePropList.DESIGNATION_LOCATION = structure.LocationDesignation.Trim();
                if (!string.IsNullOrWhiteSpace(structure.DocumentType)) pagePropList.DESIGNATION_DOCTYPE = structure.DocumentType.Trim();
                if (!string.IsNullOrWhiteSpace(structure.UserDefined)) pagePropList.DESIGNATION_USERDEFINED = structure.UserDefined.Trim();
                if (!string.IsNullOrWhiteSpace(structure.PageName)) pagePropList.PAGE_COUNTER = structure.PageName.Trim();

                page.Create(project, DocumentTypeManager.DocumentType.Circuit, pagePropList);
                return page;
            }
            catch (Exception ex)
            {
                Log("Error", $"创建页面失败: {ex.Message}");
                throw new InvalidOperationException($"创建或解析页面结构失败: {ex.Message}", ex);
            }
        }

        public static List<EplanMacroInfo> ProjectMacrosCache { get; set; } = new List<EplanMacroInfo>();
        public static string CachedProjectName { get; set; } = "";

        public static async System.Threading.Tasks.Task<List<EplanMacroInfo>> GetActiveProjectMacrosAsync(bool forceRefresh = false)
        {
            LogDebug($"--- GetActiveProjectMacros Invoked (forceRefresh={forceRefresh}) ---");
            var list = new List<EplanMacroInfo>();
            int pageCount = 0;
            try
            {
                Eplan.EplApi.DataModel.Project project = null;
                try
                {
                    Eplan.EplApi.HEServices.SelectionSet selection = new Eplan.EplApi.HEServices.SelectionSet();
                    project = selection.GetCurrentProject(false);
                }
                catch (Exception selEx)
                {
                    LogDebug($"Failed to get current project from SelectionSet: {selEx.Message}");
                }

                if (project == null)
                {
                    try
                    {
                        Eplan.EplApi.DataModel.ProjectManager pm = new Eplan.EplApi.DataModel.ProjectManager();
                        pm.LockProjectByDefault = false;
                        if (pm.OpenProjects != null && pm.OpenProjects.Length > 0)
                        {
                            project = pm.OpenProjects[0];
                        }
                    }
                    catch (Exception pmEx)
                    {
                        LogDebug($"Failed to fallback to open projects list: {pmEx.Message}");
                    }
                }

                if (project == null)
                {
                    LogDebug("No active or open project found in EPLAN.");
                    return list;
                }

                string projName = project.ProjectName;

                if (!forceRefresh && ProjectMacrosCache != null && ProjectMacrosCache.Count > 0 && string.Equals(CachedProjectName, projName, StringComparison.OrdinalIgnoreCase))
                {
                    LogDebug($"Reusing Cached Project Macros ({ProjectMacrosCache.Count} items) for project: {projName}");
                    return ProjectMacrosCache;
                }

                LogDebug($"Scanning project for unique macro files: {projName}");
                CachedProjectName = projName;

                var uniquePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // 1+2. Fetch MacroBoxes and Pages via DMObjectsFinder on a background thread
                //    to avoid blocking the UI thread for 291 pages + 700+ MacroBoxes.
                try
                {
                    var finder = new Eplan.EplApi.DataModel.DMObjectsFinder(project);

                    var bgResult = await Task.Run(() =>
                    {
                        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        int pCount = 0;

                        // MacroBoxes
                        try
                        {
                            var mbs = finder.GetAll<Eplan.EplApi.DataModel.MacroBox>(false);
                            if (mbs != null)
                            {
                                foreach (var box in mbs)
                                {
                                    try
                                    {
                                        string path = GetSafeProperty(box.Properties, Eplan.EplApi.DataModel.Properties.MacroBox.MACROBOX_MACRO);
                                        if (!string.IsNullOrWhiteSpace(path))
                                            paths.Add(path.Trim());
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch { }

                        // Pages
                        try
                        {
                            var pages = finder.GetAll<Eplan.EplApi.DataModel.Page>(false);
                            if (pages != null)
                            {
                                pCount = pages.Length;
                                foreach (var page in pages)
                                {
                                    try
                                    {
                                        var props = page.Properties;
                                        string pageMacroPath = GetSafeProperty(props, Eplan.EplApi.DataModel.Properties.Page.PAGE_MACRO_NAME);
                                        if (string.IsNullOrWhiteSpace(pageMacroPath))
                                            pageMacroPath = GetSafeProperty(props, 11910); // PAGE_MACRO

                                        if (!string.IsNullOrWhiteSpace(pageMacroPath))
                                            paths.Add(pageMacroPath.Trim());
                                    }
                                    catch { }
                                }
                            }
                        }
                        catch { }

                        return new { Paths = paths, PageCount = pCount };
                    });

                    // Merge background results
                    foreach (var p in bgResult.Paths) uniquePaths.Add(p);
                    pageCount = bgResult.PageCount;
                }
                catch (Exception finderEx)
                {
                    LogDebug($"Error in background DMObjectsFinder scan: {finderEx.Message}");
                }

                // 3. Scan only the active project-specific macro subdirectory inside $(MD_MACROS) (e.g. $(MD_MACROS)\<ProjectName>) if it exists
                try
                {
                    string mdMacros = Eplan.EplApi.Base.PathMap.SubstitutePath("$(MD_MACROS)");
                    string projectMacroDir = Path.Combine(mdMacros, projName);
                    if (System.IO.Directory.Exists(projectMacroDir))
                    {
                        LogDebug($"Scanning project-specific macro directory: {projectMacroDir}");
                        var files = await System.Threading.Tasks.Task.Run(() =>
                        {
                            try
                            {
                                return System.IO.Directory.GetFiles(projectMacroDir, "*.*", System.IO.SearchOption.AllDirectories);
                            }
                            catch (Exception ex)
                            {
                                LogDebug($"Error in background directory scan: {ex.Message}");
                                return null;
                            }
                        });

                        if (files != null)
                        {
                            var filteredFiles = files.Where(f => f.EndsWith(".ema", StringComparison.OrdinalIgnoreCase) ||
                                                                f.EndsWith(".ems", StringComparison.OrdinalIgnoreCase) ||
                                                                f.EndsWith(".emp", StringComparison.OrdinalIgnoreCase));
                            foreach (var file in filteredFiles)
                            {
                                string relativePath = file;
                                if (file.StartsWith(mdMacros, StringComparison.OrdinalIgnoreCase))
                                {
                                    string sub = file.Substring(mdMacros.Length);
                                    if (!sub.StartsWith("\\") && !sub.StartsWith("/"))
                                    {
                                        sub = "\\" + sub;
                                    }
                                    relativePath = "$(MD_MACROS)" + sub;
                                }
                                uniquePaths.Add(relativePath);
                            }
                        }
                    }
                }
                catch (Exception dirEx)
                {
                    LogDebug($"Error scanning project-specific macro directory: {dirEx.Message}");
                }

                LogDebug($"Found {uniquePaths.Count} unique macro files in the active project.");

                // Build result list from unique paths
                foreach (var path in uniquePaths)
                {
                    list.Add(new EplanMacroInfo
                    {
                        ProjectName = projName,
                        Name = Path.GetFileNameWithoutExtension(path),
                        MacroPath = path,
                        IsPageMacro = path.EndsWith(".emp", StringComparison.OrdinalIgnoreCase),
                        IsSymbolMacro = path.EndsWith(".ems", StringComparison.OrdinalIgnoreCase),
                        Description = ""
                    });
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Global error in GetActiveProjectMacros: {ex.Message}\n{ex.StackTrace}");
            }

            LogDebug($"GetActiveProjectMacros finished. Total macros found: {list.Count}, pages processed: {pageCount}");
            ProjectMacrosCache = list;
            return list;
        }

        public static List<EplanMacroInfo> LoadMacroFileDetails(string macroPath)
        {
            var list = new List<EplanMacroInfo>();
            try
            {
                string resolvedPath = macroPath;
                if (!string.IsNullOrEmpty(resolvedPath) && !Path.IsPathRooted(resolvedPath) && !resolvedPath.StartsWith("$") && !resolvedPath.StartsWith("?"))
                {
                    resolvedPath = Path.Combine("$(MD_MACROS)", resolvedPath);
                }
                resolvedPath = Eplan.EplApi.Base.PathMap.SubstitutePath(resolvedPath);
                bool isEmp = resolvedPath.EndsWith(".emp", StringComparison.OrdinalIgnoreCase);
                bool isEms = resolvedPath.EndsWith(".ems", StringComparison.OrdinalIgnoreCase);

                LogDebug($"LoadMacroFileDetails: Loading details for '{macroPath}' (resolved: '{resolvedPath}')");

                if (isEms)
                {
                    // Symbol macro (.ems): record basic info, no placeholder extraction available
                    LogDebug($"LoadMacroFileDetails: Symbol macro (.ems) detected: '{resolvedPath}' — recording metadata only.");
                    list.Add(new EplanMacroInfo
                    {
                        ProjectName = "Symbol Macros",
                        Name = Path.GetFileNameWithoutExtension(resolvedPath),
                        MacroPath = macroPath,
                        IsPageMacro = false,
                        IsSymbolMacro = true,
                        Placeholders = new List<string>()
                    });
                }
                else if (isEmp)
                {
                    // 对于 .emp 页宏，直接打开并遍历 AllPlacements 提取占位符
                    Project activeProject = null;
                    try
                    {
                        activeProject = new SelectionSet().GetCurrentProject(false);
                    }
                    catch { }

                    if (activeProject == null)
                    {
                        try
                        {
                            ProjectManager pmMgr = new ProjectManager();
                            pmMgr.LockProjectByDefault = false;
                            if (pmMgr.OpenProjects != null && pmMgr.OpenProjects.Length > 0)
                            {
                                activeProject = pmMgr.OpenProjects[0];
                            }
                        }
                        catch { }
                    }

                    Eplan.EplApi.DataModel.MasterData.PageMacro pm = null;
                    try
                    {
                        pm = new Eplan.EplApi.DataModel.MasterData.PageMacro();
                        pm.Open(resolvedPath, activeProject);
                        var pages = pm.Pages;
                        if (pages != null)
                        {
                            LogDebug($"LoadMacroFileDetails: Page macro '{Path.GetFileName(resolvedPath)}' has {pages.Length} pages");

                            for (int i = 0; i < pages.Length; i++)
                            {
                                var p = pages[i];
                                if (p == null) continue;

                                var placeholdersList = new List<string>();
                                string pageName = p.Name ?? "";

                                try
                                {
                                    var allPlacements = p.AllPlacements;
                                    if (allPlacements != null)
                                    {
                                        foreach (var placement in allPlacements)
                                        {
                                            if (placement == null) continue;

                                            // 1) 如果 placement 本身是 PlaceHolder，直接提取
                                            var ph = placement as Eplan.EplApi.DataModel.Graphics.PlaceHolder;
                                            if (ph != null)
                                            {
                                                AddPlaceholderToList(ph, placeholdersList);
                                                continue;
                                            }

                                            // 2) 对非 PlaceHolder 的 placement，检查其属性中是否包含占位符
                                            try
                                            {
                                                ExtractPlaceholdersFromPlacementProps(placement, placeholdersList);
                                            }
                                            catch (Exception propEx)
                                            {
                                                LogDebug($"LoadMacroFileDetails: Error checking placement props on page '{pageName}': {propEx.Message}");
                                            }
                                        }
                                    }
                                }
                                catch (Exception phEx)
                                {
                                    LogDebug($"LoadMacroFileDetails: Error extracting placeholders from page '{pageName}': {phEx.Message}");
                                }

                                LogDebug($"LoadMacroFileDetails: Page '{pageName}' has {placeholdersList.Count} placeholders");

                                list.Add(new EplanMacroInfo
                                {
                                    ProjectName = "System Pages",
                                    Name = pageName,
                                    MacroPath = macroPath,
                                    IsPageMacro = true,
                                    Placeholders = placeholdersList
                                });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"LoadMacroFileDetails: Failed to open page macro '{resolvedPath}': {ex.Message}");
                    }
                    finally
                    {
                        if (pm != null)
                        {
                            try { pm.Dispose(); } catch { }
                        }
                    }
                }
                else
                {
                    using (Eplan.EplApi.DataModel.MasterData.WindowMacro wm = new Eplan.EplApi.DataModel.MasterData.WindowMacro())
                    {
                        wm.Open(resolvedPath);
                        foreach (var repType in wm.RepresentationTypes)
                        {
                            for (int variantIdx = 0; variantIdx < 16; variantIdx++)
                            {
                                if (wm.ExistVariant(repType, variantIdx))
                                {
                                    wm.ChangeCurrentVariant((Eplan.EplApi.DataModel.MasterData.WindowMacro.Enums.RepresentationType)repType, variantIdx);

                                    var placeholdersList = new List<string>();
                                    var phs = wm.PlaceHolders;
                                    if (phs != null)
                                    {
                                        foreach (var ph in phs)
                                        {
                                            AddPlaceholderToList(ph, placeholdersList);
                                        }
                                    }

                                    list.Add(new EplanMacroInfo
                                    {
                                        ProjectName = "System Macros",
                                        Name = Path.GetFileNameWithoutExtension(resolvedPath),
                                        MacroPath = macroPath,
                                        IsPageMacro = false,
                                        RepresentationType = (int)repType,
                                        VariantIndex = variantIdx,
                                        Placeholders = placeholdersList
                                    });
                                }
                            }
                        }
                    }
                }

                // Add these detailed records to our global cache so that selection/validation queries will hit it instantly!
                if (list.Count > 0)
                {
                    lock (ProjectMacrosCache)
                    {
                        // Remove any old incomplete records for this macroPath
                        ProjectMacrosCache.RemoveAll(m => string.Equals(m.MacroPath, macroPath, StringComparison.OrdinalIgnoreCase));
                        // Add the fully detailed records
                        ProjectMacrosCache.AddRange(list);
                    }
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error loading macro file details for '{macroPath}': {ex.Message}");
            }
            return list;
        }

        /// <summary>
        /// 从非 PlaceHolder 类型的 placement 属性中提取占位符（#&lt;...&gt; 格式）
        /// </summary>
        private static void ExtractPlaceholdersFromPlacementProps(Eplan.EplApi.DataModel.Placement placement, List<string> placeholdersList)
        {
            if (placement == null) return;

            // PlacementPropertyList 继承自 StorableObjectPropertyList
            // 检查已知的可能包含占位符的属性
            try
            {
                var props = placement.Properties;
                if (props == null) return;

                // 检查 revision 相关属性中是否意外包含占位符引用
                var propGetters = new Func<Eplan.EplApi.DataModel.PlacementPropertyList, string>[]
                {
                    p => p.INSTANCE_REVISIONMARKER,
                    p => p.INSTANCE_REVISIONID,
                };

                foreach (var getter in propGetters)
                {
                    try
                    {
                        string val = getter(props);
                        if (!string.IsNullOrEmpty(val) && val.Contains("#<") && val.Contains(">"))
                        {
                            ExtractPlaceholderKeys(val, placeholdersList);
                        }
                    }
                    catch { /* 属性可能不可用，安全忽略 */ }
                }
            }
            catch { /* Properties 访问失败时安全忽略 */ }
        }

        /// <summary>
        /// 从字符串中提取 #&lt;...&gt; 格式的占位符键名
        /// </summary>
        private static void ExtractPlaceholderKeys(string text, List<string> placeholdersList)
        {
            int start = text.IndexOf("#<");
            while (start >= 0)
            {
                int end = text.IndexOf(">", start + 2);
                if (end > start)
                {
                    string varKey = text.Substring(start + 2, end - start - 2).Trim();
                    if (!string.IsNullOrEmpty(varKey) && !placeholdersList.Contains(varKey))
                    {
                        placeholdersList.Add(varKey);
                    }
                    start = text.IndexOf("#<", end + 1);
                }
                else break;
            }
        }


    }
}
