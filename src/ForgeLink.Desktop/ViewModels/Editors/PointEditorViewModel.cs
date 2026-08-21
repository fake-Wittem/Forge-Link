// 文件说明：封装点位新增和编辑表单状态及领域校验。
// 责任边界：不调用服务 API，不管理点位列表或 CSV。

using CommunityToolkit.Mvvm.ComponentModel;
using ForgeLink.Domain;

namespace ForgeLink.Desktop.ViewModels.Editors;

/// <summary>表示点位编辑表单。</summary>
public partial class PointEditorViewModel : ObservableObject
{
    [ObservableProperty] private Guid _id;
    [ObservableProperty] private Guid _deviceId;
    [ObservableProperty] private string _code = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _address = string.Empty;
    [ObservableProperty] private PointDataType _dataType = PointDataType.Double;
    [ObservableProperty] private double _scale = 1D;
    [ObservableProperty] private double _offset;
    [ObservableProperty] private string _unit = string.Empty;
    [ObservableProperty] private int _intervalMs = 1000;
    [ObservableProperty] private double _deadband;
    [ObservableProperty] private HistoryRecordMode _historyMode = HistoryRecordMode.ChangeWithHeartbeat;
    [ObservableProperty] private bool _isEnabled = true;
    [ObservableProperty] private bool _allowWrite;
    [ObservableProperty] private RegisterByteOrder _byteOrder = RegisterByteOrder.BigEndian;
    [ObservableProperty] private RegisterWordOrder _wordOrder = RegisterWordOrder.HighWordFirst;
    [ObservableProperty] private int _stringLength;
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private bool _isNew;

    public IReadOnlyList<PointDataType> DataTypes { get; } = Enum.GetValues<PointDataType>();
    public IReadOnlyList<HistoryRecordMode> HistoryModes { get; } = Enum.GetValues<HistoryRecordMode>();
    public IReadOnlyList<RegisterByteOrder> ByteOrders { get; } = Enum.GetValues<RegisterByteOrder>();
    public IReadOnlyList<RegisterWordOrder> WordOrders { get; } = Enum.GetValues<RegisterWordOrder>();

    /// <summary>重置为指定设备下的新增点位默认值。</summary>
    public void OpenNew(Guid firstDeviceId)
    {
        Id = Guid.Empty; DeviceId = firstDeviceId; Code = string.Empty; Name = string.Empty; Address = string.Empty;
        DataType = PointDataType.Double; Scale = 1D; Offset = 0D; Unit = string.Empty; IntervalMs = 1000;
        Deadband = 0D; HistoryMode = HistoryRecordMode.ChangeWithHeartbeat; IsEnabled = true; AllowWrite = false;
        ByteOrder = RegisterByteOrder.BigEndian; WordOrder = RegisterWordOrder.HighWordFirst; StringLength = 0;
        IsNew = true; IsOpen = true;
    }

    /// <summary>加载已有点位。</summary>
    public void Open(PointDefinition point)
    {
        Id = point.Id; DeviceId = point.DeviceId; Code = point.Code; Name = point.Name; Address = point.Address;
        DataType = point.DataType; Scale = point.Scale; Offset = point.Offset; Unit = point.Unit;
        IntervalMs = point.ScanIntervalMs; Deadband = point.Deadband; HistoryMode = point.HistoryMode;
        IsEnabled = point.IsEnabled; AllowWrite = point.AllowWrite; ByteOrder = point.ByteOrder;
        WordOrder = point.WordOrder; StringLength = point.StringLength; IsNew = false; IsOpen = true;
    }

    /// <summary>构造领域点位并返回全部校验错误。</summary>
    public (PointDefinition Point, IReadOnlyList<string> Errors) Build()
    {
        PointDefinition point = new(IsNew ? Guid.NewGuid() : Id, DeviceId, Code.Trim(), Name.Trim(), Address.Trim(),
            DataType, Scale, Offset, Unit.Trim(), IntervalMs, Deadband, HistoryMode, IsEnabled, AllowWrite,
            ByteOrder, WordOrder, StringLength);
        return (point, point.Validate());
    }
}
