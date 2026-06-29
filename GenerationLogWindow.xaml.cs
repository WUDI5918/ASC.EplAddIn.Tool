using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    public partial class GenerationLogWindow : Window
    {
        private ObservableCollection<LogEntry> _allLogs = new ObservableCollection<LogEntry>();
        private Action _generationAction;
        private bool _isCompleted = false;

        public GenerationLogWindow(Window owner, Action generationAction)
        {
            InitializeComponent();
            this.Owner = owner;
            _generationAction = generationAction;

            LstLogs.ItemsSource = _allLogs;

            // Subscribe to static service log events
            ConfiguratorService.OnLogAdded += ConfiguratorService_OnLogAdded;

            this.Loaded += GenerationLogWindow_Loaded;
            this.Closed += GenerationLogWindow_Closed;
        }

        private void GenerationLogWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Execute generation on the UI thread, pumping messages using DoEvents
            ExecuteGeneration();
        }

        private void GenerationLogWindow_Closed(object sender, EventArgs e)
        {
            // Unsubscribe to avoid memory leaks
            ConfiguratorService.OnLogAdded -= ConfiguratorService_OnLogAdded;
        }

        private void ConfiguratorService_OnLogAdded(LogEntry entry)
        {
            // Add to UI collection (Dispatcher-safe)
            Dispatcher.Invoke(() =>
            {
                _allLogs.Add(entry);
                ApplyFilter();
                
                // Auto scroll to the end
                if (LstLogs.Items.Count > 0)
                {
                    LstLogs.ScrollIntoView(LstLogs.Items[LstLogs.Items.Count - 1]);
                }

                // Force WPF layout and rendering update
                DoEvents();
            });
        }

        private void ExecuteGeneration()
        {
            _allLogs.Clear();
            ConfiguratorService.GenerationLogs.Clear();
            GenProgressBar.IsIndeterminate = true;
            TxtSummaryStatus.Text = "正在生成原理图，请稍候...";
            
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                ConfiguratorService.Log("Info", "===== 开始原理图生成流程 =====");
                DoEvents();

                // Run generation action (typically blocks, but DoEvents will pump messages)
                _generationAction?.Invoke();

                stopwatch.Stop();
                _isCompleted = true;

                ConfiguratorService.Log("Success", $"===== 原理图生成流程成功结束 (耗时: {stopwatch.Elapsed.TotalSeconds:F2}秒) =====");
                TxtSummaryStatus.Text = $"生成完成！耗时 {stopwatch.Elapsed.TotalSeconds:F2} 秒。";
                GenProgressBar.IsIndeterminate = false;
                GenProgressBar.Value = 100;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _isCompleted = true;
                ConfiguratorService.Log("Error", $"生成中断！未捕获异常: {ex.Message}\r\n{ex.StackTrace}");
                TxtSummaryStatus.Text = "生成失败，详情请查看错误日志！";
                GenProgressBar.IsIndeterminate = false;
                GenProgressBar.Value = 0;
            }
            finally
            {
                BtnClose.IsEnabled = true;
            }
        }

        private void ApplyFilter()
        {
            if (LstLogs == null || TxtSearchLog == null || CboLogLevelFilter == null) return;

            string search = TxtSearchLog.Text?.Trim();
            string selectedLevel = (CboLogLevelFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();

            // Setup visibility mapping
            foreach (var item in LstLogs.Items)
            {
                var entry = item as LogEntry;
                if (entry == null) continue;

                bool matchesSearch = string.IsNullOrEmpty(search) || 
                                     entry.Message.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
                
                bool matchesLevel = true;
                if (selectedLevel != null && selectedLevel != "全部日志级别")
                {
                    if (selectedLevel.Contains("信息") && entry.Level != "Info") matchesLevel = false;
                    else if (selectedLevel.Contains("成功") && entry.Level != "Success") matchesLevel = false;
                    else if (selectedLevel.Contains("警告") && entry.Level != "Warning") matchesLevel = false;
                    else if (selectedLevel.Contains("错误") && entry.Level != "Error") matchesLevel = false;
                }

                ListBoxItem container = LstLogs.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;
                if (container != null)
                {
                    container.Visibility = (matchesSearch && matchesLevel) ? Visibility.Visible : Visibility.Collapsed;
                }
            }
        }

        private void TxtSearchLog_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void CboLogLevelFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void BtnCopyLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var log in ConfiguratorService.GenerationLogs)
                {
                    sb.AppendLine($"[{log.Timestamp:yyyy-MM-dd HH:mm:ss}] [{log.Level}] {log.Message}");
                }
                Clipboard.SetText(sb.ToString());
                MessageBox.Show("日志已成功复制到剪贴板！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"复制日志失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = _isCompleted;
            this.Close();
        }

        /// <summary>
        /// Force the WPF UI thread to process all pending paint and layout events.
        /// </summary>
        public static void DoEvents()
        {
            Application.Current.Dispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
        }
    }
}
