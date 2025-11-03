namespace GameDevToolkit.Models;

// ========== 新功能模型：Shelved File ==========
public class ShelvedFile
{
    public string DepotPath { get; set; } = string.Empty;
    public string ClientPath { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
}