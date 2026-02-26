using System.Security.Cryptography;
using System.Text;

namespace AuditHistoryExtractorPro.Desktop.Helpers;

public static class SecurityHelper
{
    // Entropía estable para DPAPI (CurrentUser). Mantener constante para poder
    // descifrar conexiones guardadas entre sesiones.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AuditHistoryExtractorPro::SavedConnections::v1");

    public static string ProtectString(string plainText)
    {
        if (string.IsNullOrWhiteSpace(plainText))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string UnprotectString(string encryptedBase64)
    {
        if (string.IsNullOrWhiteSpace(encryptedBase64))
        {
            return string.Empty;
        }

        try
        {
            var encrypted = Convert.FromBase64String(encryptedBase64);
            var decrypted = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch
        {
            return string.Empty;
        }
    }
}
