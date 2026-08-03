using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;

namespace AutoSchematicConfigurator.EplAddIn.Tool
{
    public partial class VersionHistoryDialog : Window
    {
        /// <summary>
        /// 当用户点击"还原"时，返回还原后的 ConfiguratorData
        /// </summary>
        public ConfiguratorData RestoredData { get; private set; }

        /// <summary>
        /// 是否执行了还原操作
        /// </summary>
        public bool WasRestored { get; private set; }

        private readonly ConfiguratorData _currentDb;
        private readonly Action<ConfiguratorData> _onRestored;

        // 用于 DataGrid 显示的包装类
        private class VersionDisplayItem
        {
            public string VersionId { get; set; }
            public int VersionNumber { get; set; }
            public DateTime Timestamp { get; set; }
            public string Description { get; set; }
            public bool IsPublished { get; set; }
            public string StatusText => IsPublished ? "已发布" : "草稿";
        }

        public VersionHistoryDialog(ConfiguratorData currentDb = null, Action<ConfiguratorData> onRestored = null)
        {
            InitializeComponent();
            _currentDb = currentDb;
            _onRestored = onRestored;
            LoadVersions();
        }

        private void LoadVersions()
        {
            var versions = LibraryVersionService.ListVersions();
            var items = versions.Select(v => new VersionDisplayItem
            {
                VersionId = v.VersionId,
                VersionNumber = v.VersionNumber,
                Timestamp = v.Timestamp,
                Description = v.Description,
                IsPublished = v.IsPublished
            }).ToList();
            DgVersions.ItemsSource = items;
        }

        private VersionDisplayItem GetSelectedItem()
        {
            return DgVersions.SelectedItem as VersionDisplayItem;
        }

        private void BtnCreateVersion_Click(object sender, RoutedEventArgs e)
        {
            // 弹出输入描述对话框
            var description = PromptDescription();
            if (description == null) return;

            try
            {
                // Use the in-memory database when provided, so unsaved edits are snapshotted.
                ConfiguratorData db = _currentDb;
                if (db == null)
                {
                    string dbPath = ConfiguratorService.GetDatabasePath();
                    if (!File.Exists(dbPath))
                    {
                        MessageBox.Show("当前库文件不存在，无法创建版本。", "错误", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    db = JsonConvert.DeserializeObject<ConfiguratorData>(File.ReadAllText(dbPath)) ?? new ConfiguratorData();
                }

                var version = LibraryVersionService.CreateVersion(db, description);
                MessageBox.Show($"版本 v{version.VersionNumber} 已创建。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                LoadVersions();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"创建版本失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            var item = GetSelectedItem();
            if (item == null)
            {
                MessageBox.Show("请先选择一个版本。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"确定要还原到版本 v{item.VersionNumber} 吗？\n当前数据将被覆盖。",
                "确认还原",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                var data = LibraryVersionService.RestoreVersion(item.VersionId);
                if (data == null)
                {
                    MessageBox.Show("还原失败：版本数据无效。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                RestoredData = data;
                WasRestored = true;
                _onRestored?.Invoke(data);
                MessageBox.Show($"已还原到版本 v{item.VersionNumber}。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"还原失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            var item = GetSelectedItem();
            if (item == null)
            {
                MessageBox.Show("请先选择一个版本。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"确定要删除版本 v{item.VersionNumber} 吗？此操作不可撤销。",
                "确认删除",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                LibraryVersionService.DeleteVersion(item.VersionId);
                LoadVersions();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnPublish_Click(object sender, RoutedEventArgs e)
        {
            var item = GetSelectedItem();
            if (item == null)
            {
                MessageBox.Show("请先选择一个版本。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                if (item.IsPublished)
                {
                    LibraryVersionService.RevokeVersion(item.VersionId);
                }
                else
                {
                    LibraryVersionService.PublishVersion(item.VersionId);
                }
                LoadVersions();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"操作失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// 弹出简单的输入框让用户填写版本描述
        /// </summary>
        private string PromptDescription()
        {
            var dialog = new Window
            {
                Title = "版本描述",
                Width = 400,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = System.Windows.Media.Brushes.Black,
                Owner = this
            };

            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new System.Windows.Controls.TextBlock
            {
                Text = "请输入版本描述 (Description):",
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            System.Windows.Controls.Grid.SetRow(label, 0);
            grid.Children.Add(label);

            var textBox = new System.Windows.Controls.TextBox
            {
                Height = 50,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                Background = System.Windows.Media.Brushes.Black,
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = System.Windows.Media.Brushes.Gray,
                CaretBrush = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 12)
            };
            System.Windows.Controls.Grid.SetRow(textBox, 1);
            grid.Children.Add(textBox);

            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnCancel = new System.Windows.Controls.Button
            {
                Content = "取消",
                Width = 70,
                Height = 28,
                IsCancel = true,
                Background = System.Windows.Media.Brushes.Black,
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = System.Windows.Media.Brushes.Gray,
                Margin = new Thickness(0, 0, 8, 0)
            };
            btnPanel.Children.Add(btnCancel);

            var btnOk = new System.Windows.Controls.Button
            {
                Content = "确定",
                Width = 70,
                Height = 28,
                IsDefault = true,
                Background = System.Windows.Media.Brushes.Black,
                Foreground = System.Windows.Media.Brushes.White,
                BorderBrush = System.Windows.Media.Brushes.Gray
            };
            btnPanel.Children.Add(btnOk);

            System.Windows.Controls.Grid.SetRow(btnPanel, 2);
            grid.Children.Add(btnPanel);

            dialog.Content = grid;

            string result = null;
            btnOk.Click += (s, ev) =>
            {
                result = textBox.Text?.Trim() ?? "";
                dialog.DialogResult = true;
                dialog.Close();
            };

            if (dialog.ShowDialog() == true)
            {
                return result;
            }
            return null;
        }
    }
}
