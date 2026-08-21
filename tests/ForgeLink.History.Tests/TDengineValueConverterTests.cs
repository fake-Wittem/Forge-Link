// 文件说明：验证 TDengine BINARY 字段的 UTF-8 文本转换。
// 责任边界：不连接真实 TDengine。

using System.Text;
using ForgeLink.History.TDengine;
using Xunit;

namespace ForgeLink.History.Tests;

public sealed class TDengineValueConverterTests
{
    [Fact]
    public void ToText_ShouldDecodeBinaryAndTrimPadding()
    {
        byte[] bytes = [.. Encoding.UTF8.GetBytes("Good"), 0, 0];
        Assert.Equal("Good", TDengineValueConverter.ToText(bytes));
        Assert.Equal("Double", TDengineValueConverter.ToText("Double"));
    }
}
