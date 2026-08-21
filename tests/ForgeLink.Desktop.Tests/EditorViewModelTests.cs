// 文件说明：验证设备和点位编辑器状态已从页面协调逻辑中独立。
// 责任边界：不启动 WPF 窗口，不访问 Collector Service。

using ForgeLink.Desktop.ViewModels.Editors;
using ForgeLink.Domain;
using Xunit;

namespace ForgeLink.Desktop.Tests;

public sealed class EditorViewModelTests
{
    [Fact]
    public void DeviceEditor_ShouldCopyAndBuildDevice()
    {
        DeviceDefinition source = new(Guid.NewGuid(), "PLC-A", "Modbus TCP", "192.0.2.10", 502, true, 1000, 2, 2000, 1500);
        DeviceEditorViewModel editor = new();
        editor.Open(source);
        (DeviceDefinition device, IReadOnlyList<string> errors) = editor.Build();
        Assert.Empty(errors);
        Assert.Equal(source, device);
        Assert.False(editor.IsNew);
        Assert.True(editor.IsOpen);
    }

    [Fact]
    public void DeviceEditor_ShouldReportUnitAndDomainErrorsTogether()
    {
        DeviceEditorViewModel editor = new();
        editor.OpenNew();
        editor.UnitId = 300;
        editor.Name = string.Empty;
        (_, IReadOnlyList<string> errors) = editor.Build();
        Assert.Contains(errors, error => error.Contains("Unit ID", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("设备名称", StringComparison.Ordinal));
    }

    [Fact]
    public void PointEditor_ShouldCreateNewStablePoint()
    {
        Guid deviceId = Guid.NewGuid();
        PointEditorViewModel editor = new();
        editor.OpenNew(deviceId);
        editor.Code = "TEMP_01";
        editor.Name = "温度";
        editor.Address = "HR:0";
        (PointDefinition point, IReadOnlyList<string> errors) = editor.Build();
        Assert.Empty(errors);
        Assert.NotEqual(Guid.Empty, point.Id);
        Assert.Equal(deviceId, point.DeviceId);
    }
}
