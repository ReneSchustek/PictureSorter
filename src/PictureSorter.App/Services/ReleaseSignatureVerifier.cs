using System;
using System.IO;
using System.Security.Cryptography;

namespace PictureSorter.App.Services;

/// <summary>
/// Prüft die Echtheit eines Update-Pakets über eine losgelöste ECDSA-Signatur
/// (P-256 über SHA-256) gegen den hier einkompilierten öffentlichen Schlüssel.
///
/// Das ist der Vertrauensanker der Update-Kette – und ein bewusst anderer als eine
/// Authenticode-Signatur: Diese sagt nur „irgendein Zertifikat hat unterschrieben",
/// und ein Zertifikat kann sich jeder besorgen. Hier ist genau <em>ein</em>
/// Unterzeichner zugelassen. Selbst wer den Release-Kanal übernimmt, kann kein Update
/// unterschieben: Ohne den privaten Schlüssel entsteht keine gültige Signatur.
///
/// Fail-closed: Fehlt die Signatur oder passt sie nicht, wird das Paket verworfen.
/// </summary>
internal static class ReleaseSignatureVerifier
{
    /// <summary>
    /// Der öffentliche Schlüssel des Release-Signierers (SubjectPublicKeyInfo,
    /// Base64). Erzeugt mit <c>tools/new-signing-key.ps1</c>; der zugehörige private
    /// Schlüssel liegt offline und als CI-Secret <c>PICTURESORTER_SIGNING_KEY</c>.
    ///
    /// Ein Austausch macht bereits ausgelieferte Anwendungen update-unfähig – sie
    /// kennen nur diesen Schlüssel. Rotation deshalb nur bewusst und mit einem
    /// Übergangsplan.
    /// </summary>
    public const string PublicKeySpkiBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEaKw6jIhmwEOcRW6t55wOxcReJY/dLiAXKzECGciJ1vKjGGV4TXQjQQShl7HuSg1XiDOJXPgkD00+rNmTnZ+sWw==";

    /// <summary>
    /// Prüft, ob die Signatur zum Paket und zum bekannten Unterzeichner passt.
    /// </summary>
    /// <param name="packagePath">Das heruntergeladene Paket.</param>
    /// <param name="signature">Die losgelöste Signatur (rohe Bytes).</param>
    /// <returns><see langword="true"/>, nur wenn die Signatur gültig ist.</returns>
    public static bool IsAuthentic(string packagePath, byte[] signature) =>
        IsAuthentic(packagePath, signature, PublicKeySpkiBase64);

    /// <summary>
    /// Prüft gegen einen ausdrücklich angegebenen Schlüssel (für Tests).
    /// </summary>
    /// <param name="packagePath">Das heruntergeladene Paket.</param>
    /// <param name="signature">Die losgelöste Signatur (rohe Bytes).</param>
    /// <param name="publicKeySpkiBase64">Der öffentliche Schlüssel.</param>
    /// <returns><see langword="true"/>, nur wenn die Signatur gültig ist.</returns>
    public static bool IsAuthentic(string packagePath, byte[] signature, string publicKeySpkiBase64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeySpkiBase64);

        if (signature is not { Length: > 0 } || !File.Exists(packagePath))
        {
            return false;
        }

        try
        {
            using ECDsa ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeySpkiBase64), out _);

            using FileStream package = File.OpenRead(packagePath);
            return ecdsa.VerifyData(package, signature, HashAlgorithmName.SHA256);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or IOException)
        {
            // Beschädigte Signatur, fremdes Format, unlesbare Datei: alles dasselbe
            // Ergebnis – das Paket wird nicht angefasst.
            return false;
        }
    }
}
