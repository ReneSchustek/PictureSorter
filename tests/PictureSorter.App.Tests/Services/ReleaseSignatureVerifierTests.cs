using System.Security.Cryptography;
using PictureSorter.App.Services;

namespace PictureSorter.App.Tests.Services;

/// <summary>
/// Tests des Vertrauensankers der Update-Kette. Er entscheidet, ob ein
/// heruntergeladenes Paket ausgeführt wird – die schärfste Frage, die die Anwendung
/// sich stellt. Zugelassen sind nur die einkompilierten Unterzeichner: Selbst wer den
/// Release-Kanal übernimmt, bekommt hier ohne deren privaten Schlüssel kein Paket durch.
/// Mehrere Schlüssel sind erlaubt, weil ein Wechsel sonst jede ausgelieferte Fassung
/// dauerhaft von Updates abschneiden würde.
/// </summary>
public sealed class ReleaseSignatureVerifierTests : IDisposable
{
    private readonly string _root;
    private readonly string _package;

    public ReleaseSignatureVerifierTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "PictureSorterTests", Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(_root);
        _package = Path.Combine(_root, "paket.zip");
        File.WriteAllText(_package, "die neue Fassung der Anwendung");
    }

    [Fact]
    public void IsAuthentic_WithSignatureFromTheKnownSigner_AcceptsThePackage()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string publicKey = Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo());
        byte[] signature = Sign(signer, _package);

        Assert.True(ReleaseSignatureVerifier.IsAuthentic(_package, signature, publicKey));
    }

    [Fact]
    public void IsAuthentic_WithSignatureFromAnotherKey_RejectsThePackage()
    {
        // Der Ernstfall: Jemand hat den Release-Kanal übernommen und ein eigenes Paket
        // samt eigener, in sich gültiger Signatur hinterlegt. Ohne unseren privaten
        // Schlüssel nützt ihm das nichts.
        using ECDsa stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa ours = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] signature = Sign(stranger, _package);

        Assert.False(ReleaseSignatureVerifier.IsAuthentic(
            _package,
            signature,
            Convert.ToBase64String(ours.ExportSubjectPublicKeyInfo())));
    }

    [Fact]
    public void IsAuthentic_WhenThePackageWasChangedAfterSigning_RejectsIt()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string publicKey = Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo());
        byte[] signature = Sign(signer, _package);

        File.WriteAllText(_package, "ein untergeschobenes Paket");

        Assert.False(ReleaseSignatureVerifier.IsAuthentic(_package, signature, publicKey));
    }

    [Fact]
    public void IsAuthentic_WithoutSignature_RejectsThePackage()
    {
        // Fail-closed: Kein Beleg, kein Einspielen. Eine fehlende Signatur darf nie
        // als „dann eben ungeprüft" durchgehen.
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.False(ReleaseSignatureVerifier.IsAuthentic(
            _package,
            [],
            Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo())));
    }

    [Fact]
    public void IsAuthentic_WithGarbageSignature_RejectsWithoutThrowing()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.False(ReleaseSignatureVerifier.IsAuthentic(
            _package,
            [1, 2, 3, 4],
            Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo())));
    }

    [Fact]
    public void IsAuthentic_ForMissingPackage_RejectsWithoutThrowing()
    {
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.False(ReleaseSignatureVerifier.IsAuthentic(
            Path.Combine(_root, "gibt-es-nicht.zip"),
            [1, 2, 3, 4],
            Convert.ToBase64String(signer.ExportSubjectPublicKeyInfo())));
    }

    [Fact]
    public void IsAuthentic_WithSignatureFromTheSuccessorKey_AcceptsThePackage()
    {
        // Der Schlüsselwechsel: Solange beide Schlüssel zugelassen sind, nimmt die
        // Anwendung Pakete des alten wie des neuen Unterzeichners an. Ohne diesen
        // Übergang wäre jede ausgelieferte Fassung nach einem Wechsel update-unfähig.
        using ECDsa previous = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa successor = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string[] accepted =
        [
            Convert.ToBase64String(previous.ExportSubjectPublicKeyInfo()),
            Convert.ToBase64String(successor.ExportSubjectPublicKeyInfo()),
        ];

        Assert.True(ReleaseSignatureVerifier.IsAuthentic(_package, Sign(successor, _package), accepted));
        Assert.True(ReleaseSignatureVerifier.IsAuthentic(_package, Sign(previous, _package), accepted));
    }

    [Fact]
    public void IsAuthentic_WithSignatureFromAKeyThatWasRetired_RejectsThePackage()
    {
        // Nach abgeschlossenem Wechsel steht der alte Schlüssel nicht mehr in der Liste.
        // Ab da darf seine Signatur nicht mehr durchgehen.
        using ECDsa retired = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDsa current = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string[] accepted = [Convert.ToBase64String(current.ExportSubjectPublicKeyInfo())];

        Assert.False(ReleaseSignatureVerifier.IsAuthentic(_package, Sign(retired, _package), accepted));
    }

    [Fact]
    public void IsAuthentic_WithoutAnyAcceptedKey_RejectsThePackage()
    {
        // Fail-closed auch hier: Eine leere Liste heißt „niemand ist zugelassen",
        // nicht „jeder".
        using ECDsa signer = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.False(ReleaseSignatureVerifier.IsAuthentic(_package, Sign(signer, _package), []));
    }

    [Fact]
    public void AcceptedPublicKeys_ContainsTheCompiledInKey()
    {
        Assert.Contains(ReleaseSignatureVerifier.PublicKeySpkiBase64, ReleaseSignatureVerifier.AcceptedPublicKeys);
    }

    [Fact]
    public void PublicKey_IsAUsableEcdsaKey()
    {
        // Der einkompilierte Schlüssel muss sich laden lassen – ein Tippfehler darin
        // legte die gesamte Update-Kette lahm, und zwar erst beim Nutzer.
        using ECDsa ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(
            Convert.FromBase64String(ReleaseSignatureVerifier.PublicKeySpkiBase64),
            out _);

        Assert.Equal(256, ecdsa.KeySize);
    }

    private static byte[] Sign(ECDsa key, string path)
    {
        using FileStream file = File.OpenRead(path);
        return key.SignData(file, HashAlgorithmName.SHA256);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
