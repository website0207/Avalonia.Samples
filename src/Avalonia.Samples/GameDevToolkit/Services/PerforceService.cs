using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using GameDevToolkit.Models;

namespace GameDevToolkit.Services;

public class PerforceService
{
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
            // 首先获取当前用户名
            var currentUser = await GetCurrentUserAsync();
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
    /// 获取当前Perforce用户名
    /// </summary>
    private async Task<string?> GetCurrentUserAsync()
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
                }).ToArray();

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

                            // 步骤4: 将changelist写入TS文件
                            var writeSuccess = await WriteChangelistToTsFileAsync(result.TSPath!, result.Changelist, null);
                            if (writeSuccess)
                            {
                                onResult?.Invoke($"💾 已将Changelist {result.Changelist} 写入文件");
                            }
                            else
                            {
                                onResult?.Invoke($"⚠️ 写入Changelist到文件失败: {System.IO.Path.GetFileName(result.TSPath!)}");
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
    /// 将changelist号写入TS文件开头作为注释
    /// </summary>
    /// <param name="filePath">TS文件路径</param>
    /// <param name="changelist">Changelist号</param>
    /// <param name="onResult">实时输出回调函数</param>
    /// <returns>是否写入成功</returns>
    public async Task<bool> WriteChangelistToTsFileAsync(string filePath, string changelist, Action<string>? onResult = null)
    {
        try
        {
            onResult?.Invoke($"📝 开始将Changelist {changelist} 写入文件: {System.IO.Path.GetFileName(filePath)}");

            // 检查文件是否存在
            if (!System.IO.File.Exists(filePath))
            {
                onResult?.Invoke($"❌ 文件不存在: {filePath}");
                return false;
            }

            // 读取文件内容
            var content = await System.IO.File.ReadAllTextAsync(filePath);
            var lines = content.Split('\n').ToList();

            // 查找是否已经存在changelist注释
            var existingChangelistIndex = lines.FindIndex(line => line.Trim().StartsWith("// Changelist:"));

            if (existingChangelistIndex >= 0)
            {
                // 更新现有的changelist注释
                lines[existingChangelistIndex] = $"// Changelist: {changelist}";
                onResult?.Invoke($"🔄 更新现有Changelist注释: {changelist}");
            }
            else
            {
                // 在文件开头添加新的changelist注释
                var insertIndex = 0;

                // 跳过可能的文件头注释（如shebang或空行）
                while (insertIndex < lines.Count &&
                      (lines[insertIndex].Trim().StartsWith("///") ||
                       lines[insertIndex].Trim().StartsWith("/*") ||
                       lines[insertIndex].Trim().StartsWith("*") ||
                       string.IsNullOrWhiteSpace(lines[insertIndex])))
                {
                    insertIndex++;
                }

                lines.Insert(insertIndex, $"// Changelist: {changelist}");
                onResult?.Invoke($"➕ 在第{insertIndex + 1}行添加Changelist注释: {changelist}");
            }

            // 写回文件
            await System.IO.File.WriteAllTextAsync(filePath, string.Join('\n', lines));

            onResult?.Invoke($"✅ 成功写入Changelist {changelist} 到文件: {System.IO.Path.GetFileName(filePath)}");
            return true;
        }
        catch (Exception ex)
        {
            onResult?.Invoke($"❌ 写入Changelist到文件失败: {filePath} - {ex.Message}");
            return false;
        }
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