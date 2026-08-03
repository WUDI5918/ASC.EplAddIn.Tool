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
        public static List<string> ExtractPlaceholdersFromMacroFile(string macroPath, int variantIndex)
        {
            List<string> placeholdersList = new List<string>();
            try
            {
                SelectionSet selection = new SelectionSet();
                Page activePage = null;
                try
                {
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
                }
                catch (Exception selEx)
                {
                    LogDebug($"Failed to get active page via selection set: {selEx.Message}");
                }

                if (activePage == null)
                {
                    try
                    {
                        ProjectManager projectManager = new ProjectManager();
                        projectManager.LockProjectByDefault = false;
                        Project[] openProjects = projectManager.OpenProjects;
                        if (openProjects != null && openProjects.Length > 0)
                        {
                            foreach (Project proj in openProjects)
                            {
                                if (proj != null)
                                {
                                    foreach (Page page in proj.Pages)
                                    {
                                        if (page != null)
                                        {
                                            activePage = page;
                                            break;
                                        }
                                    }
                                    if (activePage != null) break;
                                }
                            }
                        }
                    }
                    catch (Exception projEx)
                    {
                        LogDebug($"Failed to fallback to open projects: {projEx.Message}");
                    }
                }

                if (activePage == null)
                {
                    LogDebug("ExtractPlaceholdersFromMacroFile: No active page or fallback page found.");
                    return placeholdersList;
                }

                Project activeProject = activePage.Project;
                string resolvedPath = ResolveMacroPath(macroPath);
                if (!System.IO.File.Exists(resolvedPath))
                {
                    LogDebug($"ExtractPlaceholdersFromMacroFile: Warning - File not found on disk at resolved path '{resolvedPath}'. We will let EPLAN attempt native resolution.");
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
                            try
                            {
                                oInsert.PageMacro(resolvedPath, activePage, activeProject, true);
                            }
                            catch
                            {
                                oInsert.PageMacro(macroPath, activePage, activeProject, true);
                            }

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

                            // Inherit structural designations from activePage to prevent structural constraint errors
                            try
                            {
                                pagePropList.DESIGNATION_FUNCTIONALASSIGNMENT = activePage.Properties.DESIGNATION_FUNCTIONALASSIGNMENT;
                                pagePropList.DESIGNATION_PLANT = activePage.Properties.DESIGNATION_PLANT;
                                pagePropList.DESIGNATION_LOCATION = activePage.Properties.DESIGNATION_LOCATION;
                                pagePropList.DESIGNATION_DOCTYPE = activePage.Properties.DESIGNATION_DOCTYPE;
                                pagePropList.DESIGNATION_USERDEFINED = activePage.Properties.DESIGNATION_USERDEFINED;
                            }
                            catch { }

                            pagePropList.PAGE_COUNTER = "TEMP_EXTRACT_9999";
                            tempPage.Create(activeProject, DocumentTypeManager.DocumentType.Circuit, pagePropList);

                            Insert oInsert = new Insert();
                            PointD insertPoint = new PointD(0, 0);
                            try
                            {
                                oInsert.WindowMacro(resolvedPath, variantIndex, tempPage, insertPoint, Insert.MoveKind.Relative);
                            }
                            catch
                            {
                                oInsert.WindowMacro(macroPath, variantIndex, tempPage, insertPoint, Insert.MoveKind.Relative);
                            }

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

        public static void AddPlaceholderToList(Eplan.EplApi.DataModel.Graphics.PlaceHolder ph, List<string> list)
        {
            if (ph == null) return;
            string phName = string.IsNullOrEmpty(ph.Name) ? "" : ph.Name.Trim();
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

    }
}
