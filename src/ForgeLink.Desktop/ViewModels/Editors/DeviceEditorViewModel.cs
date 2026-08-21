// 文件说明：封装设备新增和编辑表单状态及领域校验。
// 责任边界：不调用服务 API，不管理设备列表。

using CommunityToolkit.Mvvm.ComponentModel;
using ForgeLink.Domain;

namespace ForgeLink.Desktop.ViewModels.Editors;

/// <summary>表示设备编辑表单。</summary>
public partial class DeviceEditorViewModel : ObservableObject
{
    [ObservableProperty] private Guid _id;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _protocol = "Simulation";
    [ObservableProperty] private string _host = "127.0.0.1";
    [ObservableProperty] private int _port = 502;
    [ObservableProperty] private int _intervalMs = 1000;
    [ObservableProperty] private int _unitId = 1;
    [ObservableProperty] private int _connectionTimeoutMs = 3000;
    [ObservableProperty] private int _readTimeoutMs = 2000;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private bool _isNew;

    public IReadOnlyList<string> Protocols { get; } = ["Simulation", "Modbus TCP"];

    /// <summary>重置为新增设备默认值。</summary>
    public void OpenNew()
    {
        Id = Guid.Empty; Name = string.Empty; Protocol = "Simulation"; Host = "127.0.0.1";
        Port = 502; IntervalMs = 1000; UnitId = 1; ConnectionTimeoutMs = 3000;
        ReadTimeoutMs = 2000; IsEnabled = true; IsNew = true; IsOpen = true;
    }

    /// <summary>加载已有设备。</summary>
    public void Open(DeviceDefinition device)
    {
        Id = device.Id; Name = device.Name; Protocol = device.Protocol; Host = device.Host;
        Port = device.Port; IntervalMs = device.DefaultScanIntervalMs; UnitId = device.UnitId;
        ConnectionTimeoutMs = device.ConnectionTimeoutMs; ReadTimeoutMs = device.ReadTimeoutMs;
        IsEnabled = device.IsEnabled; IsNew = false; IsOpen = true;
    }

    /// <summary>构造领域设备并返回全部校验错误。</summary>
    public (DeviceDefinition Device, IReadOnlyList<string> Errors) Build()
    {
        Guid id = IsNew ? Guid.NewGuid() : Id;
        List<string> errors = [];
        byte unitId = 0;
        if (UnitId is < byte.MinValue or > byte.MaxValue) errors.Add("Unit ID 必须在 0 到 255 之间。");
        else unitId = checked((byte)UnitId);
        DeviceDefinition device = new(id, Name.Trim(), Protocol.Trim(), Host.Trim(), Port, IsEnabled, IntervalMs,
            unitId, ConnectionTimeoutMs, ReadTimeoutMs);
        errors.AddRange(device.Validate());
        return (device, errors);
    }
}
