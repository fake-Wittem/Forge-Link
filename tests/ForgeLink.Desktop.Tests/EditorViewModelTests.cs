// 文件说明：验证设备和点位编辑器状态已从页面协调逻辑中独立。
// 责任边界：不启动 WPF 窗口，不访问 Collector Service。

using System.Globalization;
using ForgeLink.Desktop.Converters;
using ForgeLink.Desktop.ViewModels.Editors;
using ForgeLink.Domain;
using Xunit;

namespace ForgeLink.Desktop.Tests;

public sealed class EditorViewModelTests
{
    [Fact]
    public void PointOptionDescription_ShouldUseChineseHistoryLabels()
    {
        PointOptionDescriptionConverter converter = new();

        object description = converter.Convert(
            HistoryRecordMode.ChangeWithHeartbeat,
            typeof(string),
            parameter: null!,
            CultureInfo.GetCultureInfo("zh-CN"));

        Assert.Equal("变化时记录（含心跳）", description);
    }

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
        DeviceDefinition device = new(Guid.NewGuid(), "PLC-A", "Modbus TCP", "192.0.2.10", 502, true, 1000);
        PointEditorViewModel editor = new();
        editor.OpenNew(device, []);
        editor.Code = "TEMP_01";
        editor.Name = "温度";
        editor.Address = "HR:0";
        editor.GroupName = "温控系统";
        (PointDefinition point, IReadOnlyList<string> errors) = editor.Build();
        Assert.Empty(errors);
        Assert.NotEqual(Guid.Empty, point.Id);
        Assert.Equal(device.Id, point.DeviceId);
        Assert.Same(device, editor.SelectedDevice);
        Assert.Equal("温控系统", point.GroupName);
    }

    [Fact]
    public void PointEditor_ShouldRestoreAndChangeSelectedDevice()
    {
        DeviceDefinition firstDevice = new(Guid.NewGuid(), "PLC-A", "Modbus TCP", "192.0.2.10", 502, true, 1000);
        DeviceDefinition secondDevice = new(Guid.NewGuid(), "PLC-B", "Modbus TCP", "192.0.2.11", 502, true, 1000);
        PointDefinition point = new(Guid.NewGuid(), secondDevice.Id, "PRESSURE_01", "管路压力", "D102",
            PointDataType.Float, 0.01D, 0D, "MPa", 1000, 0.01D, HistoryRecordMode.ChangeWithHeartbeat,
            true, false, GroupName: "空压系统");
        PointEditorViewModel editor = new();

        PointDefinition otherGroup = point with { Id = Guid.NewGuid(), GroupName = "能耗" };
        PointDefinition firstDevicePoint = point with { Id = Guid.NewGuid(), DeviceId = firstDevice.Id, GroupName = "温控" };
        editor.Open(point, [firstDevice, secondDevice], [point, otherGroup, firstDevicePoint]);

        Assert.Same(secondDevice, editor.SelectedDevice);
        Assert.Equal("空压系统", editor.GroupName);
        Assert.Equal(["空压系统", "能耗"], editor.GroupSuggestions);
        editor.SelectedDevice = firstDevice;
        Assert.Equal(firstDevice.Id, editor.DeviceId);
        Assert.Equal(["温控"], editor.GroupSuggestions);
    }
}
