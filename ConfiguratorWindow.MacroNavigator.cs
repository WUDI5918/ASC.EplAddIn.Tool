using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Newtonsoft.Json;
using Microsoft.Win32;
using System.Threading.Tasks;
using System.Xml.Linq;
using OfficeOpenXml;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    public partial class ConfiguratorWindow : Window
    {
        // ==================== MACRO NAVIGATOR PANEL LOGIC ====================
        private async void ShowMacroNavigator(bool isAddingNew, MacroElement target)
        {
            _isAddingNewElement = isAddingNew;
            _targetElementForMacroSelection = target;

            // Expand ColMacroNav column and show GridMacroNav & Splitter
            ColMacroNav.MinWidth = 200; // Force layout pass to overcome any cached local GridSplitter widths
            ColMacroNav.Width = new GridLength(280);
            GridMacroNav.Visibility = Visibility.Visible;
            if (SplitterMacroNav != null)
            {
                SplitterMacroNav.Visibility = Visibility.Visible;
            }

            // Only fetch if not already loaded to prevent slow reloading
            if (_allProjectMacros == null || _allProjectMacros.Count == 0)
            {
                await LoadMacrosFromActiveProjectAsync(false);
            }
        }

        private void BtnCloseMacroNav_Click(object sender, RoutedEventArgs e)
        {
            // Just fold/collapse the column and hide UI. DO NOT reset the macro cache.
            ColMacroNav.MinWidth = 0;
            ColMacroNav.Width = new GridLength(0);
            GridMacroNav.Visibility = Visibility.Collapsed;
            if (SplitterMacroNav != null)
            {
                SplitterMacroNav.Visibility = Visibility.Collapsed;
            }
            _targetElementForMacroSelection = null;
            _isAddingNewElement = false;
        }

        private void TvMacros_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var selected = TvMacros.SelectedItem as MacroTreeNode;
            if (selected != null)
            {
                // Only allow selecting "Page", "Variant" nodes, or "Macro" nodes if it is a Page Macro (.emp)
                bool isSelectable = selected.Type == "Page" || selected.Type == "Variant" ||
                                    (selected.Type == "Macro" && selected.FullPath != null && selected.FullPath.EndsWith(".emp", StringComparison.OrdinalIgnoreCase));
                if (isSelectable)
                {
                    ApplyMacroSelection(selected, forceAddNew: false);
                    e.Handled = true;
                }
            }
        }

        private async void BtnRefreshMacroNav_Click(object sender, RoutedEventArgs e)
        {
            await LoadMacrosFromActiveProjectAsync(true);
        }

        private void TxtSearchMacro_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_macroSearchDebounceTimer == null)
            {
                _macroSearchDebounceTimer = new System.Windows.Threading.DispatcherTimer();
                _macroSearchDebounceTimer.Interval = TimeSpan.FromMilliseconds(200);
                _macroSearchDebounceTimer.Tick += (_, __) =>
                {
                    _macroSearchDebounceTimer.Stop();
                    FilterAndShowMacros();
                };
            }
            _macroSearchDebounceTimer.Stop();
            _macroSearchDebounceTimer.Start();
        }

        private void PopulateVariableMappings(MacroElement element, MacroTreeNode node)
        {
            if (element == null || node == null) return;

            var newMappings = new List<MappingEntry>();
            var placeholderNodes = new List<MacroTreeNode>();
            if (node.Type == "PlaceHolder")
            {
                placeholderNodes.Add(node);
            }
            else
            {
                FindPlaceholderNodesRecursive(node, placeholderNodes);
            }

            foreach (var phNode in placeholderNodes)
            {
                string phObjectName = phNode.Name;
                if (phNode.Children != null)
                {
                    foreach (var varNode in phNode.Children.Where(c => c.Type == "Variable"))
                    {
                        string varName = varNode.Name;
                        string fullName = string.Equals(phObjectName, "Variables", StringComparison.OrdinalIgnoreCase)
                            ? varName
                            : (phObjectName.Contains("Page_Property") ? $"{phObjectName} : {varName}" : $"{phObjectName}={varName}");

                        var existing = element.VariableMappings?.FirstOrDefault(m =>
                        {
                            if (string.Equals(m.Key, fullName, StringComparison.OrdinalIgnoreCase)) return true;
                            string normKey = m.Key.Replace(" : ", "=");
                            string normName = fullName.Replace(" : ", "=");
                            return string.Equals(normKey, normName, StringComparison.OrdinalIgnoreCase);
                        });
                        string value = existing != null ? existing.Value : "";
                        newMappings.Add(new MappingEntry(fullName, value));
                    }
                }
            }
            element.VariableMappings = newMappings;
        }

        private void FindPlaceholderNodesRecursive(MacroTreeNode node, List<MacroTreeNode> results)
        {
            if (node == null || node.Children == null) return;
            foreach (var child in node.Children)
            {
                if (child.Type == "PlaceHolder")
                {
                    results.Add(child);
                }
                else
                {
                    FindPlaceholderNodesRecursive(child, results);
                }
            }
        }

        private void BtnPreviewVariant_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true; // Prevent selection or expand toggles
            var btn = sender as Button;
            if (btn == null) return;

            var node = btn.DataContext as MacroTreeNode;
            if (node == null) return;

            ConfiguratorService.LogDebug($"BtnPreviewVariant_Click: Preview requested for '{node.Name}', Path='{node.FullPath}', Variant={node.VariantIndex}");

            // 1. Open Right Details Panel if collapsed
            if (ColDetailsArea.Width.Value == 0)
            {
                ColDetailsArea.Width = new GridLength(300);
                if (RightSplitter != null)
                {
                    RightSplitter.Visibility = Visibility.Visible;
                }
            }

            // 2. Select Preview Tab
            if (TabControlRightDetails != null)
            {
                TabControlRightDetails.SelectedIndex = 1; // 0: Details, 1: Preview
            }

            // 3. Populate Preview UI
            if (TxtPreviewEmpty != null) TxtPreviewEmpty.Visibility = Visibility.Collapsed;
            if (GridPreviewContent != null) GridPreviewContent.Visibility = Visibility.Visible;

            if (TxtPreviewTitle != null) TxtPreviewTitle.Text = Path.GetFileName(node.FullPath);

            // Build representation type string
            string repTypeStr = GetRepresentationTypeName(node.RepresentationType);
            string variantName = node.Type == "Variant" ? $"Variant {(char)('A' + node.VariantIndex)} ({repTypeStr})" : node.Name;
            if (TxtPreviewInfo != null) TxtPreviewInfo.Text = variantName;
            if (TxtPreviewPath != null) TxtPreviewPath.Text = $"路径: {node.FullPath}";

            // 4. Try to load preview image from Windows Shell (for .ema/.emp) or $(MD_MACROS) fallback
            try
            {
                if (ImgMacroPreview != null && PanelPreviewPlaceholder != null)
                {
                    string resolvedPath = node.FullPath;
                    if (!string.IsNullOrEmpty(resolvedPath) && !Path.IsPathRooted(resolvedPath) && !resolvedPath.StartsWith("$") && !resolvedPath.StartsWith("?"))
                    {
                        resolvedPath = Path.Combine("$(MD_MACROS)", resolvedPath);
                    }
                    resolvedPath = Eplan.EplApi.Base.PathMap.SubstitutePath(resolvedPath);

                    ImageSource imageSource = null;

                    // First attempt: Extract preview directly from EPLAN .ema/.emp using Windows Explorer Shell extension (registered thumbnail provider)
                    try
                    {
                        if (File.Exists(resolvedPath))
                        {
                            ConfiguratorService.LogDebug($"Attempting to load Windows Shell thumbnail for macro: '{resolvedPath}'");
                            imageSource = ShellThumbnail.GetThumbnail(resolvedPath, 500, 500);
                        }
                    }
                    catch (Exception shellEx)
                    {
                        ConfiguratorService.LogDebug($"Windows Shell thumbnail extraction failed: {shellEx.Message}");
                    }

                    // Second attempt: Fallback to searching for PNG/BMP/JPG image files next to the macro
                    if (imageSource == null)
                    {
                        string imagePath = null;
                        string[] possibleExtensions = { ".png", ".bmp", ".jpg", ".jpeg" };

                        foreach (var ext in possibleExtensions)
                        {
                            string p1 = resolvedPath + ext;
                            if (File.Exists(p1))
                            {
                                imagePath = p1;
                                break;
                            }
                            string p2 = Path.ChangeExtension(resolvedPath, ext);
                            if (File.Exists(p2))
                            {
                                imagePath = p2;
                                break;
                            }
                        }

                        if (imagePath != null)
                        {
                            ConfiguratorService.LogDebug($"Found fallback preview image file: '{imagePath}'");
                            var bitmap = new BitmapImage();
                            bitmap.BeginInit();
                            bitmap.UriSource = new Uri(imagePath);
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.EndInit();
                            imageSource = bitmap;
                        }
                    }

                    // Display image or show placeholder
                    if (imageSource != null)
                    {
                        ImgMacroPreview.Source = imageSource;
                        ImgMacroPreview.Visibility = Visibility.Visible;
                        PanelPreviewPlaceholder.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        ConfiguratorService.LogDebug("No preview image or Windows Shell thumbnail found, showing placeholder.");
                        ImgMacroPreview.Source = null;
                        ImgMacroPreview.Visibility = Visibility.Collapsed;
                        PanelPreviewPlaceholder.Visibility = Visibility.Visible;
                    }
                }
            }
            catch (Exception ex)
            {
                ConfiguratorService.LogDebug($"Error loading preview image: {ex.Message}");
                if (ImgMacroPreview != null)
                {
                    ImgMacroPreview.Source = null;
                    ImgMacroPreview.Visibility = Visibility.Collapsed;
                }
                if (PanelPreviewPlaceholder != null)
                {
                    PanelPreviewPlaceholder.Visibility = Visibility.Visible;
                }
            }
        }

        private void ApplyMacroSelection(MacroTreeNode selected, bool forceAddNew)
        {
            if (selected == null) return;

            // Only allow selecting "Page", "Variant" nodes, or "Macro" nodes if it is a Page Macro (.emp)
            bool isSelectable = selected.Type == "Page" || selected.Type == "Variant" ||
                                (selected.Type == "Macro" && selected.FullPath != null && selected.FullPath.EndsWith(".emp", StringComparison.OrdinalIgnoreCase));
            if (!isSelectable)
            {
                MessageBox.Show("请先在左侧宏选择器中选中一个具体的宏变体或页面宏。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_selectedItem is MacroTypical typical)
            {
                if (forceAddNew || _isAddingNewElement || _targetElementForMacroSelection == null)
                {
                    // Create new MacroElement with selected path and typical positioning settings
                    var newElement = new MacroElement
                    {
                        MacroPath = selected.FullPath,
                        VariantIndex = selected.VariantIndex,
                        RepresentationType = selected.RepresentationType,
                        PositionMode = "FromMacro",
                        IsActiveFormula = ""
                    };
                    PopulateVariableMappings(newElement, selected);
                    typical.Elements.Add(newElement);
                }
                else if (_targetElementForMacroSelection != null)
                {
                    // Update existing element
                    _targetElementForMacroSelection.MacroPath = selected.FullPath;
                    _targetElementForMacroSelection.VariantIndex = selected.VariantIndex;
                    _targetElementForMacroSelection.RepresentationType = selected.RepresentationType;
                    PopulateVariableMappings(_targetElementForMacroSelection, selected);

                    // Clear target selection after applying
                    _targetElementForMacroSelection = null;
                }

                // Force UI binding refresh
                BindElementsList(typical.Elements);
                MarkDirty();
            }
        }

        private void BtnApplyMacroSelection_Click(object sender, RoutedEventArgs e)
        {
            var selected = TvMacros.SelectedItem as MacroTreeNode;
            ApplyMacroSelection(selected, forceAddNew: false);
        }

        private async Task LoadMacrosFromActiveProjectAsync(bool forceRefresh = false)
        {
            // 如果不强制刷新且本地已有缓存，直接构建树即可（无需再调服务）
            if (!forceRefresh && _allProjectMacros != null && _allProjectMacros.Count > 0)
            {
                FilterAndShowMacros();
                return;
            }

            if (ProgressBarLoad != null) ProgressBarLoad.Visibility = Visibility.Visible;
            if (BtnRefreshMacroNav != null) BtnRefreshMacroNav.IsEnabled = false;

            // Let the UI thread render the progress bar
            await Task.Delay(50);

            try
            {
                TxtSearchMacro.Text = "";
                _allProjectMacros = await ConfiguratorService.GetActiveProjectMacrosAsync(forceRefresh);
                FilterAndShowMacros();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"获取活动项目中的宏列表失败: {ex.Message}", "配置器提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                if (ProgressBarLoad != null) ProgressBarLoad.Visibility = Visibility.Collapsed;
                if (BtnRefreshMacroNav != null) BtnRefreshMacroNav.IsEnabled = true;
            }
        }

        private async void FilterAndShowMacros()
        {
            string search = TxtSearchMacro.Text.Trim().ToLower();
            var macros = _allProjectMacros;
            if (macros == null) return;

            // 在后台线程执行过滤和树构建（700+ 宏时显著减少 UI 卡顿）
            var tree = await Task.Run(() =>
            {
                var filtered = macros.Where(m =>
                    string.IsNullOrEmpty(search) ||
                    (m.Name != null && m.Name.ToLower().Contains(search)) ||
                    (m.Description != null && m.Description.ToLower().Contains(search)) ||
                    (m.MacroPath != null && m.MacroPath.ToLower().Contains(search))
                ).ToList();

                return BuildMacroTree(filtered);
            });

            TvMacros.ItemsSource = tree;
        }

        private List<MacroTreeNode> BuildMacroTree(List<EplanMacroInfo> macros)
        {
            var rootNodes = new List<MacroTreeNode>();
            // 使用字典加速节点查找，避免 O(n*m) 的 FirstOrDefault 遍历
            var projNodeMap = new Dictionary<string, MacroTreeNode>(StringComparer.OrdinalIgnoreCase);
            // key: "projKey|folderPath" -> folderNode
            var folderNodeMap = new Dictionary<string, MacroTreeNode>(StringComparer.OrdinalIgnoreCase);
            // key: "projKey|folderPath|macroFileName" -> macroNode
            var macroNodeMap = new Dictionary<string, MacroTreeNode>(StringComparer.OrdinalIgnoreCase);

            foreach (var macro in macros)
            {
                string projName = string.IsNullOrEmpty(macro.ProjectName) ? "Without name" : macro.ProjectName;

                MacroTreeNode projNode;
                if (!projNodeMap.TryGetValue(projName, out projNode))
                {
                    projNode = new MacroTreeNode
                    {
                        Name = projName,
                        Type = "Project",
                        Icon = "📁",
                        IsExpanded = true
                    };
                    rootNodes.Add(projNode);
                    projNodeMap[projName] = projNode;
                }

                var pathParts = SplitMacroPath(macro.MacroPath);
                if (pathParts.Count == 0) continue;

                var folderParts = pathParts.Take(pathParts.Count - 1).ToList();
                string macroFileName = Path.GetFileNameWithoutExtension(pathParts.Last());

                var currentNode = projNode;
                string folderPathKey = projName;

                foreach (var folder in folderParts)
                {
                    folderPathKey += "|" + folder;
                    MacroTreeNode folderNode;
                    if (!folderNodeMap.TryGetValue(folderPathKey, out folderNode))
                    {
                        folderNode = new MacroTreeNode
                        {
                            Name = folder,
                            Type = "Folder",
                            Icon = "📁",
                            IsExpanded = true
                        };
                        currentNode.Children.Add(folderNode);
                        folderNodeMap[folderPathKey] = folderNode;
                    }
                    currentNode = folderNode;
                }

                string macroKey = folderPathKey + "|" + macroFileName;
                if (!macroNodeMap.ContainsKey(macroKey))
                {
                    var macroNode = new MacroTreeNode
                    {
                        Name = macroFileName,
                        Type = "Macro",
                        Icon = "📦",
                        FullPath = macro.MacroPath
                    };
                    // Add dummy node for lazy loading
                    macroNode.Children.Add(new MacroTreeNode { Name = "Loading...", Type = "Dummy" });
                    currentNode.Children.Add(macroNode);
                    macroNodeMap[macroKey] = macroNode;
                }
            }

            // Sort all nodes in-place recursively
            SortTreeNodesRecursive(rootNodes);

            return rootNodes;
        }

        private void SortTreeNodesRecursive(System.Collections.IList nodes)
        {
            if (nodes == null || nodes.Count == 0) return;

            // Copy nodes to a list for sorting
            var temp = new List<MacroTreeNode>();
            foreach (var item in nodes)
            {
                if (item is MacroTreeNode btn)
                {
                    temp.Add(btn);
                }
            }

            // Sort by name alphabetically
            var sorted = temp.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList();

            // Clear and rebuild
            nodes.Clear();
            foreach (var node in sorted)
            {
                nodes.Add(node);
                // Recursively sort children
                SortTreeNodesRecursive(node.Children);
            }
        }

        private void TvMacros_Expanded(object sender, RoutedEventArgs e)
        {
            var item = e.OriginalSource as TreeViewItem ?? e.Source as TreeViewItem;
            if (item == null)
            {
                var dep = e.OriginalSource as DependencyObject;
                while (dep != null && !(dep is TreeViewItem))
                {
                    dep = VisualTreeHelper.GetParent(dep);
                }
                item = dep as TreeViewItem;
            }

            if (item == null)
            {
                ConfiguratorService.LogDebug("TvMacros_Expanded: Could not resolve TreeViewItem container.");
                return;
            }

            var node = item.DataContext as MacroTreeNode ?? item.Header as MacroTreeNode;
            if (node == null)
            {
                ConfiguratorService.LogDebug($"TvMacros_Expanded: node is null. DataContext={item.DataContext?.GetType().FullName}, Header={item.Header?.GetType().FullName}");
                return;
            }

            ConfiguratorService.LogDebug($"TvMacros_Expanded: Expanding node '{node.Name}', Type={node.Type}, ChildrenCount={node.Children?.Count}");

            if (node.Type == "Macro")
            {
                // Check if it has a dummy child node
                if (node.Children.Count == 1 && node.Children[0].Type == "Dummy")
                {
                    node.Children.Clear();

                    var details = ConfiguratorService.LoadMacroFileDetails(node.FullPath);
                    ConfiguratorService.LogDebug($"TvMacros_Expanded: Loaded {details?.Count ?? 0} details for '{node.FullPath}'");

                    if (details != null && details.Count > 0)
                    {
                        foreach (var macro in details)
                        {
                            if (macro.IsPageMacro)
                            {
                                string pageDisplayName = macro.Name;
                                var pageNode = node.Children.FirstOrDefault(n => n.Type == "Page" && n.Name == pageDisplayName);
                                if (pageNode == null)
                                {
                                    pageNode = new MacroTreeNode
                                    {
                                        Name = pageDisplayName,
                                        Type = "Page",
                                        Icon = "📄",
                                        FullPath = macro.MacroPath,
                                        VariantIndex = macro.VariantIndex,
                                        RepresentationType = macro.RepresentationType
                                    };
                                    node.Children.Add(pageNode);
                                }

                                if (macro.Placeholders != null)
                                {
                                    foreach (var ph in macro.Placeholders)
                                    {
                                        AddPlaceholderToNode(pageNode, ph, macro.MacroPath);
                                    }
                                }
                            }
                            else
                            {
                                string repTypeName = GetRepresentationTypeName(macro.RepresentationType);
                                var repNode = node.Children.FirstOrDefault(n => n.Type == "RepType" && n.Name == repTypeName);
                                if (repNode == null)
                                {
                                    repNode = new MacroTreeNode
                                    {
                                        Name = repTypeName,
                                        Type = "RepType",
                                        Icon = "⚙️",
                                        FullPath = macro.MacroPath
                                    };
                                    node.Children.Add(repNode);
                                }

                                string variantName = GetVariantName(macro.VariantIndex);
                                var variantNode = repNode.Children.FirstOrDefault(n => n.Type == "Variant" && n.Name == variantName);
                                if (variantNode == null)
                                {
                                    variantNode = new MacroTreeNode
                                    {
                                        Name = variantName,
                                        Type = "Variant",
                                        Icon = "🔹",
                                        FullPath = macro.MacroPath,
                                        VariantIndex = macro.VariantIndex,
                                        RepresentationType = macro.RepresentationType
                                    };
                                    repNode.Children.Add(variantNode);
                                }

                                if (macro.Placeholders != null)
                                {
                                    foreach (var ph in macro.Placeholders)
                                    {
                                        AddPlaceholderToNode(variantNode, ph, macro.MacroPath);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        private void AddPlaceholderToNode(MacroTreeNode parentNode, string ph, string macroPath)
        {
            string rawPh = string.IsNullOrEmpty(ph) ? "" : ph;
            string phObjectName = "Variables";
            string varName = rawPh;

            if (rawPh.Contains(" : "))
            {
                var parts = rawPh.Split(new string[] { " : " }, StringSplitOptions.None);
                phObjectName = parts[0];
                varName = parts[1];
            }
            else if (rawPh.Contains("="))
            {
                var parts = rawPh.Split('=');
                phObjectName = parts[0];
                varName = parts[1];
            }

            var phNode = parentNode.Children.FirstOrDefault(n => n.Type == "PlaceHolder" && n.Name == phObjectName);
            if (phNode == null)
            {
                phNode = new MacroTreeNode
                {
                    Name = phObjectName,
                    Type = "PlaceHolder",
                    Icon = "⚓",
                    FullPath = macroPath
                };
                parentNode.Children.Add(phNode);
            }

            if (!phNode.Children.Any(n => n.Type == "Variable" && n.Name == varName))
            {
                phNode.Children.Add(new MacroTreeNode
                {
                    Name = varName,
                    Type = "Variable",
                    Icon = "🔹",
                    FullPath = macroPath
                });
            }
        }

        private static List<string> SplitMacroPath(string macroPath)
        {
            string cleanPath = macroPath;
            if (cleanPath.StartsWith("$("))
            {
                int idx = cleanPath.IndexOf(')');
                if (idx != -1 && idx < cleanPath.Length - 1)
                {
                    cleanPath = cleanPath.Substring(idx + 1);
                }
            }
            cleanPath = cleanPath.TrimStart('\\', '/');
            var parts = cleanPath.Split(new char[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            return parts;
        }

        private static string GetVariantName(int index)
        {
            if (index >= 0 && index < 16)
            {
                return "Variant " + (char)('A' + index);
            }
            return "Variant A";
        }

        private static string GetRepresentationTypeName(int repType)
        {
            switch (repType)
            {
                case 1: return "Multi-line";
                case 2: return "Single-line";
                case 3: return "Pair cross-reference";
                case 4: return "Overview";
                case 5: return "Graphics";
                case 6: return "Panel layout";
                case 7: return "Fluid power schematic";
                case 8: return "P&ID";
                case 9: return "Pre-planning";
                case 10: return "Topology";
                case 11: return "3D mounting layout";
                case 12: return "Functional";
                default: return "Multi-line";
            }
        }

        public class MacroTreeNode
        {
            public string Name { get; set; }
            public string Type { get; set; } // "Root", "Project", "Folder", "Macro", "RepType", "Variant", "PlaceHolder"
            public string FullPath { get; set; }
            public string Icon { get; set; }
            public string Description { get; set; }
            public int VariantIndex { get; set; }
            public int RepresentationType { get; set; }
            public bool IsExpanded { get; set; } = false;
            public ObservableCollection<MacroTreeNode> Children { get; set; } = new ObservableCollection<MacroTreeNode>();
        }

        // ==================== PLACEHOLDER VARIABLES MAPPINGS ====================
        private void BtnAddVarMapping_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            // Find parent items control datacontext (which is MacroElement)
            DependencyObject parent = VisualTreeHelper.GetParent(btn);
            while (parent != null && !(parent is ContentPresenter))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            if (parent != null)
            {
                var element = ((ContentPresenter)parent).Content as MacroElement;
                if (element != null)
                {
                    element.VariableMappings.Add(new MappingEntry("新占位符", ""));
                    if (_selectedItem is MacroTypical typical)
                    {
                        BindElementsList(typical.Elements);
                    }
                }
            }
        }

        private void BtnShareVariable_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            
            var mapping = btn.DataContext as MappingEntry;
            if (mapping == null) return;

            if (_selectedItem is MacroTypical typical)
            {
                var existingVar = typical.Variables.FirstOrDefault(v => v.Name == mapping.Key);
                if (existingVar == null)
                {
                    typical.Variables.Add(new ConfigurationVariable
                    {
                        Name = mapping.Key,
                        Type = "String",
                        DisplayName = mapping.Key
                    });
                    
                    BindVariablesList(typical.Variables);
                }
                
                mapping.Value = mapping.Key;
            }
        }

        private void BtnDeleteVarMapping_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var entry = btn.DataContext as MappingEntry;
            if (entry == null) return;

            // Find parent ItemsControl datacontext (MacroElement)
            DependencyObject parent = VisualTreeHelper.GetParent(btn);
            while (parent != null && !(parent is Border))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            if (parent != null)
            {
                var element = ((Border)parent).DataContext as MacroElement;
                if (element != null)
                {
                    element.VariableMappings.Remove(entry);
                    if (_selectedItem is MacroTypical typical)
                    {
                        BindElementsList(typical.Elements);
                    }
                }
            }
        }

    }

    internal static class ShellThumbnail
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE
        {
            public int cx;
            public int cy;
            public SIZE(int cx, int cy)
            {
                this.cx = cx;
                this.cy = cy;
            }
        }

        [ComImport]
        [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler([In] IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, [Out] out IntPtr ppv);
            void GetParent([Out] out IShellItem ppsi);
            void GetDisplayName([In] uint sigdnName, [Out] out IntPtr ppszName);
            void GetAttributes([In] uint sfgaoMask, [Out] out uint psfgaoAttribs);
            void Compare([In] IShellItem psi, [In] uint hint, [Out] out int piOrder);
        }

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            void GetImage([In, MarshalAs(UnmanagedType.Struct)] SIZE size, [In] int flags, [Out] out IntPtr phbmp);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [In] ref Guid riid,
            [Out, MarshalAs(UnmanagedType.Interface)] out IShellItem ppsi);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        public static System.Windows.Media.Imaging.BitmapSource GetThumbnail(string fileName, int width, int height)
        {
            Guid shellItemGuid = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
            IShellItem shellItem;
            SHCreateItemFromParsingName(fileName, IntPtr.Zero, ref shellItemGuid, out shellItem);

            var imageFactory = (IShellItemImageFactory)shellItem;
            IntPtr hBitmap;
            imageFactory.GetImage(new SIZE(width, height), 0x0, out hBitmap);

            try
            {
                var bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    System.Windows.Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

                return bitmapSource;
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }
    }
}
