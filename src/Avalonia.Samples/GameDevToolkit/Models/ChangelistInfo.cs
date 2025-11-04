using System.Collections.Generic;

namespace GameDevToolkit.Models;

/// <summary>
/// Perforce Changelist信息模型
/// </summary>
public class ChangelistInfo
{
    /// <summary>
    /// Changelist编号
    /// </summary>
    public int ChangeNumber { get; set; }

    /// <summary>
    /// 作者
    /// </summary>
    public string Author { get; set; } = "";

    /// <summary>
    /// 客户端名称
    /// </summary>
    public string Client { get; set; } = "";

    /// <summary>
    /// 日期时间
    /// </summary>
    public string Date { get; set; } = "";

    /// <summary>
    /// 描述
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    /// 状态 (pending, submitted, etc.)
    /// </summary>
    public string Status { get; set; } = "";

    /// <summary>
    /// 文件列表
    /// </summary>
    public List<ChangelistFile>? Files { get; set; }
}

/// <summary>
/// Changelist中的文件信息
/// </summary>
public class ChangelistFile
{
    /// <summary>
    /// Depot路径
    /// </summary>
    public string DepotPath { get; set; } = "";

    /// <summary>
    /// 文件操作类型 (add, edit, delete, integrate, etc.)
    /// </summary>
    public string Action { get; set; } = "";

    /// <summary>
    /// 文件类型
    /// </summary>
    public string Type { get; set; } = "";

    /// <summary>
    /// 版本信息
    /// </summary>
    public string Revision { get; set; } = "";
}