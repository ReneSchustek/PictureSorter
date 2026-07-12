using System;
using System.Runtime.InteropServices;

namespace PictureSorter.App.Services;

/// <summary>
/// Prüft die Authenticode-Signatur einer Programmdatei über die Windows-eigene
/// Vertrauensprüfung (<c>WinVerifyTrust</c>).
///
/// Warum das nötig ist: Der Updater wird aus dem Netz geladen und anschließend
/// ausgeführt. Ohne Prüfung würde jede Datei gestartet, die unter der erwarteten
/// Adresse liegt – ein falsch konfiguriertes oder übernommenes Repository würde
/// damit zur Codeausführung führen. Die Signaturprüfung stellt sicher, dass die
/// Datei von einem vertrauenswürdigen Herausgeber stammt und unterwegs nicht
/// verändert wurde.
/// </summary>
internal static partial class AuthenticodeVerifier
{
    private const uint StateActionVerify = 1;
    private const uint StateActionClose = 2;
    private const uint UiNone = 2;
    private const uint RevokeNone = 0;
    private const uint ChoiceFile = 1;
    private const uint ProvFlagsNone = 0;
    private const int TrustOk = 0;

    // WINTRUST_ACTION_GENERIC_VERIFY_V2 – die Standard-Aktion für die Prüfung einer
    // Authenticode-Signatur.
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    /// <summary>
    /// Prüft, ob die Datei gültig signiert ist und der Signatur vertraut wird.
    /// </summary>
    /// <param name="filePath">Absoluter Pfad der zu prüfenden Datei.</param>
    /// <returns>
    /// <see langword="true"/>, wenn Windows der Signatur vertraut; sonst
    /// <see langword="false"/> (nicht signiert, beschädigt oder nicht vertrauenswürdig).
    /// </returns>
    public static bool IsTrusted(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        IntPtr fileInfoPtr = IntPtr.Zero;
        IntPtr trustDataPtr = IntPtr.Zero;

        try
        {
            WinTrustFileInfo fileInfo = new()
            {
                CbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                PcwszFilePath = Marshal.StringToCoTaskMemUni(filePath),
                HFile = IntPtr.Zero,
                PgKnownSubject = IntPtr.Zero,
            };

            fileInfoPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPtr, fDeleteOld: false);

            WinTrustData trustData = new()
            {
                CbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                PPolicyCallbackData = IntPtr.Zero,
                PSIPClientData = IntPtr.Zero,
                DwUIChoice = UiNone,
                FdwRevocationChecks = RevokeNone,
                DwUnionChoice = ChoiceFile,
                PFile = fileInfoPtr,
                DwStateAction = StateActionVerify,
                HWVTStateData = IntPtr.Zero,
                PwszURLReference = IntPtr.Zero,
                DwProvFlags = ProvFlagsNone,
                DwUIContext = 0,
                PSignatureSettings = IntPtr.Zero,
            };

            trustDataPtr = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(trustData, trustDataPtr, fDeleteOld: false);

            Guid action = GenericVerifyV2;
            int result = WinVerifyTrust(IntPtr.Zero, ref action, trustDataPtr);

            // Zustandsdaten wieder freigeben, sonst leckt die Prüfung Speicher.
            WinTrustData toClose = Marshal.PtrToStructure<WinTrustData>(trustDataPtr);
            toClose.DwStateAction = StateActionClose;
            Marshal.StructureToPtr(toClose, trustDataPtr, fDeleteOld: true);
            _ = WinVerifyTrust(IntPtr.Zero, ref action, trustDataPtr);

            Marshal.FreeCoTaskMem(fileInfo.PcwszFilePath);
            return result == TrustOk;
        }
        finally
        {
            if (fileInfoPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(fileInfoPtr);
            }

            if (trustDataPtr != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(trustDataPtr);
            }
        }
    }

    // Der Suchpfad ist auf System32 festgelegt, damit die Bibliothek nicht durch eine
    // untergeschobene Datei im Programmverzeichnis ersetzt werden kann (DLL-Hijacking).
    [LibraryImport("wintrust.dll", EntryPoint = "WinVerifyTrust")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int WinVerifyTrust(IntPtr hwnd, ref Guid actionId, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint CbStruct;
        public IntPtr PcwszFilePath;
        public IntPtr HFile;
        public IntPtr PgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint CbStruct;
        public IntPtr PPolicyCallbackData;
        public IntPtr PSIPClientData;
        public uint DwUIChoice;
        public uint FdwRevocationChecks;
        public uint DwUnionChoice;
        public IntPtr PFile;
        public uint DwStateAction;
        public IntPtr HWVTStateData;
        public IntPtr PwszURLReference;
        public uint DwProvFlags;
        public uint DwUIContext;
        public IntPtr PSignatureSettings;
    }
}
