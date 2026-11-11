using System.Security.Cryptography;

namespace AlleyWatch.Worker.Security;

/// <summary>
/// Thin wrapper over Windows DPAPI, CurrentUser scope. Used to protect the Gmail refresh token at rest.
/// CurrentUser scope means only the Windows account that encrypted the data can decrypt it — which is
/// why the service must log on as the same account that ran --auth-gmail. LocalMachine scope would
/// weaken this (any local process could decrypt) and is only a fallback if the service must run as
/// LocalSystem.
/// </summary>
public static class DpapiProtector
{
    public static byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, optionalEntropy: null, DataProtectionScope.CurrentUser);

    public static byte[] Unprotect(byte[] ciphertext) =>
        ProtectedData.Unprotect(ciphertext, optionalEntropy: null, DataProtectionScope.CurrentUser);
}
