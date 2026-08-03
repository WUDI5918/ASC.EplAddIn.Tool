using System;
using System.Collections.Generic;
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

namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    public partial class ConfiguratorWindow : Window
    {
        // ==================== EBUILD STYLE INTERACTIONS & TRIGGERS ====================
        private void ItrElements_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedMacroElement = ItrElements.SelectedItem as MacroElement;
            if (_selectedMacroElement != null)
            {
                ItrVariables.SelectionChanged -= ItrVariables_SelectionChanged;
                ItrVariables.SelectedItem = null;
                ItrVariables.SelectionChanged += ItrVariables_SelectionChanged;

                if (ItrInstances != null)
                {
                    ItrInstances.SelectionChanged -= ItrInstances_SelectionChanged;
                    ItrInstances.SelectedItem = null;
                    ItrInstances.SelectionChanged += ItrInstances_SelectionChanged;
                }

                _selectedConfigVariable = null;
                GridVariableDetails.Visibility = Visibility.Collapsed;
                if (GridInstanceDetails != null) GridInstanceDetails.Visibility = Visibility.Collapsed;

                GridDetailsEmpty.Visibility = Visibility.Collapsed;
                GridMacroDetails.Visibility = Visibility.Visible;
                GridMacroDetails.DataContext = null;
                GridMacroDetails.DataContext = _selectedMacroElement;

                // Bind Active Formula Status radio buttons
                if (string.IsNullOrEmpty(_selectedMacroElement.IsActiveFormula))
                {
                    RadStatusAlways.IsChecked = true;
                    if (TxtIsActiveFormula != null) TxtIsActiveFormula.IsEnabled = false;
                }
                else
                {
                    RadStatusDependent.IsChecked = true;
                    if (TxtIsActiveFormula != null) TxtIsActiveFormula.IsEnabled = true;
                }

                // Bind Position Mode radio buttons
                if (string.Equals(_selectedMacroElement.PositionMode, "FromMacro", StringComparison.OrdinalIgnoreCase))
                {
                    RadPosMacro.IsChecked = true;
                    ChkPosSequential.IsChecked = false;
                }
                else if (string.Equals(_selectedMacroElement.PositionMode, "Absolute", StringComparison.OrdinalIgnoreCase))
                {
                    RadPosAbsolute.IsChecked = true;
                    ChkPosSequential.IsChecked = false;
                }
                else if (string.Equals(_selectedMacroElement.PositionMode, "Sequential", StringComparison.OrdinalIgnoreCase))
                {
                    RadPosAbsolute.IsChecked = true;
                    ChkPosSequential.IsChecked = true;
                }

                // Bind Structure checkboxes
                ChkStructureMacro.IsChecked = _selectedMacroElement.StructureMappings == null || _selectedMacroElement.StructureMappings.All(m => string.IsNullOrEmpty(m.Value));
            }
            else
            {
                GridMacroDetails.Visibility = Visibility.Collapsed;
                if (_selectedConfigVariable == null && GridInstanceDetails?.Visibility != Visibility.Visible)
                {
                    GridDetailsEmpty.Visibility = Visibility.Visible;
                }
            }
        }

        private void ItrVariables_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedConfigVariable = ItrVariables.SelectedItem as ConfigurationVariable;
            if (_selectedConfigVariable != null)
            {
                ItrElements.SelectionChanged -= ItrElements_SelectionChanged;
                ItrElements.SelectedItem = null;
                ItrElements.SelectionChanged += ItrElements_SelectionChanged;

                if (ItrInstances != null)
                {
                    ItrInstances.SelectionChanged -= ItrInstances_SelectionChanged;
                    ItrInstances.SelectedItem = null;
                    ItrInstances.SelectionChanged += ItrInstances_SelectionChanged;
                }

                _selectedMacroElement = null;
                GridMacroDetails.Visibility = Visibility.Collapsed;
                if (GridInstanceDetails != null) GridInstanceDetails.Visibility = Visibility.Collapsed;

                GridDetailsEmpty.Visibility = Visibility.Collapsed;
                GridVariableDetails.Visibility = Visibility.Visible;
                GridVariableDetails.DataContext = null;
                GridVariableDetails.DataContext = _selectedConfigVariable;
            }
            else
            {
                GridVariableDetails.Visibility = Visibility.Collapsed;
                if (_selectedMacroElement == null && GridInstanceDetails?.Visibility != Visibility.Visible)
                {
                    GridDetailsEmpty.Visibility = Visibility.Visible;
                }
            }
        }

        private void ItrInstances_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ItrInstances == null) return;
            var selectedInstance = ItrInstances.SelectedItem as TypicalInstance;
            if (selectedInstance != null)
            {
                ItrElements.SelectionChanged -= ItrElements_SelectionChanged;
                ItrElements.SelectedItem = null;
                ItrElements.SelectionChanged += ItrElements_SelectionChanged;

                ItrVariables.SelectionChanged -= ItrVariables_SelectionChanged;
                ItrVariables.SelectedItem = null;
                ItrVariables.SelectionChanged += ItrVariables_SelectionChanged;

                _selectedMacroElement = null;
                _selectedConfigVariable = null;
                GridMacroDetails.Visibility = Visibility.Collapsed;
                GridVariableDetails.Visibility = Visibility.Collapsed;

                GridDetailsEmpty.Visibility = Visibility.Collapsed;
                if (GridInstanceDetails != null)
                {
                    GridInstanceDetails.Visibility = Visibility.Visible;
                    GridInstanceDetails.DataContext = null;
                    GridInstanceDetails.DataContext = selectedInstance;

                    if (string.IsNullOrEmpty(selectedInstance.IsActiveFormula))
                    {
                        RadInstanceActiveAlways.IsChecked = true;
                        if (TxtInstanceActiveFormula != null) TxtInstanceActiveFormula.IsEnabled = false;
                    }
                    else
                    {
                        RadInstanceActiveDependent.IsChecked = true;
                        if (TxtInstanceActiveFormula != null) TxtInstanceActiveFormula.IsEnabled = true;
                    }
                }
            }
            else
            {
                if (GridInstanceDetails != null) GridInstanceDetails.Visibility = Visibility.Collapsed;
                if (_selectedMacroElement == null && _selectedConfigVariable == null)
                {
                    GridDetailsEmpty.Visibility = Visibility.Visible;
                }
            }
        }

        private void RadInstanceActiveAlways_Checked(object sender, RoutedEventArgs e)
        {
            if (ItrInstances == null) return;
            var instance = ItrInstances.SelectedItem as TypicalInstance;
            if (instance == null) return;
            instance.IsActiveFormula = "";
            if (TxtInstanceActiveFormula != null)
            {
                TxtInstanceActiveFormula.Text = "";
                TxtInstanceActiveFormula.IsEnabled = false;
            }
            MarkDirty();
        }

        private void RadInstanceActiveDependent_Checked(object sender, RoutedEventArgs e)
        {
            if (TxtInstanceActiveFormula != null) TxtInstanceActiveFormula.IsEnabled = true;
        }

        private void BtnCollapseLeft_Click(object sender, RoutedEventArgs e)
        {
            ColSelectionArea.Width = new GridLength(0);
        }

        private void BtnExpandLeft_Click(object sender, RoutedEventArgs e)
        {
            ColSelectionArea.Width = new GridLength(240);
        }

        private void BtnCollapseRight_Click(object sender, RoutedEventArgs e)
        {
            ColDetailsArea.Width = new GridLength(0);
        }

        private void BtnExpandRight_Click(object sender, RoutedEventArgs e)
        {
            ColDetailsArea.Width = new GridLength(300);
        }

        private void ColSelectionAreaGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width <= 10)
            {
                if (LeftSplitter != null) LeftSplitter.Visibility = Visibility.Collapsed;
                if (BtnExpandLeft != null) BtnExpandLeft.Visibility = Visibility.Visible;
            }
            else
            {
                if (LeftSplitter != null) LeftSplitter.Visibility = Visibility.Visible;
                if (BtnExpandLeft != null) BtnExpandLeft.Visibility = Visibility.Collapsed;
            }
        }

        private void ColDetailsAreaGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width <= 10)
            {
                if (RightSplitter != null) RightSplitter.Visibility = Visibility.Collapsed;
                if (BtnExpandRight != null) BtnExpandRight.Visibility = Visibility.Visible;
            }
            else
            {
                if (RightSplitter != null) RightSplitter.Visibility = Visibility.Visible;
                if (BtnExpandRight != null) BtnExpandRight.Visibility = Visibility.Collapsed;
            }
        }

        private void BtnToggleSearch_Click(object sender, RoutedEventArgs e)
        {
            if (TxtSearch == null) return;
            if (TxtSearch.Visibility == Visibility.Visible)
            {
                TxtSearch.Visibility = Visibility.Collapsed;
                TxtSearch.Text = "";
            }
            else
            {
                TxtSearch.Visibility = Visibility.Visible;
                TxtSearch.Focus();
            }
        }

        private void BtnAddNewFolder_Click(object sender, RoutedEventArgs e)
        {
            if (ModalCreateItem == null || TxtModalCreateTitle == null || TxtModalCreateName == null) return;

            string title = "新建文件夹 (Create Folder)";
            string defaultPrefix = "";
            if (_selectedTab == 0) defaultPrefix = "New_Folder/MT_";
            else if (_selectedTab == 1) defaultPrefix = "New_Folder/TG_";
            else if (_selectedTab == 2) defaultPrefix = "New_Folder/Config_";
            else return;

            TxtModalCreateTitle.Text = title;
            TxtModalCreateName.Text = defaultPrefix;
            ModalCreateItem.Visibility = Visibility.Visible;
            TxtModalCreateName.Focus();
            TxtModalCreateName.SelectionStart = defaultPrefix.Length;
            TxtModalCreateName_TextChanged(null, null);
        }

        private void BtnElementMenu_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;
            var element = btn.DataContext as MacroElement;
            if (element == null) return;

            var menu = new ContextMenu();

            var previewItem = new MenuItem { Header = "👁 Preview" };
            previewItem.Click += (s, ev) =>
            {
                char varChar = (char)('A' + element.VariantIndex);
                string msg = $"正在显示宏预览:\n\n路径: {element.MacroPath}\n变体: Variant {varChar}";
                MessageBox.Show(msg, "显示预览", MessageBoxButton.OK, MessageBoxImage.Information);
            };

            var replaceItem = new MenuItem { Header = "⇅ Replace" };
            replaceItem.Click += (s, ev) => ShowMacroNavigator(isAddingNew: false, target: element);

            var duplicateItem = new MenuItem { Header = "📑 Duplicate" };
            duplicateItem.Click += (s, ev) =>
            {
                if (_selectedItem is MacroTypical typical)
                {
                    int idx = typical.Elements.IndexOf(element);
                    string json = JsonConvert.SerializeObject(element);
                    var copy = JsonConvert.DeserializeObject<MacroElement>(json);
                    if (idx != -1) typical.Elements.Insert(idx + 1, copy);
                    else typical.Elements.Add(copy);
                    BindElementsList(typical.Elements);
                    MarkDirty();
                    ShowToast("已复制宏");
                }
            };

            var deleteItem = new MenuItem { Header = "🗑 Delete" };
            deleteItem.Click += (s, ev) =>
            {
                if (_selectedItem is MacroTypical typical)
                {
                    ShowDeleteConfirmation("确认删除", "您确定要删除此宏元素吗？", () =>
                    {
                        typical.Elements.Remove(element);
                        BindElementsList(typical.Elements);
                        MarkDirty();
                        ShowToast("已删除宏");
                    });
                }
            };

            menu.Items.Add(previewItem);
            menu.Items.Add(replaceItem);
            menu.Items.Add(duplicateItem);
            menu.Items.Add(deleteItem);

            btn.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private void BtnEditSelectionValues_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedConfigVariable == null) return;

            // Create a simple custom input dialog styled in dark theme
            Window dlg = new Window
            {
                Title = "编辑预设下拉选项",
                Width = 350,
                Height = 320,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                Foreground = Brushes.White,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            Grid grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            TextBlock lbl = new TextBlock
            {
                Text = "请输入下拉选项(每行输入一个选项):",
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8),
                FontSize = 12
            };
            grid.Children.Add(lbl);

            TextBox txt = new TextBox
            {
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(51, 51, 51)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(5),
                Text = string.Join(Environment.NewLine, _selectedConfigVariable.PredefinedValues ?? new List<string>())
            };
            Grid.SetRow(txt, 1);
            grid.Children.Add(txt);

            StackPanel btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };
            Button btnOk = new Button { Content = "确定", Width = 70, Height = 26, Margin = new Thickness(0, 0, 10, 0), Background = new SolidColorBrush(Color.FromRgb(0, 132, 255)), Foreground = Brushes.White };
            Button btnCancel = new Button { Content = "取消", Width = 70, Height = 26, Background = new SolidColorBrush(Color.FromRgb(60, 60, 60)), Foreground = Brushes.White };

            btnOk.Click += (s, ev) =>
            {
                var list = txt.Text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(line => line.Trim())
                                   .Where(line => !string.IsNullOrEmpty(line))
                                   .ToList();
                _selectedConfigVariable.PredefinedValues = list;

                GridVariableDetails.DataContext = null;
                GridVariableDetails.DataContext = _selectedConfigVariable;

                dlg.DialogResult = true;
                dlg.Close();
                MarkDirty();
                ShowToast("已更新预设下拉选项");
            };

            btnCancel.Click += (s, ev) => dlg.Close();

            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            Grid.SetRow(btnPanel, 2);
            grid.Children.Add(btnPanel);

            dlg.Content = grid;
            dlg.ShowDialog();
        }

        private void RadStatusAlways_Checked(object sender, RoutedEventArgs e)
        {
            if (_selectedMacroElement == null) return;
            _selectedMacroElement.IsActiveFormula = "";
            if (TxtIsActiveFormula != null)
            {
                TxtIsActiveFormula.Text = "";
                TxtIsActiveFormula.IsEnabled = false;
            }
            MarkDirty();
        }

        private void RadStatusDependent_Checked(object sender, RoutedEventArgs e)
        {
            if (_selectedMacroElement == null) return;
            if (TxtIsActiveFormula != null)
            {
                TxtIsActiveFormula.IsEnabled = true;
                TxtIsActiveFormula.Focus();
            }
        }

        private void RadPosMacro_Checked(object sender, RoutedEventArgs e)
        {
            if (_selectedMacroElement == null) return;
            _selectedMacroElement.PositionMode = "FromMacro";
            MarkDirty();
        }

        private void RadPosAbsolute_Checked(object sender, RoutedEventArgs e)
        {
            if (_selectedMacroElement == null) return;
            _selectedMacroElement.PositionMode = "Absolute";
            MarkDirty();
        }

        private void ChkPosSequential_Checked(object sender, RoutedEventArgs e)
        {
            if (_selectedMacroElement == null) return;
            bool isChecked = (sender as CheckBox)?.IsChecked == true;
            if (isChecked)
            {
                _selectedMacroElement.PositionMode = "Sequential";
            }
            else
            {
                _selectedMacroElement.PositionMode = RadPosAbsolute?.IsChecked == true ? "Absolute" : "FromMacro";
            }
            MarkDirty();
        }

        public void ShowToast(string message)
        {
            if (ToastText == null || ToastBorder == null) return;
            ToastText.Text = message;
            ToastBorder.Visibility = Visibility.Visible;

            if (_toastTimer == null)
            {
                _toastTimer = new System.Windows.Threading.DispatcherTimer();
                _toastTimer.Interval = TimeSpan.FromSeconds(3.5);
                _toastTimer.Tick += (s, e) =>
                {
                    ToastBorder.Visibility = Visibility.Collapsed;
                    _toastTimer.Stop();
                };
            }
            else
            {
                _toastTimer.Stop();
            }
            _toastTimer.Start();
        }

        private void BtnCloseToast_Click(object sender, RoutedEventArgs e)
        {
            if (ToastBorder != null) ToastBorder.Visibility = Visibility.Collapsed;
            _toastTimer?.Stop();
        }

        private void UpdateBreadcrumbs()
        {
            if (TxtBreadcrumbs == null) return;

            string tabName = "Macro Typicals";
            if (_selectedTab == 0) tabName = "Macro Typicals";
            else if (_selectedTab == 1) tabName = "Typical Groups";
            else if (_selectedTab == 2) tabName = "Configurators";
            else if (_selectedTab == 3) tabName = "Project Builder";

            string itemName = "";
            if (_selectedItem != null)
            {
                if (_selectedItem is MacroTypical t) itemName = t.Name;
                else if (_selectedItem is TypicalGroup g) itemName = g.Name;
                else if (_selectedItem is Configurator c) itemName = c.Name;
            }

            if (string.IsNullOrEmpty(itemName))
            {
                TxtBreadcrumbs.Text = $"Designer > ASC_Macro_GettingStarted > {tabName}";
            }
            else
            {
                TxtBreadcrumbs.Text = $"Designer > ASC_Macro_GettingStarted > {tabName} > {itemName}";
            }
        }

        // ==================== NEW OVERLAY MODAL HANDLERS & HELPERS ====================
        private void BtnAddScriptTypical_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            ShowToast("Script Typicals are not supported yet.");
        }



        private void BtnAddConfigGroup_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true; // Prevent Expander toggle!
            if (!(_selectedItem is Configurator config)) return;

            if (ModalAddElements == null || TxtModalAddTitle == null || TxtModalAddSubtitle == null || LstModalAddElements == null) return;

            TxtModalAddTitle.Text = "Add elements";
            TxtModalAddSubtitle.Text = "Select elements to be added to the configurator.";

            // Populate all available TypicalGroups
            _allModalAddElements.Clear();

            foreach (var tg in _db.TypicalGroups)
            {
                bool alreadyAdded = config.TypicalGroupRefs != null && config.TypicalGroupRefs.Contains(tg.Name);
                if (alreadyAdded) continue; // Exclude already associated ones

                _allModalAddElements.Add(new CheckableAddElement
                {
                    Name = tg.Name,
                    Type = "TypicalGroup",
                    Icon = "👥",
                    IsChecked = false
                });
            }

            foreach (var mt in _db.MacroTypicals)
            {
                string wrappedName = "TG_" + mt.Name;
                bool alreadyAdded = config.TypicalGroupRefs != null && config.TypicalGroupRefs.Contains(wrappedName);
                if (alreadyAdded) continue;

                _allModalAddElements.Add(new CheckableAddElement
                {
                    Name = mt.Name,
                    Type = "MacroTypical",
                    Icon = "🧿",
                    IsChecked = false
                });
            }

            TxtModalAddSearch.Text = "";
            FilterModalAddElements();

            ModalAddElements.Visibility = Visibility.Visible;
            UpdateModalAddSelectedCount();
        }

        private Action _onDeleteConfirmed;

        private void ShowDeleteConfirmation(string title, string message, Action onConfirm)
        {
            if (ModalConfirmDelete == null || TxtModalDeleteTitle == null || TxtModalDeleteMessage == null) return;
            TxtModalDeleteTitle.Text = title;
            TxtModalDeleteMessage.Text = message;
            _onDeleteConfirmed = onConfirm;
            ModalConfirmDelete.Visibility = Visibility.Visible;
        }

        private void BtnCloseDeleteModal_Click(object sender, RoutedEventArgs e)
        {
            if (ModalConfirmDelete != null)
            {
                ModalConfirmDelete.Visibility = Visibility.Collapsed;
            }
            _onDeleteConfirmed = null;
        }

        private void BtnModalDeleteConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (ModalConfirmDelete != null)
            {
                ModalConfirmDelete.Visibility = Visibility.Collapsed;
            }
            var callback = _onDeleteConfirmed;
            _onDeleteConfirmed = null;
            callback?.Invoke();
        }

        private void BtnCloseCreateModal_Click(object sender, RoutedEventArgs e)
        {
            if (ModalCreateItem != null)
            {
                ModalCreateItem.Visibility = Visibility.Collapsed;
            }
        }

        private void TxtModalCreateName_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (BtnModalCreateConfirm == null || TxtModalCreateName == null) return;

            string text = TxtModalCreateName.Text.Trim();
            bool isValid = !string.IsNullOrEmpty(text);

            if (_selectedTab == 0 && text == "MT_") isValid = false;
            else if (_selectedTab == 1 && text == "TG_") isValid = false;

            BtnModalCreateConfirm.IsEnabled = isValid;
        }

        private void BtnModalCreateConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (TxtModalCreateName == null) return;
            string newName = TxtModalCreateName.Text.Trim();
            if (string.IsNullOrEmpty(newName)) return;

            bool isUnique = true;
            if (_selectedTab == 0)
            {
                isUnique = !_db.MacroTypicals.Any(t => string.Equals(t.Name, newName, StringComparison.OrdinalIgnoreCase));
            }
            else if (_selectedTab == 1)
            {
                isUnique = !_db.TypicalGroups.Any(g => string.Equals(g.Name, newName, StringComparison.OrdinalIgnoreCase));
            }
            else if (_selectedTab == 2)
            {
                isUnique = !_db.Configurators.Any(c => string.Equals(c.Name, newName, StringComparison.OrdinalIgnoreCase));
            }

            if (!isUnique)
            {
                MessageBox.Show($"Name '{newName}' already exists. Please choose a different name.", "Naming Conflict", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            object createdItem = null;
            string itemTypeName = "";

            if (_selectedTab == 0)
            {
                var mt = new MacroTypical { Name = newName };
                _db.MacroTypicals.Add(mt);
                createdItem = mt;
                itemTypeName = "Macro Typical";
            }
            else if (_selectedTab == 1)
            {
                var tg = new TypicalGroup { Name = newName };
                _db.TypicalGroups.Add(tg);
                createdItem = tg;
                itemTypeName = "Typical Group";
            }
            else if (_selectedTab == 2)
            {
                var cfg = new Configurator { Name = newName };
                _db.Configurators.Add(cfg);
                createdItem = cfg;
                itemTypeName = "configurator";
            }

            if (createdItem != null)
            {
                _selectedItem = createdItem;
                InvalidateTypicalTargetsCache();
                RefreshSidebar();
                LstSidebarItems.SelectedItem = _selectedItem;
                UpdateWorkspaceSelection();
                MarkDirty();

                ShowToast($"The {itemTypeName} '{newName}' was created");
            }

            ModalCreateItem.Visibility = Visibility.Collapsed;
        }

        private void BtnCloseAddModal_Click(object sender, RoutedEventArgs e)
        {
            if (ModalAddElements != null)
            {
                ModalAddElements.Visibility = Visibility.Collapsed;
            }
        }

        private void TxtModalAddSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_modalSearchDebounceTimer == null)
            {
                _modalSearchDebounceTimer = new System.Windows.Threading.DispatcherTimer();
                _modalSearchDebounceTimer.Interval = TimeSpan.FromMilliseconds(200);
                _modalSearchDebounceTimer.Tick += (_, __) =>
                {
                    _modalSearchDebounceTimer.Stop();
                    FilterModalAddElements();
                };
            }
            _modalSearchDebounceTimer.Stop();
            _modalSearchDebounceTimer.Start();
        }

        private void CboModalAddFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            FilterModalAddElements();
        }

        private void ChkModalElement_Checked(object sender, RoutedEventArgs e)
        {
            UpdateModalAddSelectedCount();
        }

        private void ChkModalElement_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateModalAddSelectedCount();
        }

        private void FilterModalAddElements()
        {
            if (LstModalAddElements == null) return;

            string search = TxtModalAddSearch?.Text?.Trim()?.ToLower() ?? "";
            int filterIndex = CboModalAddFilter?.SelectedIndex ?? 0;

            var filtered = _allModalAddElements.Where(item =>
            {
                if (filterIndex == 1 && item.Type != "MacroTypical") return false;
                if (filterIndex == 2 && item.Type != "TypicalGroup") return false;

                if (string.IsNullOrEmpty(search)) return true;
                return item.Name.ToLower().Contains(search);
            }).ToList();

            LstModalAddElements.ItemsSource = null;
            LstModalAddElements.ItemsSource = filtered;
        }

        private void UpdateModalAddSelectedCount()
        {
            if (TxtModalAddSelectedCount == null || BtnModalAddConfirm == null) return;

            int checkedCount = _allModalAddElements.Count(item => item.IsChecked);
            TxtModalAddSelectedCount.Text = $"Elements selected: {checkedCount}";
            BtnModalAddConfirm.IsEnabled = checkedCount > 0;
        }

        private void BtnModalAddConfirm_Click(object sender, RoutedEventArgs e)
        {
            var checkedItems = _allModalAddElements.Where(item => item.IsChecked).ToList();
            if (checkedItems.Count == 0) return;

            if (_selectedItem is TypicalGroup group)
            {
                int addedCount = 0;
                foreach (var item in checkedItems)
                {
                    var instance = new TypicalInstance
                    {
                        TargetName = item.Name,
                        Type = item.Type,
                        IsActiveFormula = ""
                    };

                    List<ConfigurationVariable> childVars = null;
                    if (item.Type.Equals("MacroTypical", StringComparison.OrdinalIgnoreCase))
                    {
                        var child = _db.MacroTypicals.FirstOrDefault(t => string.Equals(t.Name, item.Name, StringComparison.OrdinalIgnoreCase));
                        childVars = child?.Variables;
                    }
                    else if (item.Type.Equals("TypicalGroup", StringComparison.OrdinalIgnoreCase))
                    {
                        var child = _db.TypicalGroups.FirstOrDefault(g => string.Equals(g.Name, item.Name, StringComparison.OrdinalIgnoreCase));
                        childVars = child?.Variables;
                    }

                    if (childVars != null)
                    {
                        foreach (var v in childVars)
                        {
                            instance.VariableMappings.Add(new MappingEntry(v.Name, ""));
                        }
                    }

                    group.Instances.Add(instance);
                    addedCount++;
                }

                BindInstancesList(group.Instances);
                MarkDirty();
                ShowToast($"{addedCount} elements were added.");
            }
            else if (_selectedItem is Configurator config)
            {
                int addedCount = 0;
                bool showWarning = false;
                List<string> warnings = new List<string>();

                foreach (var item in checkedItems)
                {
                    if (item.Type == "TypicalGroup")
                    {
                        if (config.TypicalGroupRefs == null) config.TypicalGroupRefs = new List<string>();
                        if (!config.TypicalGroupRefs.Contains(item.Name))
                        {
                            config.TypicalGroupRefs.Add(item.Name);
                            addedCount++;
                        }
                    }
                    else if (item.Type == "MacroTypical")
                    {
                        string wrappedGroupName = "TG_" + item.Name;
                        var tg = _db.TypicalGroups.FirstOrDefault(g => string.Equals(g.Name, wrappedGroupName, StringComparison.OrdinalIgnoreCase));
                        if (tg == null)
                        {
                            tg = new TypicalGroup { Name = wrappedGroupName };
                            var mt = _db.MacroTypicals.FirstOrDefault(t => string.Equals(t.Name, item.Name, StringComparison.OrdinalIgnoreCase));
                            if (mt != null)
                            {
                                foreach (var v in mt.Variables)
                                {
                                    tg.Variables.Add(new ConfigurationVariable
                                    {
                                        Name = v.Name,
                                        DisplayName = v.DisplayName,
                                        Type = v.Type,
                                        IsMandatory = v.IsMandatory,
                                        VisibilityFormula = v.VisibilityFormula,
                                        PredefinedValues = new List<string>(v.PredefinedValues)
                                    });
                                }
                            }

                            var inst = new TypicalInstance
                            {
                                TargetName = item.Name,
                                Type = "MacroTypical",
                                IsActiveFormula = ""
                            };
                            if (mt != null)
                            {
                                foreach (var v in mt.Variables)
                                {
                                    inst.VariableMappings.Add(new MappingEntry(v.Name, "=" + v.Name));
                                }
                            }
                            tg.Instances.Add(inst);
                            _db.TypicalGroups.Add(tg);
                        }

                        if (config.TypicalGroupRefs == null) config.TypicalGroupRefs = new List<string>();
                        if (!config.TypicalGroupRefs.Contains(tg.Name))
                        {
                            config.TypicalGroupRefs.Add(tg.Name);
                            addedCount++;
                            showWarning = true;
                            warnings.Add($"Macro Typical '{item.Name}' cannot be added directly to a configurator. It has been wrapped in a new Typical Group '{tg.Name}'.");
                        }
                    }
                }

                BindConfigGroupsList(config);
                MarkDirty();

                if (showWarning)
                {
                    MessageBox.Show(string.Join("\n\n", warnings), "Wrapper Info", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                ShowToast($"{addedCount} elements were added.");
            }

            ModalAddElements.Visibility = Visibility.Collapsed;
        }
    }

    public class CheckableAddElement : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    OnPropertyChanged(nameof(IsChecked));
                }
            }
        }
        public string Name { get; set; }
        public string Type { get; set; }
        public string Icon { get; set; }

        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name));
    }

    public class TypicalTargetInfo
    {
        public string Name { get; set; }
        public string Type { get; set; }
    }

    public class CheckableTypicalGroup
    {
        public string Name { get; set; }
        public bool IsChecked { get; set; }
    }

    public class StringListConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is List<string> list && list != null)
            {
                return string.Join(", ", list);
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string str)
            {
                return str.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(s => s.Trim())
                          .Where(s => !string.IsNullOrEmpty(s))
                          .ToList();
            }
            return new List<string>();
        }
    }

    public class MacroElementHeaderConverter : System.Windows.Data.IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (values == null || values.Length < 2) return "";

            var element = values[1] as MacroElement;
            if (element == null) return "";

            int index = 0;
            if (values[0] is ConfiguratorWindow window)
            {
                if (window._selectedItem is MacroTypical typical)
                {
                    index = typical.Elements.IndexOf(element);
                }
            }
            if (index < 0) index = 0;

            string formattedPath = "";
            if (!string.IsNullOrEmpty(element.MacroPath))
            {
                string clean = element.MacroPath;
                if (clean.StartsWith("$("))
                {
                    int idx = clean.IndexOf(')');
                    if (idx != -1 && idx < clean.Length - 1)
                    {
                        clean = clean.Substring(idx + 1);
                    }
                }
                clean = clean.TrimStart('\\', '/');
                var parts = clean.Split(new char[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                formattedPath = string.Join(" » ", parts);
            }
            else
            {
                formattedPath = "未指定宏文件";
            }

            char varChar = (char)('A' + element.VariantIndex);
            return $"{index}: EPLAN_Macro » {formattedPath} » Variant '{varChar}'";
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class EmpPathToVisibilityConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string path && !string.IsNullOrEmpty(path))
            {
                string trim = path.Trim();
                if (trim.EndsWith(".ema", StringComparison.OrdinalIgnoreCase) || trim.EndsWith(".ems", StringComparison.OrdinalIgnoreCase))
                {
                    return System.Windows.Visibility.Collapsed;
                }
            }
            return System.Windows.Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class MacroPathToMarginConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string path && !string.IsNullOrEmpty(path))
            {
                string trim = path.Trim();
                if (trim.EndsWith(".ema", StringComparison.OrdinalIgnoreCase) || trim.EndsWith(".ems", StringComparison.OrdinalIgnoreCase))
                {
                    return new System.Windows.Thickness(20, 0, 0, 12);
                }
            }
            return new System.Windows.Thickness(0, 0, 0, 12);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class MacroPathToFileNameConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string path && !string.IsNullOrEmpty(path))
            {
                try
                {
                    return System.IO.Path.GetFileName(path);
                }
                catch
                {
                    return path;
                }
            }
            return "未指定宏文件";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class VariantIndexToNameConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is int index)
            {
                if (index >= 0 && index < 16)
                {
                    return "Variant " + (char)('A' + index);
                }
            }
            return "Variant A";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class RepresentationTypeToNameConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is int repType)
            {
                switch (repType)
                {
                    case 1: return "Multi-line";
                    case 2: return "Single-line";
                    case 3: return "Pair cross-reference";
                    case 4: return "Overview";
                    case 5: return "Graphics";
                    case 6: return "Panel layout";
                    case 7: return "Schematic Fluid power";
                    case 8: return "P&ID";
                    case 9: return "Pre-planning";
                    case 10: return "Topology";
                    case 11: return "Bus topology";
                    case 12: return "Functional";
                }
            }
            return "Multi-line";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class RowIndexConverter : System.Windows.Data.IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (values == null || values.Length == 0) return 0;

            if (values[0] is System.Windows.Controls.DataGridRow row)
            {
                return row.GetIndex();
            }

            if (values.Length >= 2)
            {
                var item = values[0];
                var itemsControl = values[1] as System.Windows.Controls.ItemsControl;
                if (item != null && itemsControl != null)
                {
                    int index = itemsControl.Items.IndexOf(item);
                    return index >= 0 ? index : 0;
                }
            }

            return 0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}