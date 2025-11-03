using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using GameDevToolkit.Services;
using GameDevToolkit.Models;

namespace GameDevToolkit.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    private readonly PerforceService _perforceService;
    private string _greeting = "Welcome to Avalonia!";
    private string _filePath = "";
    private string _changelistResult = "";
    private bool _isProcessing = false;
    private PerforceClient? _selectedClient;
    private bool _isLoadingClients = false;
    
    public string Greeting
    {
        get => _greeting;
        set => this.RaiseAndSetIfChanged(ref _greeting, value);
    }

    public string FilePath
    {
        get => _filePath;
        set => this.RaiseAndSetIfChanged(ref _filePath, value);
    }

    public string ChangelistResult
    {
        get => _changelistResult;
        set => this.RaiseAndSetIfChanged(ref _changelistResult, value);
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        private set => this.RaiseAndSetIfChanged(ref _isProcessing, value);
    }

    public bool IsLoadingClients
    {
        get => _isLoadingClients;
        private set => this.RaiseAndSetIfChanged(ref _isLoadingClients, value);
    }

    public PerforceClient? SelectedClient
    {
        get => _selectedClient;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedClient, value);
            if (value != null)
            {
                Greeting = $"已选择客户端: {value.Name} (根目录: {value.Root})";
            }

        }
    }

    public ObservableCollection<PerforceClient> PerforceClients { get; } = new();

    // ========== 原有功能的命令 ==========
    public ICommand GetChangelistCommand { get; }
    public ICommand SelectFileCommand { get; }

    // ========== 新功能的属性和命令 ==========
    private string _changeNumber = "";
    private bool _isProcessingShelved = false;

    public string ChangeNumber
    {
        get => _changeNumber;
        set => this.RaiseAndSetIfChanged(ref _changeNumber, value);
    }

    public bool IsProcessingShelved
    {
        get => _isProcessingShelved;
        private set => this.RaiseAndSetIfChanged(ref _isProcessingShelved, value);
    }

    public ICommand GetShelvedFilesCommand { get; }

    public MainWindowViewModel()
    {
        _perforceService = new PerforceService();

        // ========== 原有功能命令初始化 ==========
        GetChangelistCommand = ReactiveCommand.CreateFromTask(GetChangelistAsync);
        SelectFileCommand = ReactiveCommand.Create(SelectFile);

        // ========== 新功能命令初始化 ==========
        GetShelvedFilesCommand = ReactiveCommand.CreateFromTask(GetShelvedFilesAsync);

        // 初始化时加载客户端列表
        _ = LoadClientsAsync();
    }

    private void SelectFile()
    {
        Greeting = "请选择TypeScript文件...";
    }

    private async Task LoadClientsAsync()
    {
        try
        {
            IsLoadingClients = true;
            Greeting = "正在加载Perforce客户端...";

            var clients = await _perforceService.GetClientsAsync();
            var currentClientName = await _perforceService.GetCurrentClientAsync();

            // 清空并重新添加客户端
            PerforceClients.Clear();
            foreach (var client in clients.OrderBy(c => c.Name))
            {
                PerforceClients.Add(client);
            }

            // 设置当前选中的客户端
            if (!string.IsNullOrEmpty(currentClientName))
            {
                var currentClient = PerforceClients.FirstOrDefault(c => c.Name == currentClientName);
                if (currentClient != null)
                {
                    SelectedClient = currentClient;
                    Greeting = $"已连接到客户端: {currentClient.Name} (根目录: {currentClient.Root})";
                }
                else
                {
                    Greeting = $"当前客户端: {currentClientName} (不在可用列表中)";
                }
            }
            else if (PerforceClients.Any())
            {
                SelectedClient = PerforceClients.First();
                Greeting = $"已选择客户端: {SelectedClient.Name}";
            }
            else
            {
                Greeting = "未找到可用的Perforce客户端";
            }
        }
        catch (Exception ex)
        {
            Greeting = $"加载客户端失败: {ex.Message}";
        }
        finally
        {
            IsLoadingClients = false;
        }
    }

    private async Task GetChangelistAsync()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            ChangelistResult = "请输入或选择TypeScript文件的路径";
            return;
        }

        try
        {
            IsProcessing = true;
            ChangelistResult = "正在查询Perforce...";

            if (SelectedClient == null)
            {
                ChangelistResult = "请先选择一个Perforce客户端";
                return;
            }
            var (isSuccess, changelist) = await _perforceService.GetLatestChangelistAsync(SelectedClient.Name, FilePath);


            if (isSuccess)
            {
                ChangelistResult = $"Changelist ID: {changelist}\n客户端: {SelectedClient?.Name ?? "未知"}\n根目录: {SelectedClient?.Root ?? "未知"}\n";
                Greeting = "查询成功！";
            }
            else
            {
                ChangelistResult = changelist; // 显示错误信息
                Greeting = "查询失败";
            }
        }
        catch (Exception ex)
        {
            ChangelistResult = $"异常: {ex.Message}";
            Greeting = "查询出错";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    // ========== 新功能方法：根据Change号查询Shelved Files ==========
    private async Task GetShelvedFilesAsync()
    {
        if (string.IsNullOrWhiteSpace(ChangeNumber))
        {
            Greeting = "请输入Change号";
            return;
        }

        if (!int.TryParse(ChangeNumber, out int changeNum))
        {
            Greeting = "Change号必须是数字";
            return;
        }

        try
        {
            IsProcessingShelved = true;
            Greeting = $"正在查询Change {ChangeNumber}的Shelved Files...";

            if (SelectedClient == null)
            {
                Greeting = "请先选择一个Perforce客户端";
                return;
            }

            // ========== 真实实现：调用Perforce服务查询Shelved Files，实时输出结果 ==========
            ChangelistResult = ""; // 清空之前的结果

            // 定义实时输出回调函数
            void OnResult(string resultText)
            {
                ChangelistResult += resultText + "\n";
            }

            var (isSuccess, shelvedFiles, errorMessage) = await _perforceService.GetShelvedFilesAsync(SelectedClient!.Name, changeNum, OnResult);

            if (isSuccess)
            {
                // 最终结果已经在实时输出中显示了
                Greeting = $"Change {ChangeNumber}的Shelved Files查询完成！处理了{shelvedFiles?.Count ?? 0}个文件";
            }
            else
            {
                Greeting = $"Change {ChangeNumber}查询失败：{errorMessage}";
            }
        }
        catch (Exception ex)
        {
            ChangelistResult = $"查询Shelved Files异常: {ex.Message}";
            Greeting = $"查询Shelved Files失败: {ex.Message}";
        }
        finally
        {
            IsProcessingShelved = false;
        }
    }
}