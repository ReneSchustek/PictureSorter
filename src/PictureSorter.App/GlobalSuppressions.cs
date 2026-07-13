using System.Diagnostics.CodeAnalysis;

// Der Auto-Initialisierer des Windows App SDK (UndockedRegFreeWinRT-AutoInitializer.cs)
// wird vom NuGet-Paket in die Kompilierung eingeschleust, sobald die App die Runtime
// selbst mitbringt (WindowsAppSDKSelfContained). Er deklariert seinen P/Invoke ohne
// [DefaultDllImportSearchPaths] und verletzt damit CA5392 – in einem Projekt, das alle
// Analyzer-Regeln als Fehler behandelt, bricht daran der Build.
//
// Die Datei stammt aus dem SDK-Paket und darf nicht geändert werden ("DO NOT MODIFY.
// Changes ... will be lost on updates"). Die Regel wird deshalb ausschließlich für diese
// eine Methode ausgesetzt, nicht projektweit: Für eigene P/Invokes muss CA5392 ein
// Fehler bleiben.
//
// Vertretbar ist das, weil die geladene Microsoft.WindowsAppRuntime.dll im
// Anwendungsverzeichnis der self-contained App liegt und .NET native Bibliotheken
// zuerst ebendort sucht.
[assembly: SuppressMessage(
    "Security",
    "CA5392:DefaultDllImportSearchPaths-Attribut für P/Invokes verwenden",
    Justification = "Unveränderbarer Code aus dem Windows-App-SDK-Paket; die Bibliothek liegt anwendungslokal.",
    Scope = "member",
    Target = "~M:Microsoft.Windows.Foundation.UndockedRegFreeWinRTCS.NativeMethods.WindowsAppRuntime_EnsureIsLoaded~System.Int32")]
