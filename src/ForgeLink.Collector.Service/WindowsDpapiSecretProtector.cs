// 文件说明：使用 Windows DPAPI 保护 Collector Service 保存的敏感凭据。
// 责任边界：只处理加解密，不记录、传输或校验明文密码。

using System.Security.Cryptography;
using System.Text;
using ForgeLink.Application;

/// <summary>使用本机范围 DPAPI，使服务账户调整后仍可读取受 ACL 保护的配置。</summary>
internal sealed class WindowsDpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("ForgeLink.TDengine.v1");

    public byte[] Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        return ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), Entropy, DataProtectionScope.LocalMachine);
    }

    public string Unprotect(byte[] protectedPayload)
    {
        ArgumentNullException.ThrowIfNull(protectedPayload);
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedPayload, Entropy, DataProtectionScope.LocalMachine));
    }
}
