using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using GameDevToolkit.Services;
using GameDevToolkit.Models;

namespace GameDevToolkit.ViewModels;

public class PerforceToolsViewModel : ViewModelBase
{
    private readonly PerforceService _perforceService;
    private string _filePath = "";
    private string _changelistResult = "";
    private string _shelvedFilesResult = "";
    private string _changeNumber = "";
    private string _changelistInput = "";
    private string _summaryResult = "";
    private bool _isProcessing = false;
    private bool _isProcessingShelved = false;
    private bool _isProcessingSummary = false;
    private bool _isLoadingClients = false;
    private string _connectionStatus = "检查连接中...";
    private string _processingStatus = "";
    private string _summaryStatus = "";
    private PerforceClient? _selectedClient;

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

    public string ShelvedFilesResult
    {
        get => _shelvedFilesResult;
        set => this.RaiseAndSetIfChanged(ref _shelvedFilesResult, value);
    }

    public string ChangeNumber
    {
        get => _changeNumber;
        set => this.RaiseAndSetIfChanged(ref _changeNumber, value);
    }

    public string ChangelistInput
    {
        get => _changelistInput;
        set => this.RaiseAndSetIfChanged(ref _changelistInput, value);
    }

    public string SummaryResult
    {
        get => _summaryResult;
        set => this.RaiseAndSetIfChanged(ref _summaryResult, value);
    }

    public bool IsProcessingSummary
    {
        get => _isProcessingSummary;
        private set => this.RaiseAndSetIfChanged(ref _isProcessingSummary, value);
    }

    public string SummaryStatus
    {
        get => _summaryStatus;
        set => this.RaiseAndSetIfChanged(ref _summaryStatus, value);
    }

    public bool IsProcessing
    {
        get => _isProcessing;
        private set => this.RaiseAndSetIfChanged(ref _isProcessing, value);
    }

    public bool IsProcessingShelved
    {
        get => _isProcessingShelved;
        private set => this.RaiseAndSetIfChanged(ref _isProcessingShelved, value);
    }

    public bool IsLoadingClients
    {
        get => _isLoadingClients;
        private set => this.RaiseAndSetIfChanged(ref _isLoadingClients, value);
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        set => this.RaiseAndSetIfChanged(ref _connectionStatus, value);
    }

    public string ProcessingStatus
    {
        get => _processingStatus;
        set => this.RaiseAndSetIfChanged(ref _processingStatus, value);
    }

    public PerforceClient? SelectedClient
    {
        get => _selectedClient;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedClient, value);
            if (value != null)
            {
                ConnectionStatus = $"已选择客户端: {value.Name} (根目录: {value.Root})";
            }
        }
    }

    public ObservableCollection<PerforceClient> PerforceClients { get; } = new();

    // 命令
    public ICommand GetChangelistCommand { get; }
    public ICommand GetShelvedFilesCommand { get; }
    public ICommand GenerateSummaryCommand { get; }
    public ICommand TestConnectionCommand { get; }

    public PerforceToolsViewModel()
    {
        _perforceService = new PerforceService();

        // 初始化命令
        GetChangelistCommand = ReactiveCommand.CreateFromTask(GetChangelistAsync);
        GetShelvedFilesCommand = ReactiveCommand.CreateFromTask(GetShelvedFilesAsync);
        GenerateSummaryCommand = ReactiveCommand.CreateFromTask(GenerateSummaryAsync);
        TestConnectionCommand = ReactiveCommand.CreateFromTask(TestConnectionAsync);

        // 初始化时加载客户端列表
        _ = LoadClientsAsync();
        _ = CheckInitialConnectionAsync();
    }

  
    private async Task LoadClientsAsync()
    {
        try
        {
            IsLoadingClients = true;
            ConnectionStatus = "正在加载Perforce客户端...";

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
                    ConnectionStatus = $"已连接到客户端: {currentClient.Name} (根目录: {currentClient.Root})";
                }
                else
                {
                    ConnectionStatus = $"当前客户端: {currentClientName} (不在可用列表中)";
                }
            }
            else if (PerforceClients.Any())
            {
                SelectedClient = PerforceClients.First();
                ConnectionStatus = $"已选择客户端: {SelectedClient.Name}";
            }
            else
            {
                ConnectionStatus = "未找到可用的Perforce客户端";
            }
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"加载客户端失败: {ex.Message}";
        }
        finally
        {
            IsLoadingClients = false;
        }
    }

    private async Task CheckInitialConnectionAsync()
    {
        try
        {
            await Task.Delay(1000); // 等待客户端加载
            await TestConnectionAsync();
        }
        catch
        {
            ConnectionStatus = "连接检查失败";
        }
    }

    private async Task TestConnectionAsync()
    {
        try
        {
            ConnectionStatus = "正在检查连接...";
            var isConnected = await _perforceService.IsConnectedAsync();

            if (isConnected)
            {
                ConnectionStatus = "✅ Perforce连接正常";
            }
            else
            {
                ConnectionStatus = "❌ Perforce连接失败，请检查配置";
            }
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"❌ 连接检查出错: {ex.Message}";
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
            }
            else
            {
                ChangelistResult = changelist; // 显示错误信息
            }
        }
        catch (Exception ex)
        {
            ChangelistResult = $"异常: {ex.Message}";
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private async Task GetShelvedFilesAsync()
    {
        if (string.IsNullOrWhiteSpace(ChangeNumber))
        {
            ShelvedFilesResult = "请输入Change号";
            return;
        }

        if (!int.TryParse(ChangeNumber, out int changeNum))
        {
            ShelvedFilesResult = "Change号必须是数字";
            return;
        }

        try
        {
            IsProcessingShelved = true;
            ProcessingStatus = $"正在查询Change {ChangeNumber}的Shelved Files...";

            if (SelectedClient == null)
            {
                ShelvedFilesResult = "请先选择一个Perforce客户端";
                return;
            }

            // 清空之前的结果
            ShelvedFilesResult = "";

            // 定义实时输出回调函数
            void OnResult(string resultText)
            {
                ShelvedFilesResult += resultText + "\n";
                ProcessingStatus = resultText.Trim();
            }

            var (isSuccess, shelvedFiles, errorMessage) = await _perforceService.GetShelvedFilesAsync(SelectedClient!.Name, changeNum, OnResult);

            if (isSuccess)
            {
                ProcessingStatus = $"Change {ChangeNumber}的Shelved Files查询完成！处理了{shelvedFiles?.Count ?? 0}个文件";
            }
            else
            {
                ProcessingStatus = $"Change {ChangeNumber}查询失败：{errorMessage}";
            }
        }
        catch (Exception ex)
        {
            ShelvedFilesResult = $"查询Shelved Files异常: {ex.Message}";
            ProcessingStatus = $"查询Shelved Files失败: {ex.Message}";
        }
        finally
        {
            IsProcessingShelved = false;
        }
    }

    private async Task GenerateSummaryAsync()
    {
        if (string.IsNullOrWhiteSpace(ChangelistInput))
        {
            SummaryResult = "请输入changelist号或changelist列表";
            return;
        }

        try
        {
            IsProcessingSummary = true;
            SummaryStatus = "正在解析changelist输入...";

            if (SelectedClient == null)
            {
                SummaryResult = "请先选择一个Perforce客户端";
                return;
            }

            // 清空之前的结果
            SummaryResult = "";

            // 解析changelist输入（支持单个或多个）
            var changelistNumbers = ParseChangelistInput(ChangelistInput);
            if (changelistNumbers.Count == 0)
            {
                SummaryResult = "未能解析出有效的changelist号";
                return;
            }

            SummaryStatus = $"正在查询 {changelistNumbers.Count} 个changelist的详细信息...";

            // 查询每个changelist的详细信息
            var changelistDetails = new List<ChangelistInfo>();
            foreach (var changelistNum in changelistNumbers)
            {
                SummaryStatus = $"正在查询 changelist {changelistNum}...";
                var (success, details, error) = await _perforceService.GetChangelistDetailsAsync(SelectedClient.Name, changelistNum);

                if (success && details != null)
                {
                    changelistDetails.Add(details);
                }
                else
                {
                    SummaryResult += $"⚠️ Changelist {changelistNum} 查询失败: {error}\n";
                }
            }

            if (changelistDetails.Count == 0)
            {
                SummaryResult = "所有changelist查询都失败了";
                return;
            }

            SummaryStatus = "正在生成总结...";

            // 生成总结
            var summary = GenerateChangelistSummary(changelistDetails);
            SummaryResult = summary;

            SummaryStatus = $"总结生成完成！处理了 {changelistDetails.Count} 个changelist";
        }
        catch (Exception ex)
        {
            SummaryResult = $"生成总结时出现异常: {ex.Message}";
            SummaryStatus = "生成总结失败";
        }
        finally
        {
            IsProcessingSummary = false;
        }
    }

    private List<int> ParseChangelistInput(string input)
    {
        var changelists = new List<int>();

        // 支持多种格式：
        // - 单个数字: 12345
        // - 逗号分隔: 12345,12346,12347
        // - 空格分隔: 12345 12346 12347
        // - 范围: 12345-12347
        // - 混合: 12345,12347-12349,12350

        var parts = input.Split(new[] { ',', ';', ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var part in parts)
        {
            if (part.Contains('-'))
            {
                // 处理范围
                var rangeParts = part.Split('-');
                if (rangeParts.Length == 2 &&
                    int.TryParse(rangeParts[0].Trim(), out int start) &&
                    int.TryParse(rangeParts[1].Trim(), out int end))
                {
                    for (int i = start; i <= end; i++)
                    {
                        if (!changelists.Contains(i))
                            changelists.Add(i);
                    }
                }
            }
            else if (int.TryParse(part.Trim(), out int single))
            {
                if (!changelists.Contains(single))
                    changelists.Add(single);
            }
        }

        return changelists.OrderBy(x => x).ToList();
    }

    private string GenerateChangelistSummary(List<ChangelistInfo> changelistDetails)
    {
        if (changelistDetails.Count == 0)
            return "没有可用的changelist信息";

        var summary = new StringBuilder();

        // 标题
        summary.AppendLine("📋 Changelist 总结报告");
        summary.AppendLine("=" + new string('=', 50));
        summary.AppendLine();

        // 概览信息
        summary.AppendLine("📊 概览信息");
        summary.AppendLine("-" + new string('-', 30));
        summary.AppendLine($"处理的changelist数量: {changelistDetails.Count}");
        summary.AppendLine($"时间范围: {changelistDetails.Min(c => c.Date)} 至 {changelistDetails.Max(c => c.Date)}");

        var authors = changelistDetails.Select(c => c.Author).Distinct().ToList();
        summary.AppendLine($"涉及作者: {string.Join(", ", authors)}");

        var totalFiles = changelistDetails.Sum(c => c.Files?.Count ?? 0);
        summary.AppendLine($"涉及文件总数: {totalFiles}");
        summary.AppendLine();

        // 按作者分组
        summary.AppendLine("👥 作者贡献");
        summary.AppendLine("-" + new string('-', 30));
        var authorGroups = changelistDetails.GroupBy(c => c.Author);
        foreach (var group in authorGroups)
        {
            summary.AppendLine($"• {group.Key}: {group.Count()} 个changelist, {group.Sum(c => c.Files?.Count ?? 0)} 个文件");
        }
        summary.AppendLine();

        // 文件类型统计
        summary.AppendLine("📁 文件类型统计");
        summary.AppendLine("-" + new string('-', 30));
        var fileTypes = new Dictionary<string, int>();
        foreach (var changelist in changelistDetails)
        {
            if (changelist.Files != null)
            {
                foreach (var file in changelist.Files)
                {
                    var extension = System.IO.Path.GetExtension(file.DepotPath)?.ToLower() ?? "无扩展名";
                    fileTypes[extension] = fileTypes.GetValueOrDefault(extension) + 1;
                }
            }
        }

        foreach (var kvp in fileTypes.OrderByDescending(x => x.Value))
        {
            summary.AppendLine($"• {kvp.Key}: {kvp.Value} 个文件");
        }
        summary.AppendLine();

        // 详细信息
        summary.AppendLine("📝 详细信息");
        summary.AppendLine("=" + new string('=', 50));
        summary.AppendLine();

        foreach (var changelist in changelistDetails.OrderBy(c => c.ChangeNumber))
        {
            summary.AppendLine($"🔹 Changelist {changelist.ChangeNumber}");
            summary.AppendLine($"   作者: {changelist.Author}");
            summary.AppendLine($"   时间: {changelist.Date}");
            summary.AppendLine($"   状态: {changelist.Status}");
            summary.AppendLine($"   描述: {changelist.Description}");

            if (changelist.Files != null && changelist.Files.Count > 0)
            {
                summary.AppendLine($"   文件 ({changelist.Files.Count} 个):");
                foreach (var file in changelist.Files.Take(10)) // 最多显示10个文件
                {
                    summary.AppendLine($"     • {file.Action}: {file.DepotPath}");
                }
                if (changelist.Files.Count > 10)
                {
                    summary.AppendLine($"     ... 还有 {changelist.Files.Count - 10} 个文件");
                }
            }
            summary.AppendLine();
        }

        return summary.ToString();
    }
}