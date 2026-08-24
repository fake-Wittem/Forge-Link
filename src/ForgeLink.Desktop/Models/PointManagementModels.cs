// 文件说明：定义点位管理页面的设备和点位分组导航项。
// 责任边界：只承载页面展示状态，不参与采集、持久化或接口传输。

using ForgeLink.Domain;

namespace ForgeLink.Desktop.Models;

/// <summary>表示一个设备级点位导航项。</summary>
public sealed record DevicePointFilterItem(DeviceDefinition Device, int PointCount);

/// <summary>表示设备内的一个点位分组筛选项。</summary>
public sealed record PointGroupFilterItem(string? Key, string DisplayName, int PointCount, bool IsAll = false);

/// <summary>把实时快照与对应的设备、点位配置组合为可读表格行。</summary>
public sealed record RealtimePointRow(DeviceDefinition Device, PointDefinition Point, RealtimeValueDto Value);
