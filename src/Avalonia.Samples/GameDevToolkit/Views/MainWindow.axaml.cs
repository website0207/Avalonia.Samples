using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using GameDevToolkit.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace GameDevToolkit.Views;

public partial class MainWindow : Window
{
    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    public MainWindow()
    {
        InitializeComponent();
    }

    private async void SelectFileButtonClick(object sender, RoutedEventArgs e)
    {
        try
        {
            // 获取TopLevel引用来访问StorageProvider
            var topLevel = TopLevel.GetTopLevel(this);

            if (topLevel?.StorageProvider == null)
            {
                if (ViewModel != null)
                {
                    ViewModel.ChangelistResult = "无法访问文件系统";
                }
                return;
            }

            // 创建文件选择选项
            var filePickerOpenOptions = new FilePickerOpenOptions
            {
                Title = "选择TypeScript文件",
                AllowMultiple = false,
                FileTypeFilter = new FilePickerFileType[]
                {
                    new("TypeScript文件")
                    {
                        Patterns = new[] { "*.ts", "*.tsx" },
                        MimeTypes = new[] { "text/typescript" }
                    },
                    new("所有文件")
                    {
                        Patterns = new[] { "*.*" },
                        MimeTypes = new[] { "application/octet-stream" }
                    }
                }
            };

            // 显示文件选择对话框
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(filePickerOpenOptions);

            if (files.Any())
            {
                var selectedFile = files.First();
                var filePath = selectedFile.Path.LocalPath;

                // 设置选择的文件路径到ViewModel
                if (ViewModel != null)
                {
                    ViewModel.FilePath = filePath;
                    ViewModel.Greeting = "已选择文件: " + System.IO.Path.GetFileName(filePath);
                }
            }
            else
            {
                if (ViewModel != null)
                {
                    ViewModel.Greeting = "未选择文件";
                }
            }
        }
        catch (Exception ex)
        {
            if (ViewModel != null)
            {
                ViewModel.Greeting = "选择文件时出错";
                ViewModel.ChangelistResult = $"文件选择错误: {ex.Message}";
            }
        }
    }
}