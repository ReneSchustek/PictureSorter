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
    /// </summary>
    public const string PublicKeySpkiBase64 =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEaKw6jIhmwEOcRW6t55wOxcReJY/dLiAXKzECGciJ1vKjGGV4TXQjQQShl7HuSg1XiDOJXPgkD00+rNmTnZ+sWw==";

    /// <summary>
    /// Alle Schlüssel, deren Signatur diese Fassung annimmt.
    ///
    /// Mehrere sind zugelassen, damit ein Schlüsselwechsel überhaupt möglich ist: Eine
    /// ausgelieferte Anwendung kennt nur die Schlüssel, die zum Zeitpunkt ihrer
    /// Auslieferung hier standen. Wird einfach ausgetauscht, nimmt jede bereits
    /// installierte Fassung kein Update mehr an – und kann sich damit auch nicht mehr
    /// auf die Fassung mit dem neuen Schlüssel bringen. Der Wechsel läuft deshalb in
    /// drei Schritten: den Nachfolger hier aufnehmen und ausliefern, danach mit ihm
    /// signieren, und erst wenn die Installationen nachgezogen sind, den alten Schlüssel
    /// entfernen. Der Ablauf steht in BETRIEB.md.
    /// </summary>
    public static readonly IReadOnlyList<string> AcceptedPublicKeys = [PublicKeySpkiBase64];

    /// <summary>
    /// Prüft, ob die Signatur zum Paket und zu einem der zugelassenen Unterzeichner passt.
    /// </summary>
    /// <param name="packagePath">Das heruntergeladene Paket.</param>
    /// <param name="signature">Die losgelöste Signatur (rohe Bytes).</param>
    /// <returns><see langword="true"/>, nur wenn die Signatur gültig ist.</returns>
    public static bool IsAuthentic(string packagePath, byte[] signature) =>
        IsAuthentic(packagePath, signature, AcceptedPublicKeys);

    /// <summary>
    /// Prüft gegen einen ausdrücklich angegebenen Schlüssel (für Tests).
    /// </summary>
    /// <param name="packagePath">Das heruntergeladene Paket.</param>
    /// <param name="signature">Die losgelöste Signatur (rohe Bytes).</param>
    /// <param name="publicKeySpkiBase64">Der öffentliche Schlüssel.</param>
    /// <returns><see langword="true"/>, nur wenn die Signatur gültig ist.</returns>
    public static bool IsAuthentic(string packagePath, byte[] signature, string publicKeySpkiBase64) =>
        IsAuthentic(packagePath, signature, [publicKeySpkiBase64]);

    /// <summary>
    /// Prüft gegen eine ausdrücklich angegebene Menge zugelassener Schlüssel.
    /// </summary>
    /// <param name="packagePath">Das heruntergeladene Paket.</param>
    /// <param name="signature">Die losgelöste Signatur (rohe Bytes).</param>
    /// <param name="acceptedPublicKeys">Die zugelassenen öffentlichen Schlüssel.</param>
    /// <returns><see langword="true"/>, nur wenn die Signatur zu einem davon passt.</returns>
    public static bool IsAuthentic(string packagePath, byte[] signature, IReadOnlyList<string> acceptedPublicKeys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentNullException.ThrowIfNull(acceptedPublicKeys);

        if (signature is not { Length: > 0 } || acceptedPublicKeys.Count == 0 || !File.Exists(packagePath))
        {
            return false;
        }

        byte[] hash;
        try
        {
            using FileStream package = File.OpenRead(packagePath);
            hash = SHA256.HashData(package);
        }
        catch (IOException)
        {
            return false;
        }

        // Der Hash wird einmal gebildet und gegen jeden zugelassenen Schlüssel geprüft:
        // Das Paket ist mehrere hundert Megabyte groß, ein erneutes Durchlesen je
        // Schlüssel wäre reine Wartezeit.
        foreach (string publicKeySpkiBase64 in acceptedPublicKeys)
        {
            if (MatchesKey(hash, signature, publicKeySpkiBase64))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesKey(byte[] hash, byte[] signature, string publicKeySpkiBase64)
    {
        if (string.IsNullOrWhiteSpace(publicKeySpkiBase64))
        {
            return false;
        }

        try
        {
            using ECDsa ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKeySpkiBase64), out _);
            return ecdsa.VerifyHash(hash, signature);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Beschädigte Signatur oder fremdes Schlüsselformat: dasselbe Ergebnis wie
            // eine falsche Signatur – das Paket wird nicht angefasst.
            return false;
        }
    }
}
