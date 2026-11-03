using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PictureSorter.Core.Interfaces;

namespace PictureSorter.Infrastructure.Caching;

/// <summary>
/// Bildet den Inhaltsschlüssel aus der Dateigröße und den ersten und letzten 64 KB.
/// </summary>
/// <remarks>
/// Am Anfang stehen die Metadaten und der Bildbeginn, am Ende die letzten Bilddaten. Eine
/// Bearbeitung ändert damit den Schlüssel. Bei 4.000 Fotos fallen so rund 500 MB Lesearbeit
/// an statt des gesamten Bestands. Die Änderungszeit gehört nicht dazu, weil ein Cloud-Abgleich
/// sie ändert, ohne dass sich das Bild ändert.
/// </remarks>
public sealed class FileFingerprinter : IFileFingerprinter
{
    // Ein EXIF-Block fasst in einem JPEG höchstens 64 KB und steckt damit vollständig im Anfang.
    private const int SampleLength = 64 * 1024;

    /// <inheritdoc />
    public async Task<string?> ComputeAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        byte[] buffer = ArrayPool<byte>.Shared.Rent(SampleLength);
        try
        {
            FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1,
                FileOptions.Asynchronous);
            await using (stream.ConfigureAwait(false))
            {
                using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long length = stream.Length;
                hash.AppendData(Encoding.ASCII.GetBytes(length.ToString(CultureInfo.InvariantCulture)));

                await AppendSampleAsync(hash, stream, buffer, cancellationToken).ConfigureAwait(false);
                if (length > SampleLength)
                {
                    // Überlappt das Ende den Anfang, wird ein Stück doppelt gehasht. Das ist
                    // unschädlich, weil es für jede Kopie gleich geschieht.
                    _ = stream.Seek(-SampleLength, SeekOrigin.End);
                    await AppendSampleAsync(hash, stream, buffer, cancellationToken).ConfigureAwait(false);
                }

                return Convert.ToHexString(hash.GetHashAndReset());
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task AppendSampleAsync(
        IncrementalHash hash,
        FileStream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < SampleLength)
        {
            int read = await stream
                .ReadAsync(buffer.AsMemory(total, SampleLength - total), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        hash.AppendData(buffer, 0, total);
    }
}
