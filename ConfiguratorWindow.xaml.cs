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
using System.Windows.Media.Imaging;

namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    public partial class ConfiguratorWindow : Window
    {
        private const string DbFileName = "configurator_data.json";
        private string _dbFilePath;
        private readonly ConfiguratorViewModel _viewModel = new ConfiguratorViewModel();
        private ConfiguratorData _db { get => _viewModel.Database; set => _viewModel.Database = value; }
        private int _selectedTab = 0; // 0: Macro-Typical, 1: Typical-Group, 2: Configurator
        internal object _selectedItem = null;
        private MacroElement _targetElementForMacroSelection = null;
        private bool _isAddingNewElement = false;
        private List<EplanMacroInfo> _allProjectMacros; // null = 未加载
        private MacroElement _selectedMacroElement = null;
        private ConfigurationVariable _selectedConfigVariable = null;
        private System.Windows.Threading.DispatcherTimer _toastTimer;
        private List<CheckableAddElement> _allModalAddElements = new List<CheckableAddElement>();

        private bool _isDirty { get => _viewModel.IsDirty; set => _viewModel.IsDirty = value; }
        private bool _hasInjectedData = false;
        private System.Windows.Threading.DispatcherTimer _saveDebounceTimer;
        private System.Windows.Threading.DispatcherTimer _searchDebounceTimer;
        private System.Windows.Threading.DispatcherTimer _macroSearchDebounceTimer;
        private System.Windows.Threading.DispatcherTimer _modalSearchDebounceTimer;
        private System.Windows.Threading.DispatcherTimer _builderDebounceTimer;

        public ConfiguratorWindow()
        {
            InitializeComponent();
            ModernWpf.ThemeManager.SetRequestedTheme(this, ModernWpf.ElementTheme.Dark);

            // Register Closed event for resource cleanup (timers, event handlers)
            this.Closed += ConfiguratorWindow_Closed;

            // Defer I/O-heavy init to Loaded so the window appears immediately
            this.Loaded += Window_Loaded;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_hasInjectedData)
            {
                InitializeDbPath();
                await Task.Run(() => LoadDatabase());
            }
            else
            {
                InitializeDbPath(); // Still need the path for future saves
            }
            RefreshSidebar();
            BindScriptsList();
            UpdateWorkspaceSelection();
            try
            {
                await LoadMacrosFromActiveProjectAsync(false);
            }
            catch (Exception ex)
            {
                ConfiguratorService.LogDebug($"Error loading macros on load: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the current database instance (used for data migration when reopening window).
        /// </summary>
        public ConfiguratorData Database => _db;

        /// <summary>
        /// Gets whether there are unsaved changes (used for data migration when reopening window).
        /// </summary>
        public bool HasUnsavedChanges => _isDirty;

        /// <summary>
        /// Injects previously-saved database state (used when reopening window to preserve user data).
        /// Call this before the window is shown to override file-loaded data.
        /// </summary>
        public void InjectDatabase(ConfiguratorData db, bool isDirty)
        {
            if (db != null)
            {
                _db = db;
                _isDirty = isDirty;
                _hasInjectedData = true;
            }
        }

        /// <summary>
        /// Collapse button click: close the window.
        /// The window can be reopened via the AddIn menu, with data preserved.
        /// </summary>
        private void BtnCollapse_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        /// <summary>
        /// Clean up all timers and event handlers when the window is closed.
        /// This prevents memory leaks and ensures the window can be garbage collected.
        /// </summary>
        private void ConfiguratorWindow_Closed(object sender, EventArgs e)
        {
            // Stop and null all timers to release references
            StopAndNullTimer(ref _toastTimer);
            StopAndNullTimer(ref _saveDebounceTimer);
            StopAndNullTimer(ref _searchDebounceTimer);
            StopAndNullTimer(ref _macroSearchDebounceTimer);
            StopAndNullTimer(ref _modalSearchDebounceTimer);
            StopAndNullTimer(ref _builderDebounceTimer);

            // Mark as not dirty since we're closing
            _isDirty = false;
        }

        private void StopAndNullTimer(ref System.Windows.Threading.DispatcherTimer timer)
        {
            if (timer != null)
            {
                timer.Stop();
                timer = null;
            }
        }

        private void MarkDirty()
        {
            _isDirty = true;
            if (_saveDebounceTimer == null)
            {
                _saveDebounceTimer = new System.Windows.Threading.DispatcherTimer();
                _saveDebounceTimer.Interval = TimeSpan.FromMilliseconds(500);
                _saveDebounceTimer.Tick += (_, __) =>
                {
                    _saveDebounceTimer.Stop();
                    if (_isDirty) { _isDirty = false; FlushSave(); }
                };
            }
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Start();
        }

        private void ForceFlushSave()
        {
            if (_saveDebounceTimer != null) _saveDebounceTimer.Stop();
            if (_isDirty) { _isDirty = false; FlushSave(); }
        }

        private void InitializeDbPath()
        {
            _dbFilePath = ConfiguratorService.GetDatabasePath();
            // If the resolved path is empty or points to a non-writable location, use AppData
            if (string.IsNullOrEmpty(_dbFilePath) || !IsDirectoryWritable(Path.GetDirectoryName(_dbFilePath)))
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string dataDir = Path.Combine(appData, "ASC.EplAddIn.Tool");
                if (!Directory.Exists(dataDir)) Directory.CreateDirectory(dataDir);
                string targetPath = Path.Combine(dataDir, "configurator_data.json");
                // First-run: copy from assembly dir if target doesn't exist yet
                try
                {
                    string asmDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                    if (!string.IsNullOrEmpty(asmDir))
                    {
                        string sourceFile = Path.Combine(asmDir, "configurator_data.json");
                        if (File.Exists(sourceFile) && !File.Exists(targetPath))
                        {
                            File.Copy(sourceFile, targetPath, overwrite: false);
                        }
                    }
                }
                catch { }
                _dbFilePath = targetPath;
            }
        }

        private static bool? _cachedWritable;
        private static string _checkedDir;

        private static bool IsDirectoryWritable(string dirPath)
        {
            if (_cachedWritable.HasValue && _checkedDir == dirPath)
                return _cachedWritable.Value;

            if (string.IsNullOrEmpty(dirPath) || !Directory.Exists(dirPath))
            {
                _cachedWritable = false;
                _checkedDir = dirPath;
                return false;
            }
            try
            {
                string testFile = Path.Combine(dirPath, ".write_test");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                _cachedWritable = true;
                _checkedDir = dirPath;
                return true;
            }
            catch
            {
                _cachedWritable = false;
                _checkedDir = dirPath;
                return false;
            }
        }

        // ==================== DATABASE LOAD / SAVE ====================
        private void LoadDatabase()
        {
            try
            {
                if (File.Exists(_dbFilePath))
                {
                    string json = File.ReadAllText(_dbFilePath);
                    _db = JsonConvert.DeserializeObject<ConfiguratorData>(json);
                }

                if (_db == null)
                {
                    _db = new ConfiguratorData();
                }

                // Repair null collections and duplicate structure mappings from any hand-edited JSON.
                _db.Normalize();

                // Add sample data if completely empty
                if (_db.MacroTypicals.Count == 0 && _db.TypicalGroups.Count == 0 && _db.Configurators.Count == 0)
                {
                    CreateSampleData();
                }

                InvalidateTypicalTargetsCache();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载配置失败: {ex.Message}", "配置器提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                _db = new ConfiguratorData();
                InvalidateTypicalTargetsCache();
            }
        }

        private async void FlushSave()
        {
            try
            {
                if (string.IsNullOrEmpty(_dbFilePath)) return;
                string json = JsonConvert.SerializeObject(_db, Formatting.Indented);
                await Task.Run(() => File.WriteAllText(_dbFilePath, json));
                TxtStatus.Text = $"已成功保存配置到本地！({DateTime.Now:HH:mm:ss})";
                TxtStatus.Foreground = Brushes.Green;
            }
            catch (Exception ex)
            {
                TxtStatus.Text = $"保存配置出错: {ex.Message}";
                TxtStatus.Foreground = Brushes.Red;
            }
        }

        private void CreateSampleData()
        {
            // 1. Create a MacroTypical
            var typical = new MacroTypical
            {
                Name = "Main_Distribution_Cabinet_Macro"
            };
            typical.Variables.Add(new ConfigurationVariable
            {
                Name = "MCCB_Current_Value",
                DisplayName = "设定塑壳断路器电流值",
                Type = "String",
                IsMandatory = true,
                PredefinedValues = new List<string> { "160A", "250A", "320A", "630A" }
            });
            typical.Variables.Add(new ConfigurationVariable
            {
                Name = "MCCB_Pole_Number",
                DisplayName = "设定塑壳断路器极数",
                Type = "String",
                IsMandatory = true,
                PredefinedValues = new List<string> { "3P", "4P" }
            });

            var element = new MacroElement
            {
                MacroPath = @"$(MD_MACROS)\Main_Incoming_Basic_Framework_3P.ema",
                VariantIndex = 0,
                IsActiveFormula = "=MCCB_Pole_Number=='3P'"
            };
            element.VariableMappings.Add(new MappingEntry("Connection_Cross-section_diameter", "=(MCCB_Current_Value=='320A' ? '185' : '240')"));
            typical.Elements.Add(element);

            _db.MacroTypicals.Add(typical);

            // 2. Create a TypicalGroup and nest the MacroTypical inside it
            var group = new TypicalGroup
            {
                Name = "Main Distribution Cabinet"
            };
            group.Variables.Add(new ConfigurationVariable
            {
                Name = "Function_designation",
                DisplayName = "图纸高层代号 (==)",
                Type = "String"
            });
            group.Variables.Add(new ConfigurationVariable
            {
                Name = "Use_Cabinet",
                DisplayName = "是否生成配电柜图纸",
                Type = "Boolean"
            });
            group.Variables.Add(new ConfigurationVariable
            {
                Name = "Cabinet_Current",
                DisplayName = "配电柜额定电流",
                Type = "String",
                VisibilityFormula = "=Use_Cabinet==true",
                PredefinedValues = new List<string> { "160A", "250A", "320A", "630A" }
            });

            // Add the MacroTypical instance inside the TypicalGroup
            var instance = new TypicalInstance
            {
                TargetName = "Main_Distribution_Cabinet_Macro",
                Type = "MacroTypical",
                IsActiveFormula = "=Use_Cabinet==true"
            };
            instance.VariableMappings.Add(new MappingEntry("MCCB_Current_Value", "=Cabinet_Current"));
            instance.VariableMappings.Add(new MappingEntry("MCCB_Pole_Number", "3P")); // Constant mapping
            group.Instances.Add(instance);

            _db.TypicalGroups.Add(group);

            // 3. Create a Configurator and reference the TypicalGroup
            var configurator = new Configurator
            {
                Name = "Configurator_P8",
                Description = "主配电柜原理图自动配置项目"
            };
            configurator.TypicalGroupRefs.Add("Main Distribution Cabinet");
            _db.Configurators.Add(configurator);
        }

        // ==================== HIGH-FIDELITY DRAG & DROP REORDERING LOGIC ====================
        private object _draggedItem = null;
        private ItemsControl _activeItemsControl = null;
        private int _dragStartRowIndex = -1;
        private Point _dragStartPoint;
        private double _dragStartMouseY;
        private double _rowHeight;
        private double _dragStartRowY;
        private int _currentHoverIndex;
        private double[] _rowOffsets;
        private Border _dragFloatingCard = null;
        private bool _isDraggingActive = false;
        private bool _isVoluntaryRelease = false;

        private void DragGrip_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var element = sender as DependencyObject;
            DependencyObject container = null;
            ItemsControl parentControl = null;

            while (element != null)
            {
                if (element is DataGridRow || element is ListBoxItem)
                {
                    container = element;
                }
                if (element is DataGrid || element is ListBox)
                {
                    parentControl = element as ItemsControl;
                    break;
                }
                element = VisualTreeHelper.GetParent(element);
            }

            if (container != null && parentControl != null)
            {
                var list = parentControl.ItemsSource as System.Collections.IList;
                if (list == null || list.Count <= 1) return;

                _activeItemsControl = parentControl;
                if (container is DataGridRow dgr)
                {
                    _draggedItem = dgr.Item;
                }
                else
                {
                    _draggedItem = (container as FrameworkElement).DataContext;
                }

                _dragStartRowIndex = parentControl.ItemContainerGenerator.IndexFromContainer(container);
                _dragStartPoint = e.GetPosition(null);
                _dragStartMouseY = e.GetPosition(DragCanvas).Y;
                _rowHeight = (container as FrameworkElement).ActualHeight;
                _currentHoverIndex = _dragStartRowIndex;
                _rowOffsets = new double[list.Count];
                _isDraggingActive = false;

                // Handle selection
                if (container is DataGridRow)
                {
                    ((DataGridRow)container).IsSelected = true;
                }
                else if (container is ListBoxItem)
                {
                    ((ListBoxItem)container).IsSelected = true;
                }

                e.Handled = true;
            }
        }

        private void General_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_draggedItem == null || _activeItemsControl == null) return;

            Point currentPosition = e.GetPosition(null);
            Vector diff = _dragStartPoint - currentPosition;

            var list = _activeItemsControl.ItemsSource as System.Collections.IList;
            if (list == null) return;

            if (!_isDraggingActive)
            {
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    _isDraggingActive = true;
                    _activeItemsControl.CaptureMouse();

                    var originalRow = _activeItemsControl.ItemContainerGenerator.ContainerFromIndex(_dragStartRowIndex) as FrameworkElement;
                    if (originalRow != null)
                    {
                        RenderTargetBitmap bmp = new RenderTargetBitmap((int)originalRow.ActualWidth, (int)originalRow.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                        bmp.Render(originalRow);
                        ImageBrush brush = new ImageBrush(bmp);

                        originalRow.Tag = "Placeholder";

                        _dragFloatingCard = new Border
                        {
                            Width = originalRow.ActualWidth,
                            Height = originalRow.ActualHeight,
                            Background = brush,
                            BorderBrush = new SolidColorBrush(Color.FromRgb(0, 132, 255)),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(4),
                            RenderTransform = new TranslateTransform(),
                            Effect = new System.Windows.Media.Effects.DropShadowEffect
                            {
                                Color = Colors.Black,
                                Direction = 315,
                                ShadowDepth = 8,
                                BlurRadius = 15,
                                Opacity = 0.6
                            }
                        };

                        DragCanvas.Children.Clear();
                        DragCanvas.Children.Add(_dragFloatingCard);
                        Canvas.SetLeft(_dragFloatingCard, 0);

                        _dragStartRowY = originalRow.TranslatePoint(new Point(0, 0), DragCanvas).Y;
                        Canvas.SetTop(_dragFloatingCard, _dragStartRowY);
                    }
                }
            }

            if (_isDraggingActive && _dragFloatingCard != null)
            {
                double currentMouseY = e.GetPosition(DragCanvas).Y;
                double deltaY = currentMouseY - _dragStartMouseY;

                double top = _dragStartRowY + deltaY;
                double minTop = _activeItemsControl.TranslatePoint(new Point(0, 0), DragCanvas).Y;
                double maxTop = minTop + _activeItemsControl.ActualHeight - _rowHeight;
                top = Math.Max(minTop, Math.Min(maxTop, top));
                Canvas.SetTop(_dragFloatingCard, top);

                int hoverIndex = _dragStartRowIndex + (int)Math.Round((top - _dragStartRowY) / _rowHeight);
                hoverIndex = Math.Max(0, Math.Min(list.Count - 1, hoverIndex));

                _currentHoverIndex = hoverIndex;

                // Animate placeholder row to target offset
                double placeholderOffset = (hoverIndex - _dragStartRowIndex) * _rowHeight;
                var startRow = _activeItemsControl.ItemContainerGenerator.ContainerFromIndex(_dragStartRowIndex) as FrameworkElement;
                if (startRow != null)
                {
                    if (Math.Abs(_rowOffsets[_dragStartRowIndex] - placeholderOffset) > 0.01)
                    {
                        _rowOffsets[_dragStartRowIndex] = placeholderOffset;
                        AnimateRowToOffset(startRow, placeholderOffset);
                    }
                }

                for (int i = 0; i < list.Count; i++)
                {
                    if (i == _dragStartRowIndex) continue;

                    double targetOffset = 0;
                    if (_dragStartRowIndex < hoverIndex)
                    {
                        if (i > _dragStartRowIndex && i <= hoverIndex)
                        {
                            targetOffset = -_rowHeight;
                        }
                    }
                    else if (_dragStartRowIndex > hoverIndex)
                    {
                        if (i >= hoverIndex && i < _dragStartRowIndex)
                        {
                            targetOffset = _rowHeight;
                        }
                    }

                    if (Math.Abs(_rowOffsets[i] - targetOffset) > 0.01)
                    {
                        _rowOffsets[i] = targetOffset;
                        var row = _activeItemsControl.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                        if (row != null)
                        {
                            AnimateRowToOffset(row, targetOffset);
                        }
                    }
                }
            }
        }

        private void General_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_draggedItem == null || _activeItemsControl == null) return;

            _isVoluntaryRelease = true;
            _activeItemsControl.ReleaseMouseCapture();
            _isVoluntaryRelease = false;

            if (!_isDraggingActive)
            {
                CleanupDragDropState();
                return;
            }

            if (_dragFloatingCard == null)
            {
                CleanupDragDropState();
                return;
            }

            var list = _activeItemsControl.ItemsSource as System.Collections.IList;
            if (list == null)
            {
                CleanupDragDropState();
                return;
            }

            double targetSnapY = _dragStartRowY + (_currentHoverIndex - _dragStartRowIndex) * _rowHeight;

            var snapAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = Canvas.GetTop(_dragFloatingCard),
                To = targetSnapY,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };

            snapAnimation.Completed += (s, ev) =>
            {
                DragCanvas.Children.Clear();

                if (_currentHoverIndex != _dragStartRowIndex && _currentHoverIndex >= 0 && _currentHoverIndex < list.Count)
                {
                    var dragged = list[_dragStartRowIndex];
                    list.RemoveAt(_dragStartRowIndex);
                    list.Insert(_currentHoverIndex, dragged);

                    // Refresh UI binding and keep selection
                    if (_activeItemsControl == ItrElements)
                    {
                        BindElementsList(list as List<MacroElement>);
                    }
                    else if (_activeItemsControl == ItrVariables)
                    {
                        BindVariablesList(list as List<ConfigurationVariable>);
                    }
                    else if (_activeItemsControl == ItrInstances)
                    {
                        BindInstancesList(list as List<TypicalInstance>);
                    }
                    else if (_activeItemsControl == ItrConfigGroupsList)
                    {
                        BindConfigGroupsList(_selectedItem as Configurator);
                    }
                    MarkDirty();
                }
                else
                {
                    var startRow = _activeItemsControl.ItemContainerGenerator.ContainerFromIndex(_dragStartRowIndex) as FrameworkElement;
                    if (startRow != null)
                    {
                        startRow.Tag = null;
                        startRow.Opacity = 1.0;
                    }
                }

                for (int i = 0; i < list.Count; i++)
                {
                    var row = _activeItemsControl.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                    if (row != null)
                    {
                        row.Tag = null;
                        row.Opacity = 1.0;
                        if (row.RenderTransform is TranslateTransform transform)
                        {
                            transform.BeginAnimation(TranslateTransform.YProperty, null);
                            transform.Y = 0;
                        }
                    }
                }

                _draggedItem = null;
                _activeItemsControl = null;
                _dragFloatingCard = null;
                _isDraggingActive = false;
            };

            _dragFloatingCard.BeginAnimation(Canvas.TopProperty, snapAnimation);
        }

        private void General_LostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDraggingActive && !_isVoluntaryRelease)
            {
                CleanupDragDropState();
            }
        }

        private void CleanupDragDropState()
        {
            DragCanvas.Children.Clear();

            if (_activeItemsControl != null)
            {
                var list = _activeItemsControl.ItemsSource as System.Collections.IList;
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var row = _activeItemsControl.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
                        if (row != null)
                        {
                            row.Tag = null;
                            row.Opacity = 1.0;
                            if (row.RenderTransform is TranslateTransform transform)
                            {
                                transform.BeginAnimation(TranslateTransform.YProperty, null);
                                transform.Y = 0;
                            }
                        }
                    }
                }
            }

            _draggedItem = null;
            _activeItemsControl = null;
            _dragFloatingCard = null;
            _isDraggingActive = false;
        }

        private void AnimateRowToOffset(FrameworkElement row, double targetY)
        {
            if (!(row.RenderTransform is TranslateTransform))
            {
                row.RenderTransform = new TranslateTransform();
            }
            var transform = (TranslateTransform)row.RenderTransform;

            var animation = new System.Windows.Media.Animation.DoubleAnimation
            {
                To = targetY,
                Duration = TimeSpan.FromMilliseconds(250),
                EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };

            transform.BeginAnimation(TranslateTransform.YProperty, animation);
        }

        private void ItrElements_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void ItrElements_Drop(object sender, DragEventArgs e)
        {
            e.Handled = true;
        }

    }
}
