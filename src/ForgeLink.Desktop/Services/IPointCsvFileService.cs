// 文件说明：声明点位 CSV 文件选择与读写契约。
// 责任边界：便于页面 ViewModel 测试时替换系统文件对话框。

namespace ForgeLink.Desktop.Services;

/// <summary>定义用户选择的 CSV 文件读写操作。</summary>
public interface IPointCsvFileService
{
    Task<bool> SaveAsync(byte[] content, CancellationToken cancellationToken);
    Task<string?> OpenAsync(CancellationToken cancellationToken);
}
