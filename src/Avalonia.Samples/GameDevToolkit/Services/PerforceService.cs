using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GameDevToolkit.Models;

namespace GameDevToolkit.Services;

public class PerforceService
{
    private string? _cachedCurrentUser = null;

    /// <summary>
    /// 获取指定文件的最新changelist ID
    /// </summary>
    /// <param name="clientName">P4 客户端名</param>
    /// <param name="filePath">文件的绝对路径</param>
    /// <returns>changelist ID，如果出错返回错误信息</returns>
    public async Task<(bool, string)> GetLatestChangelistAsync(string clientName, string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return (false, "请输入有效的文件路径");
            }

            // 首先检查Perforce连接状态
            var isConnected = await IsConnectedAsync();
            if (!isConnected)
            {
                return (false, "Perforce连接失败，请检查Perforce配置和网络连接");
            }

            // 使用 p4 fstat 命令获取文件信息
            var startInfo = new ProcessStartInfo
            {
                FileName = "p4",
                Arguments = $"-c {clientName} changes -m1 \"{filePath}#have\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return (false, "无法启动Perforce进程");
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                return (false, !string.IsNullOrEmpty(error)
                    ? FormatPerforceError(error.Trim(), filePath)
                    : "文件可能不在Perforce管理下或未提交");
            }

            // 解析输出获取headChange字段
            return (true, output.Split(' ')[1]);
        }
        catch (Exception ex)
        {
            return (false, $"执行错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 格式化Perforce错误信息，提供更友好的提示
    /// </summary>
    private string FormatPerforceError(string error, string filePath)
    {
        if (error.Contains("not under client's root"))
        {
            return $"路径错误: 文件路径不在当前Perforce客户端根目录下\n" +
                   $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                   $"文件路径: {filePath}\n" +
                   $"错误详情: {error}\n" +
                   $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
                   $"可能的解决方案:\n" +
                   $"1. 选择正确的Perforce工作空间：使用下拉菜单切换到包含该文件路径的工作空间\n" +
                   $"2. 确认文件路径格式正确：使用绝对路径，避免相对路径\n" +
                   $"3. 检查文件是否已提交到Perforce：使用 'p4 fstat {filePath}' 确认文件状态\n" +
                   $"4. 如果文件在本地分支，确保选择了对应分支的工作空间\n" +
                   $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━";
        }
        else if (error.Contains("no such file"))
        {
            return $"文件不存在: {filePath}\n" +
                   $"建议: \n" +
                   $"1. 确认文件路径是否正确\n" +
                   $"2. 检查文件是否存在\n" +
                   $"3. 确认文件是否已提交到Perforce";
        }
        else if (error.Contains("not opened for edit"))
        {
            return $"文件状态错误: 文件未在Perforce中打开或提交\n" +
                   $"建议: \n" +
                   $"1. 确认文件已提交到Perforce\n" +
                   $"2. 检查当前工作空间配置";
        }
        else if (error.Contains("protected namespace"))
        {
            return $"权限错误: 文件所在的受保护命名空间\n" +
                   $"文件路径: {filePath}\n" +
                   $"建议: \n" +
                   $"1. 确认你有访问该文件的权限\n" +
                   $"2. 联系Perforce管理员获取访问权限";
        }

        return $"Perforce错误: {error}\n文件路径: {filePath}";
    }

    /// <summary>
    /// 获取当前用户相关的Perforce客户端
    /// </summary>
    public async Task<List<PerforceClient>> GetClientsAsync()
    {
        var clients = new List<PerforceClient>();

        try
        {
            // 使用缓存的用户名，如果没有则获取并缓存
            if (string.IsNullOrEmpty(_cachedCurrentUser))
            {
                _cachedCurrentUser = await GetCurrentUserAsync();
            }

            var currentUser = _cachedCurrentUser;
            if (string.IsNullOrEmpty(currentUser))
            {
                return clients;
            }

            // 使用 p4 clients -u 只获取当前用户的客户端
            var startInfo = new ProcessStartInfo
            {
                FileName = "p4",
                Arguments = $"clients -u {currentUser}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return clients;

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                // 如果 -u 参数失败，回退到获取所有客户端然后过滤
                return clients;
            }

            // 解析客户端列表
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("Client "))
                {
                    // 使用正则表达式解析客户端信息
                    var client = ParseClientLine(line);
                    if (client != null)
                    {
                        clients.Add(client);
                    }
                }
            }
        }
        catch
        {
            // 返回空列表，UI层面会处理错误
        }

        return clients;
    }

    
    /// <summary>
    /// 解析客户端行信息
    /// </summary>
    private PerforceClient? ParseClientLine(string line)
    {
        try
        {
            // 实际格式: Client 0.9 2024/06/07 root E:\aki0.9 'Created by qinxusheng. '
            // 格式: Client <名称> <日期> root <路径> '<描述>'
            var match = System.Text.RegularExpressions.Regex.Match(
                line,
                @"^Client\s+(?<Name>\S+)\s+(?<Date>\d{4}/\d{2}/\d{2})\s+(?<Host>\S+)\s+(?<Root>[^\r\n']+)\s+'(?<Description>.*)'");

            if (match.Success)
            {
                var clientName = match.Groups["Name"].Value;
                var date = match.Groups["Date"].Value;
                var root = match.Groups["Root"].Value;
                var description = match.Groups["Description"].Value;

                // 检查根目录是否存在（过滤掉无效的workspace）
                if (System.IO.Directory.Exists(root))
                {
                    return new PerforceClient
                    {
                        Name = clientName,
                        Root = root,
                        Owner = ExtractOwnerFromDescription(description),
                        Description = description,
                        LastModified = date,
                        IsValid = true
                    };
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 从描述中提取所有者信息
    /// </summary>
    private string ExtractOwnerFromDescription(string description)
    {
        try
        {
            var match = System.Text.RegularExpressions.Regex.Match(description, @"Created by\s+([^\s\.]+)");
            if (match.Success)
            {
                return match.Groups[1].Value;
            }

            return "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    /// <summary>
    /// 获取特定客户端的详细信息
    /// </summary>
    private async Task<PerforceClient?> GetClientDetailsAsync(string clientName)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "p4",
                Arguments = $"client -o {clientName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0) return null;

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            string? clientRoot = null;
            string? owner = null;
            string? description = null;

            foreach (var line in lines)
            {
                if (line.StartsWith("Root:"))
                {
                    clientRoot = line.Substring("Root:".Length).Trim();
                }
                else if (line.StartsWith("Owner:"))
                {
                    owner = line.Substring("Owner:".Length).Trim();
                }
                else if (line.StartsWith("Description:"))
                {
                    description = line.Substring("Description:".Length).Trim();
                }
            }

            return new PerforceClient
            {
                Name = clientName,
                Root = clientRoot ?? "Unknown",
                Owner = owner ?? "Unknown",
                Description = description ?? ""
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取当前激活的客户端名称
    /// </summary>
    public async Task<string?> GetCurrentClientAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "p4",
                Arguments = "client -o",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0) return null;

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("Client:"))
                {
                    return line.Substring("Client:".Length).Trim();
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 检查Perforce连接是否正常
    /// </summary>
    public async Task<bool> IsConnectedAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "p4",
                Arguments = "info",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            await process.WaitForExitAsync();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 设置P4客户端
    /// </summary>
    private async Task SetClientAsync(string clientName)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "p4",
                Arguments = $"client -c {clientName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
            }
        }
        catch
        {
            // 忽略设置客户端的错误
        }
    }

    /// <summary>
    /// 获取当前Perforce用户名（使用缓存）
    /// </summary>
    public async Task<string?> GetCurrentUserAsync()
    {
        try
        {
            // 使用缓存的用户名，如果没有则获取并缓存
            if (string.IsNullOrEmpty(_cachedCurrentUser))
            {
                _cachedCurrentUser = await GetCurrentUserInternalAsync();
            }

            return _cachedCurrentUser;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取当前Perforce用户名（内部实现）
    /// </summary>
    private async Task<string?> GetCurrentUserInternalAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "p4",
                Arguments = "user -o",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0) return null;

            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("User:"))
                {
                    return line.Substring("User:".Length).Trim();
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 根据Change号查询Shelved Files
    /// </summary>
    /// <param name="clientName">P4 客户端名</param>
    /// <param name="changeNumber">Change号</param>
    /// <param name="onResult">实时输出回调函数</param>
    /// <returns>Shelved文件列表，如果出错返回错误信息</returns>
    public async Task<(bool, List<ShelvedFile>?, string)> GetShelvedFilesAsync(string clientName, int changeNumber, Action<string>? onResult = null)
    {
        try
        {
            // 首先检查Perforce连接状态
            var isConnected = await IsConnectedAsync();
            if (!isConnected)
            {
                onResult?.Invoke("❌ Perforce连接失败，请检查Perforce配置和网络连接");
                return (false, null, "Perforce连接失败");
            }

            onResult?.Invoke($"🔍 开始查询Change {changeNumber} 的Shelved Files...");

            // 首先检查Change是否存在以及属于哪个客户端
            var changeCheckResult = await ValidateChangeForClientAsync(changeNumber, clientName);
            if (!changeCheckResult.Item1)
            {
                onResult?.Invoke($"❌ {changeCheckResult.Item2}");
                return (false, null, changeCheckResult.Item2);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "p4",
                Arguments = $"describe -S -s {changeNumber}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                onResult?.Invoke("❌ 无法启动Perforce进程");
                return (false, null, "无法启动Perforce进程");
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var errorMessage = error.Trim();
                if (string.IsNullOrEmpty(errorMessage))
                {
                    onResult?.Invoke($"❌ Change {changeNumber} 不存在或无权访问");
                    return (false, null, $"Change {changeNumber} 不存在或无权访问");
                }

                onResult?.Invoke($"❌ 查询失败: {errorMessage}");
                return (false, null, $"查询失败: {errorMessage}");
            }

            onResult?.Invoke($"✅ 成功获取Change {changeNumber} 的信息，开始解析Shelved Files...");

            // 解析输出获取shelved files信息
            var shelvedFiles = await ParseShelvedFiles(output, clientName, onResult);
            return (true, shelvedFiles, "");
        }
        catch (Exception ex)
        {
            onResult?.Invoke($"❌ 执行错误: {ex.Message}");
            return (false, null, $"执行错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 验证Change号是否属于指定的客户端
    /// </summary>
    private async Task<(bool, string)> ValidateChangeForClientAsync(int changeNumber, string clientName)
    {
        try
        {
            // 使用 p4 changes 命令检查change是否存在
            var changeInfoStartInfo = new ProcessStartInfo
            {
                FileName = "p4",
                Arguments = $"describe -s {changeNumber}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var changeInfoProcess = Process.Start(changeInfoStartInfo);
            if (changeInfoProcess == null)
            {
                return (false, "无法启动Perforce进程验证Change信息");
            }

            var changeInfoOutput = await changeInfoProcess.StandardOutput.ReadToEndAsync();
            var changeInfoError = await changeInfoProcess.StandardError.ReadToEndAsync();
            await changeInfoProcess.WaitForExitAsync();

            if (changeInfoProcess.ExitCode != 0)
            {
                return (false, changeInfoError);
            }

            // 解析change信息，获取所属客户端
            var changeLine = changeInfoOutput.Trim();
            if (!changeLine.Contains($"@{clientName}"))
            {
                // 提取change的实际客户端名
                var atIndex = changeLine.IndexOf('@');
                if (atIndex >= 0)
                {
                    var afterAt = changeLine.Substring(atIndex + 1);
                    var spaceIndex = afterAt.IndexOf(' ');
                    var actualClient = spaceIndex >= 0 ? afterAt.Substring(0, spaceIndex) : afterAt;

                    return (false,
                        $"Change {changeNumber} 属于客户端 '{actualClient}'，但当前选择的是 '{clientName}'。请选择正确的客户端后重试。");
                }
                else
                {
                    return (false, $"Change {changeNumber} 不属于当前选择的客户端 '{clientName}'");
                }
            }

            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, $"验证Change信息时出错: {ex.Message}");
        }
    }

    /// <summary>
    /// 解析p4 describe输出，提取C# Shelved Files的路径信息，并实时输出结果
    /// </summary>
    private async Task<List<ShelvedFile>> ParseShelvedFiles(string describeOutput, string clientName, Action<string>? onResult)
    {
        var shelvedFiles = new List<ShelvedFile>();

        try
        {
            var lines = describeOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var depotPaths = new List<string>();
            int processedCount = 0;

            // 第一步：解析出所有需要处理的 depot paths
            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                // 使用正则表达式解析文件信息，包含三个点
                // 格式示例: ... //aki/branch_3.0/Source/Client/CSharpScript/CSharpScript/Core/Audio/AudioSystem.cs#3 edit
                var match = System.Text.RegularExpressions.Regex.Match(trimmedLine, @"^\.\.\.\s+(?<DepotPath>//[^\s#]+)#(?<Revision>\d+)\s+(?<Action>\w+)$");

                if (match.Success)
                {
                    var depotPath = match.Groups["DepotPath"].Value;

                    // 只处理C#文件（.cs扩展名）
                    if (depotPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                    {
                        depotPaths.Add(depotPath);
                    }
                }
            }

            onResult?.Invoke($"开始处理 {depotPaths.Count} 个 C# 文件（并发处理）...\n");

            // 第二步：分批并发处理文件，每批30个
            const int batchSize = 30;
            var totalBatches = (int)Math.Ceiling((double)depotPaths.Count / batchSize);

            for (int batchIndex = 0; batchIndex < totalBatches; batchIndex++)
            {
                var batch = depotPaths.Skip(batchIndex * batchSize).Take(batchSize).ToList();
                onResult?.Invoke($"\n🚀 处理第 {batchIndex + 1}/{totalBatches} 批 ({batch.Count} 个文件)...");

                // 创建并发任务
                var batchTasks = batch.Select<string, Task<(string DepotPath, string? Error, ShelvedFile? Result, string? CSPath, string? TSPath, string? Changelist)>>(async depotPath =>
                {
                    try
                    {
                        // 步骤1: 获取 CS 文件的完整路径信息
                        var pathInfo = await GetFilePathsAsync(depotPath, clientName);
                        if (pathInfo == null)
                        {
                            return (depotPath, "无法获取路径信息", null, null, null, null);
                        }

                        var csLocalPath = pathInfo.Value.LocalPath;

                        // 步骤2: 根据 CS 路径计算出对应的 TS 路径
                        var tsLocalPath = CalculateTsPath(csLocalPath);

                        // 步骤3: 根据 TS 路径获取最新的 changelist 号
                        var changelistResult = await GetLatestChangelistForFileAsync(clientName, tsLocalPath);

                        // 创建 ShelvedFile 对象
                        var shelvedFile = new ShelvedFile
                        {
                            DepotPath = depotPath,
                            ClientPath = pathInfo.Value.ClientPath,
                            LocalPath = csLocalPath
                        };

                        return (depotPath, null, shelvedFile, csLocalPath, tsLocalPath, changelistResult.IsSuccess ? changelistResult.Changelist : changelistResult.ErrorMessage);
                    }
                    catch (Exception ex)
                    {
                        return (depotPath, ex.Message, null, null, null, null);
                    }
                });

                // 等待当前批次完成
                var batchResults = await Task.WhenAll(batchTasks);

                // 输出当前批次结果
                foreach (var result in batchResults)
                {
                    processedCount++;

                    if (result.Error != null)
                    {
                        onResult?.Invoke($"❌ 处理失败: {System.IO.Path.GetFileName(result.DepotPath)} - {result.Error}");
                    }
                    else if (result.Result != null)
                    {
                        onResult?.Invoke($"🔍 {System.IO.Path.GetFileName(result.DepotPath)}");
                        onResult?.Invoke($"✅ CS路径: {result.CSPath}");
                        onResult?.Invoke($"🔄 对应TS路径: {result.TSPath}");

                        if (result.Changelist is not null && result.Changelist.All(char.IsDigit))
                        {
                            onResult?.Invoke($"📝 TS文件最新Changelist: {result.Changelist}");

                            // 步骤4: 将翻译详情写入TS文件
                            var writeSuccess = await WriteChangelistToTsFileAsync(result.TSPath!, result.DepotPath, result.Changelist!, null);
                            if (writeSuccess)
                            {
                                onResult?.Invoke($"💾 已将翻译详情写入文件");
                            }
                            else
                            {
                                onResult?.Invoke($"⚠️ 写入翻译详情到文件失败: {System.IO.Path.GetFileName(result.TSPath!)}");
                            }
                        }
                        else
                        {
                            onResult?.Invoke($"⚠️ 无法获取TS文件的Changelist: {result.Changelist}");
                        }

                        shelvedFiles.Add(result.Result);
                        onResult?.Invoke($"✅ 批次内完成 ({processedCount}/{depotPaths.Count})");
                    }
                }

                onResult?.Invoke($"📦 第 {batchIndex + 1}/{totalBatches} 批完成，已处理 {processedCount}/{depotPaths.Count} 个文件");
            }

            onResult?.Invoke($"\n🎉 全部处理完成！共处理 {shelvedFiles.Count} 个文件（并发处理）");
        }
        catch (Exception ex)
        {
            onResult?.Invoke($"❌ 解析过程出错: {ex.Message}");
        }

        return shelvedFiles;
    }

    /// <summary>
    /// 根据 CS 路径计算出对应的 TS 路径
    /// </summary>
    private string CalculateTsPath(string csPath)
    {
        // 将 \CSharpScript\CSharpScript\ 替换为 \TypeScript\Src\
        // 将 .cs 替换为 .ts
        return csPath.Replace("\\CSharpScript\\CSharpScript\\", "\\TypeScript\\Src\\").Replace(".cs", ".ts");
    }

    /// <summary>
    /// 获取指定文件的最新changelist ID
    /// </summary>
    private async Task<(bool IsSuccess, string Changelist, string ErrorMessage)> GetLatestChangelistForFileAsync(string clientName, string filePath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "p4",
                Arguments = $"-c {clientName} changes -m1 \"{filePath}#have\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return (false, "", "无法启动Perforce进程");

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var errorMessage = !string.IsNullOrEmpty(error) ? error.Trim() : "文件可能不在Perforce管理下或未提交";
                return (false, "", errorMessage);
            }

            // 解析输出获取changelist
            var parts = output.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return (true, parts[1], "");
            }

            return (false, "", "无法解析changelist信息");
        }
        catch (Exception ex)
        {
            return (false, "", $"执行错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 将翻译详情写入TS文件开头作为注释
    /// </summary>
    /// <param name="filePath">TS文件路径</param>
    /// <param name="csDepotPath">C#文件的DepotPath</param>
    /// <param name="changelist">TS文件的changelist号</param>
    /// <param name="onResult">实时输出回调函数</param>
    /// <returns>是否写入成功</returns>
    public async Task<bool> WriteChangelistToTsFileAsync(string filePath, string csDepotPath, string changelist, Action<string>? onResult = null)
    {
        try
        {
            onResult?.Invoke($"📝 开始将翻译详情写入文件: {System.IO.Path.GetFileName(filePath)}");

            // 检查文件是否存在
            if (!System.IO.File.Exists(filePath))
            {
                onResult?.Invoke($"❌ 文件不存在: {filePath}");
                return false;
            }

            // 按行读取TS文件内容，检查是否已有翻译详情
            var tsLines = new List<string>();
            var hasTranslationDetails = false;
            var translationDetailsStartIndex = -1;
            var translationDetailsEndIndex = -1;
            var lineCount = 0;
            var inCommentBlock = false;

            try
            {
                using var reader = new System.IO.StreamReader(filePath);
                string? line;

                while ((line = await reader.ReadLineAsync()) != null)
                {
                    tsLines.Add(line);
                    lineCount++;

                    var trimmedLine = line.Trim();
                    var lowerLine = trimmedLine.ToLower();

                    // 检测注释块开始 /**
                    if (trimmedLine.StartsWith("/**") && !hasTranslationDetails)
                    {
                        inCommentBlock = true;
                        translationDetailsStartIndex = lineCount - 1;
                    }
                    // 在注释块内检查是否包含翻译详情
                    else if (inCommentBlock)
                    {
                        if (lowerLine.Contains("翻译详情") || lowerLine.Contains("translation details"))
                        {
                            hasTranslationDetails = true;
                        }
                        // 检测注释块结束 */
                        if (trimmedLine.EndsWith("*/"))
                        {
                            inCommentBlock = false;
                            if (hasTranslationDetails)
                            {
                                translationDetailsEndIndex = lineCount - 1;
                            }
                            else
                            {
                                // 如果不是翻译详情注释块，重置开始索引
                                translationDetailsStartIndex = -1;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                onResult?.Invoke($"❌ 读取TS文件失败: {filePath} - {ex.Message}");
                return false;
            }

            // 解析现有的翻译详情（如果存在）
            var existingTranslator = "";
            var existingCsPath = "";
            if (hasTranslationDetails && translationDetailsStartIndex >= 0)
            {
                onResult?.Invoke($"🔍 发现已有翻译详情，解析现有信息...");
                ParseExistingTranslationDetails(tsLines, translationDetailsStartIndex, translationDetailsEndIndex, out existingTranslator, out existingCsPath);
                onResult?.Invoke($"📝 现有信息: 翻译人={existingTranslator}, 路径={existingCsPath}");
            }
            else
            {
                onResult?.Invoke($"📝 文件暂无翻译详情，将新增...");
            }

            // 计算对应的C#文件路径
            var csFilePath = filePath.Replace("\\TypeScript\\Src\\", "\\CSharpScript\\CSharpScript\\").Replace(".ts", ".cs");

            // 检查C#文件是否存在并统计TODO
            var todoCount = 0;
            if (System.IO.File.Exists(csFilePath))
            {
                try
                {
                    using var csReader = new System.IO.StreamReader(csFilePath);
                    string? csLine;

                    while ((csLine = await csReader.ReadLineAsync()) != null)
                    {
                        // 检查整个文件的所有行，不限制行数
                        if (System.Text.RegularExpressions.Regex.IsMatch(csLine, @"//\s*todo|#\s*todo|/\*\s*todo\s*\*/|\*\s*todo", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        {
                            todoCount++;
                        }
                    }

                    onResult?.Invoke($"🔍 检查C#文件: {System.IO.Path.GetFileName(csFilePath)} (共检查整个文件)");
                }
                catch (Exception ex)
                {
                    onResult?.Invoke($"⚠️ 读取C#文件失败，默认认为已翻译完成: {csFilePath} - {ex.Message}");
                }
            }
            else
            {
                onResult?.Invoke($"⚠️ 找不到对应的C#文件: {csFilePath}，默认认为已翻译完成");
            }

            var status = todoCount > 0 ? $"未翻译完成 (共{todoCount}个todo)" : "已翻译完成";
            onResult?.Invoke($"📊 翻译状态: {status}");

            // 获取当前用户
            var currentUser = await GetCurrentUserAsync() ?? "Unknown";

            // 生成翻译详情注释
            var translationDetails = GenerateTranslationDetails(currentUser, csDepotPath, status, changelist);

            // 处理翻译详情的替换或新增
            if (hasTranslationDetails && translationDetailsStartIndex >= 0)
            {
                // 替换现有的翻译详情块
                var startIdx = translationDetailsStartIndex;
                var endIdx = translationDetailsEndIndex >= 0 ? translationDetailsEndIndex : translationDetailsStartIndex;

                // 删除旧的翻译详情块
                for (int i = endIdx; i >= startIdx; i--)
                {
                    if (i < tsLines.Count)
                    {
                        tsLines.RemoveAt(i);
                    }
                }

                // 在相同位置插入新的翻译详情
                tsLines.Insert(startIdx, translationDetails);
                onResult?.Invoke($"🔄 替换现有翻译详情 (第{startIdx + 1}行)");
            }
            else
            {
                // 新增翻译详情注释
                var insertIndex = 0;

                // 跳过可能的文件头注释（如shebang或空行）
                while (insertIndex < tsLines.Count &&
                      (tsLines[insertIndex].Trim().StartsWith("///") ||
                       tsLines[insertIndex].Trim().StartsWith("/*") ||
                       tsLines[insertIndex].Trim().StartsWith("*") ||
                       string.IsNullOrWhiteSpace(tsLines[insertIndex])))
                {
                    insertIndex++;
                }

                tsLines.Insert(insertIndex, translationDetails);
                onResult?.Invoke($"➕ 在第{insertIndex + 1}行添加翻译详情注释");
            }

            // 写回文件
            await System.IO.File.WriteAllTextAsync(filePath, string.Join('\n', tsLines));

            onResult?.Invoke($"✅ 成功写入翻译详情到文件: {System.IO.Path.GetFileName(filePath)}");
            return true;
        }
        catch (Exception ex)
        {
            onResult?.Invoke($"❌ 写入翻译详情到文件失败: {filePath} - {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 检查文件前N行是否包含翻译详情注释
    /// </summary>
    private bool HasTranslationDetailsInFirstLines(List<string> lines, int lineCount)
    {
        var checkLines = lines.Take(Math.Min(lineCount, lines.Count));
        var combinedText = string.Join("\n", checkLines).ToLower();

        return combinedText.Contains("翻译详情") ||
               combinedText.Contains("translation details") ||
               combinedText.Contains("翻译人") ||
               combinedText.Contains("对应路径");
    }

    /// <summary>
    /// 解析现有的翻译详情注释
    /// </summary>
    private void ParseExistingTranslationDetails(List<string> lines, int startIndex, int endIndex, out string translator, out string csPath)
    {
        translator = "";
        csPath = "";

        try
        {
            var endIdx = endIndex >= 0 ? endIndex : Math.Min(startIndex + 10, lines.Count - 1);

            for (int i = startIndex; i <= endIdx && i < lines.Count; i++)
            {
                var line = lines[i].Trim();

                // 解析翻译人
                if (line.Contains("翻译人:") || line.Contains("翻译人："))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"翻译人[：:]\s*(.+)");
                    if (match.Success)
                    {
                        translator = match.Groups[1].Value.Trim();
                    }
                }
                // 解析对应路径
                else if (line.Contains("对应路径:") || line.Contains("对应路径："))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(line, @"对应路径[：:]\s*(.+)");
                    if (match.Success)
                    {
                        csPath = match.Groups[1].Value.Trim();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // 解析失败时保持空值
            System.Diagnostics.Debug.WriteLine($"解析翻译详情失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 统计文件中TODO注释的数量
    /// </summary>
    private int CountTodoComments(string content)
    {
        // 匹配各种TODO注释格式
        var todoPattern = @"//\s*todo|#\s*todo|/\*\s*todo\s*\*/|\*\s*todo";
        var matches = System.Text.RegularExpressions.Regex.Matches(content, todoPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return matches.Count;
    }

    /// <summary>
    /// 生成翻译详情注释字符串
    /// </summary>
    private string GenerateTranslationDetails(string translator, string csDepotPath, string status, string tsChangelist)
    {
        var details = new StringBuilder();
        details.AppendLine("/**");
        details.AppendLine(" * ---翻译详情---");
        details.AppendLine($" * 翻译人: {translator}");
        details.AppendLine($" * 对应路径: {csDepotPath}");
        details.AppendLine($" * 状态: {status}");
        details.AppendLine($" * 其他: 对应TS文件版本: {tsChangelist}");
        details.AppendLine(" */");

        return details.ToString();
    }

    /// <summary>
    /// 使用 p4 where 命令获取文件的完整路径信息
    /// </summary>
    private async Task<(string ClientPath, string LocalPath)?> GetFilePathsAsync(string depotPath, string clientName)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "p4",
                Arguments = $"-c {clientName} where \"{depotPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0) return null;

            // 解析 p4 where 输出
            var resultLine = output.Trim();
            var parts = resultLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length >= 3)
            {
                var clientPath = parts[1];
                var localPath = parts[2];
                return (clientPath, localPath);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 获取changelist详细信息
    /// </summary>
    /// <param name="clientName">P4 客户端名</param>
    /// <param name="changeNumber">changelist号</param>
    /// <returns>changelist详细信息</returns>
    public async Task<(bool, ChangelistInfo?, string)> GetChangelistDetailsAsync(string clientName, int changeNumber)
    {
        try
        {
            // 首先检查Perforce连接状态
            var isConnected = await IsConnectedAsync();
            if (!isConnected)
            {
                return (false, null, "Perforce连接失败");
            }

            // 设置P4客户端
            await SetClientAsync(clientName);

            // 使用p4 describe命令获取changelist详细信息
            var startInfo = new ProcessStartInfo
            {
                FileName = "p4",
                Arguments = $"describe -s {changeNumber}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            using var process = Process.Start(startInfo);
            if (process == null)
                return (false, null, "无法启动p4进程");

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                return (false, null, error ?? "查询失败");
            }

            // 解析输出
            var changelistInfo = ParseChangelistDescribeOutput(output, changeNumber);
            return (true, changelistInfo, "");
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    /// <summary>
    /// 解析p4 describe命令的输出
    /// </summary>
    private ChangelistInfo ParseChangelistDescribeOutput(string output, int changeNumber)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var changelistInfo = new ChangelistInfo
        {
            ChangeNumber = changeNumber,
            Files = new List<ChangelistFile>()
        };

        var currentSection = "description";
        var descriptionLines = new List<string>();

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            if (trimmedLine.StartsWith("Change"))
            {
                // 解析changelist基本信息
                ParseChangeInfo(trimmedLine, changelistInfo);
            }
            else if (trimmedLine.StartsWith("Client"))
            {
                var client = trimmedLine.Substring(7).Trim();
                changelistInfo.Client = client;
            }
            else if (trimmedLine.StartsWith("User"))
            {
                var user = trimmedLine.Substring(5).Trim();
                changelistInfo.Author = user;
            }
            else if (trimmedLine.StartsWith("Status"))
            {
                var status = trimmedLine.Substring(7).Trim();
                changelistInfo.Status = status;
            }
            else if (trimmedLine.StartsWith("Date"))
            {
                var date = trimmedLine.Substring(5).Trim();
                changelistInfo.Date = date;
            }
            else if (trimmedLine.StartsWith("Affected files"))
            {
                currentSection = "files";
            }
            else if (currentSection == "description" && !string.IsNullOrWhiteSpace(trimmedLine))
            {
                descriptionLines.Add(trimmedLine);
            }
            else if (currentSection == "files" && trimmedLine.StartsWith("..."))
            {
                var fileInfo = ParseFileInfo(trimmedLine);
                if (fileInfo != null)
                {
                    changelistInfo.Files.Add(fileInfo);
                }
            }
        }

        changelistInfo.Description = string.Join("\n", descriptionLines).Trim();

        return changelistInfo;
    }

    /// <summary>
    /// 解析changelist基本信息行
    /// </summary>
    private void ParseChangeInfo(string line, ChangelistInfo changelistInfo)
    {
        // 格式: Change 12345 on 2023/12/25 by author@client 'description'
        var match = System.Text.RegularExpressions.Regex.Match(line, @"Change (\d+) on (.+?) by (.+?)@(.+?) '(.*)'");
        if (match.Success)
        {
            changelistInfo.ChangeNumber = int.Parse(match.Groups[1].Value);
            changelistInfo.Date = match.Groups[2].Value;
            changelistInfo.Author = match.Groups[3].Value;
            changelistInfo.Client = match.Groups[4].Value;
            changelistInfo.Description = match.Groups[5].Value;
        }
    }

    /// <summary>
    /// 解析文件信息行
    /// </summary>
    private ChangelistFile? ParseFileInfo(string line)
    {
        // 格式: ... #1 edit //depot/path/file.cs
        var match = System.Text.RegularExpressions.Regex.Match(line, @"...\s+#(\d+)\s+(\w+)\s+(.+)");
        if (match.Success)
        {
            return new ChangelistFile
            {
                Revision = match.Groups[1].Value,
                Action = match.Groups[2].Value,
                DepotPath = match.Groups[3].Value
            };
        }
        return null;
    }
}

/// <summary>
/// Perforce客户端信息
/// </summary>
public class PerforceClient
{
    public string Name { get; set; } = string.Empty;
    public string Root { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string LastModified { get; set; } = string.Empty;
    public bool IsValid { get; set; } = true;

    public string DisplayName => $"{Name} ({Root})";

    public override string ToString()
    {
        return string.IsNullOrEmpty(Description)
            ? $"{Name} ({Root})"
            : $"{Name} - {Description} ({Root})";
    }
}