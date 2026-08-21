// 文件说明：封装桌面端点位 CSV 文件选择与异步读写。
// 责任边界：只访问用户明确选择的文件，不解析 CSV 或访问配置数据库。

using Microsoft.Win32;
using System.IO;

namespace ForgeLink.Desktop.Services;

/// <summary>使用 Windows 文件选择器导入和导出点位配置文件。</summary>
public sealed class PointCsvFileService
{
    /// <summary>选择保存位置并写入服务端导出的 CSV 字节。</summary>
    /// <returns>用户完成保存时返回 true，取消时返回 false。</returns>
    public async Task<bool> SaveAsync(byte[] content, CancellationToken cancellationToken)
    {
        SaveFileDialog dialog = new()
        {
            Title = "导出 ForgeLink 点位配置",
            Filter = "CSV 文件 (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = "forgelink-points.csv"
        };
        if (dialog.ShowDialog() != true) return false;
        await File.WriteAllBytesAsync(dialog.FileName, content, cancellationToken);
        return true;
    }

    /// <summary>选择点位 CSV 并读取全部文本。</summary>
    /// <returns>用户选择的 CSV 文本；取消时返回 null。</returns>
    public async Task<string?> OpenAsync(CancellationToken cancellationToken)
    {
        OpenFileDialog dialog = new()
        {
            Title = "导入 ForgeLink 点位配置",
            Filter = "CSV 文件 (*.csv)|*.csv",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog() == true
            ? await File.ReadAllTextAsync(dialog.FileName, cancellationToken)
            : null;
    }
}
