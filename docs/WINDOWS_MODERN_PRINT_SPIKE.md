# Moderner Windows-Druck: isolierter Spike

**Historisches Dokument:** Die experimentelle Implementierung einschließlich
Testbutton, Abhängigkeiten, Tests und Diagnoseskripten wurde zurückgenommen.
Die folgenden Beschreibungen und Befehle dokumentieren den damaligen Versuch
und sind mit dem aktuellen Checkout nicht ausführbar.

Der Spike untersucht die native Seitenvorschau im Windows-11-Druckdialog für
vorhandene SpanDraft-Report-Bitmaps. Er ist standardmäßig ausgeschaltet.
Der normale Druckpfad über `WindowsReportPrinter` bleibt unverändert.

## Bauen und prüfen

Auf Windows x64 im Repository-Verzeichnis:

```powershell
dotnet build SpanDraft.sln -c Release -p:EnableModernPrintSpike=true
dotnet test --solution SpanDraft.sln -c Release --no-build --no-restore -p:EnableModernPrintSpike=true
```

Die Anwendung liegt anschließend unter
`src/SpanDraft.Desktop/bin/Release/net10.0-windows10.0.19041.0/win-x64/SpanDraft.Desktop.exe`.
Der temporäre Button heißt „Drucken (Test)“ und übergibt sämtliche Reportseiten
in ihrer Reihenfolge. Vorschau und Testdruck verwenden die vorhandene
`RenderPage`-Ausgabe mit 300 dpi. Die Verkleinerung für die Vorschau verwendet
hochwertige kubische Interpolation.

Ohne Schalter bleiben Desktop und Produkttests bei `net10.0`; der Button ist
ausgeblendet. Der Windows-Spike verwendet einen separaten NuGet-Restorepfad,
damit zwischen beiden Buildvarianten ohne gegenseitiges Überschreiben der
Assets-Dateien gewechselt werden kann. macOS/Linux erhalten keine zusätzlichen
Laufzeitpakete. Der Spike selbst wird nur auf Windows gebaut.

## Separater PDF-Eingang: an der API-Grenze abgebrochen

Das vorhandene `ReportDocument.WritePdf(Stream)` kann auf dem hier untersuchten,
öffentlich unterstützten `PrintManager`-Weg nicht direkt als Dokumentquelle
übergeben werden. Eine vorhandene Microsoft-PDF→`IPrintDocumentSource`-Brücke
ist in den geprüften Windows-/Windows-App-SDK-APIs nicht verfügbar.

Die konkrete Grenze ist
[`PrintTaskSourceRequestedArgs.SetSource(IPrintDocumentSource)`](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.printing.printtasksourcerequestedargs.setsource):
Der Aufruf akzeptiert weder einen PDF-Stream noch eine PDF-Datei oder ein
`PdfDocument`. [`Windows.Data.Pdf.PdfDocument`](https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf.pdfdocument)
lädt Dateien und Streams und stellt Seiten bereit; es implementiert die
verlangte Druckschnittstelle nicht und besitzt keine öffentliche Methode,
die eine entsprechende Druckquelle erzeugt.

Der ausschließlich im Windows-Spike enthaltene Test
`ExportedPdfLoadsInWindowsButDoesNotExposePrintDocumentSource` erzeugt über
das bestehende `WritePdf` einen zweiseitigen Report im Speicher. Er prüft den
PDF-Header und das Fehlen eingebetteter Bildobjekte für diesen Report, lädt
denselben Stream mit `PdfDocument.LoadFromStreamAsync` und bestätigt beide Seiten.
Die native `QueryInterface`-Kontrollabfrage nach `IPdfDocument` gelingt;
die Abfrage nach `IPrintDocumentSource` liefert `E_NOINTERFACE` (`0x80004002`).
Dabei wird keine Seite gerendert und kein Druckdialog geöffnet. Die
Schnittstellenabfrage belegt die fehlende direkte Übergabe dieses Windows-Objekts;
die Adapterbewertung stützt sich zusätzlich auf die öffentlichen API-Verträge.

Nach dem oben beschriebenen Spike-Build lässt sich der Test separat ausführen:

```powershell
& .\tests\SpanDraft.Tests\bin\Release\net10.0-windows10.0.19041.0\win-x64\SpanDraft.Tests.exe -method '*ExportedPdfLoadsInWindowsButDoesNotExposePrintDocumentSource'
```

Auch die übrigen geprüften APIs schließen diese Lücke nicht:

- Das dokumentierte [`Microsoft.UI.Xaml.Printing.PrintDocument`](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/print-from-your-app)
  verlangt von der Anwendung vorbereitete XAML-Seiten. Das vorhandene
  [`CanvasPrintDocument`](https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_Printing_CanvasPrintDocument.htm)
  verlangt gezeichnete Seiten. Beide bieten keinen PDF-Datei-/Stream-Eingang.
- [`IPdfRendererNative`](https://learn.microsoft.com/en-us/windows/win32/api/windows.data.pdf.interop/nn-windows-data-pdf-interop-ipdfrenderernative)
  ist ein Seitenrenderer: `RenderPageToDeviceContext` gibt laut Vertrag ein
  Bitmapbild aus, `RenderPageToSurface` DirectX-Bilddaten. Auch
  [`PdfPage.RenderToStreamAsync`](https://learn.microsoft.com/en-us/uwp/api/windows.data.pdf.pdfpage.rendertostreamasync)
  erzeugt ein Seitenbild. Diese APIs werden im Test nicht aufgerufen und
  wären kein vektorerhaltender PDF-Durchgriff bis zum Drucksystem.
- [`PrintWorkflowPdlConverter`](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.printing.workflow.printworkflowpdlconverter)
  konvertiert Druckdaten innerhalb eines Druckworkflows. Er erzeugt keine
  `IPrintDocumentSource` für den anwendungsseitigen `SetSource`-Aufruf.
  Die [`XPS-Paketdruckschnittstelle`](https://learn.microsoft.com/en-us/windows/win32/api/xpsobjectmodel_1/nn-xpsobjectmodel_1-ixpsdocumentpackagetarget)
  nimmt XPS-Inhalt entgegen und ist ebenfalls kein PDF-Adapter für diesen Dialog.

Der PDF-Versuch endet deshalb hier. Eine zusätzliche Druckquelle müsste selbst
implementiert und mit Vorschau-/Druckinhalt versorgt werden; das überschreitet
den erlaubten Umfang. Es gibt keinen `Drucken (PDF-Test)`-Button, keine neue
Abhängigkeit und keine PDF→Bitmap-, Browser-, Direct2D- oder XPS-Implementierung.
Das erzeugte PDF bleibt beim Laden unverändert; eine vektorerhaltende Übergabe
an das Drucksystem wurde damit nicht erreicht. Daraus folgt keine allgemeine
Aussage, dass Windows oder jeder PDF-Druckweg grundsätzlich rasterisieren muss.
Der vorhandene Win2D-Spike, klassische Druckpfad, Report-Renderer und PDF-Export
bleiben von diesem separaten Versuch unberührt.

## API-Grenze und gewählter Adapter

[`PrintManagerInterop`](https://learn.microsoft.com/en-us/windows/apps/develop/devices-sensors/print-from-your-app)
verknüpft `PrintManager` mit dem vorhandenen Avalonia-HWND. Der Dialogaufruf allein
liefert noch keinen Seiteninhalt: `PrintTaskSourceRequestedArgs.SetSource` erwartet
eine `IPrintDocumentSource`, keine Bitmap. Eine leere Implementierung dieser
Schnittstelle wäre keine funktionsfähige Druckquelle.

Microsofts [`CanvasPrintDocument`](https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_Printing_CanvasPrintDocument.htm)
aus Win2D übernimmt diesen nativen Vertrag einschließlich Vorschau und Druck.
SpanDraft überträgt die BGRA-Pixel der vorhandenen `RenderPage`-Ausgabe und lässt
Win2D die jeweilige Bitmap einpassen. Es entsteht kein zweites Reportlayout und
keine selbst implementierte Direct2D-/XPS-Druckquelle.

Die dafür erforderliche Windows-App-SDK-DispatcherQueue läuft auf einem eigenen
Thread. Sie benötigt kein XAML-Hosting und ersetzt nicht Avalonias Dispatcher.
Dokumentquelle und native Bitmaps bleiben bis zum Abschluss des Druckauftrags erhalten;
ihre Freigabe wird mit den Vorschau-Callbacks auf diesem Thread serialisiert.
Die CPU-Bitmaps werden nach der Übernahme durch Win2D freigegeben.

Win2D zieht Windows-App-SDK/WinUI transitiv nach. Diese Abhängigkeiten sind auf
den Test-Build begrenzt; die Windows-App-SDK-Laufzeit liegt lokal neben der EXE.
Eine gesonderte Runtime-Installation und ein WinUI-Anwendungsfenster sind für
diesen Ansatz nicht vorgesehen.

## Nachweis und Abbruchgrenze

Der dialogfreie native Smoke-Test prüft Aktivierung, bytegenaue Übernahme aller
gerenderten Reportseiten bei Druckauflösung und wiederholtes Erzeugen/Freigeben. Layout- und
Interaktionstests prüfen die zusätzliche Toolbar-Aktion. Diese Tests beweisen
noch nicht die sichtbare native Windows-Vorschau.

Die native Vorschau und die Darstellung beider Seiten wurden im
Windows-11-Druckdialog bestätigt. Der A/B-Test bestätigt außerdem eine deutlich
schärfere Vorschau mit korrigierter Bildschirm-DPI. Der unveränderte
Win2D-Adapter ist bei hoher Bildschirmskalierung noch kein geeigneter
Produktionsweg. Die native DPI-Korrektur ist experimentell nachgewiesen;
der saubere Fix an der Bibliothek ist weiterhin nur vorbereitet.

## Isolierter A/B-Test: 96 gegen 216 Bildschirm-DPI

Für den Nachweis stehen zwei getrennte Diagnosekopien bereit. Beide verwenden
dieselben Anwendungsdateien, dieselbe Bitmapauflösung und dieselbe Skalierung.
Nur die native Win2D-DLL in der 216-dpi-Kopie ist geändert. Der Eingriff leitet
exakt die Fallback-Ladeanweisung in `DefaultPrintDocumentAdapter::GetLogicalDpi`
auf ein eigenes 216.0-Literal um; die gemeinsam verwendete 96.0-Konstante bleibt
erhalten. Das Präparationsskript prüft den vollständigen Originalhash und die
erwarteten Bytes der Win2D-1.4.0-x64-Datei und lehnt andere Binärdateien ab.
Die Originalpakete und bisherigen Builds werden nicht verändert. Die veränderte
DLL ist ausschließlich ein Wegwerfartefakt für diesen Vergleich und darf nicht
ausgeliefert werden; ihre ursprüngliche Signatur ist durch den Eingriff ungültig.

Reproduktion in einem frischen Artefaktordner, vom Repository-Verzeichnis aus:

```powershell
dotnet build SpanDraft.sln -c Release -p:EnableModernPrintSpike=true --artifacts-path artifacts/modern-print-ab/build
python scripts/prepare-modern-print-dpi-ab.py --source artifacts/modern-print-ab/build/bin/SpanDraft.Desktop/release_win-x64
dotnet run --file scripts/probe-modern-print-dpi.cs -- artifacts/modern-print-dpi-ab/96/dpi-test.json
dotnet run --file scripts/probe-modern-print-dpi.cs -- artifacts/modern-print-dpi-ab/216/dpi-test.json
```

Die letzten beiden Befehle müssen den nativen Rückgabewert 96 beziehungsweise
216 melden. Sie öffnen keinen Druckdialog. Das Präparationsskript überschreibt
keine vorhandenen Testordner; für eine Wiederholung einen frischen `--output`
unter `artifacts` wählen.

Den sichtbaren Vergleich nacheinander starten:

```powershell
& C:\dev\SpanDraft\artifacts\modern-print-dpi-ab\96\start.ps1
& C:\dev\SpanDraft\artifacts\modern-print-dpi-ab\216\start.ps1
```

Jeweils dasselbe Projekt und denselben Bericht über „Drucken (Test)“ öffnen.
Bildschirmskalierung, Dialoggröße, Zoom, Drucker und Papierformat gleich lassen.
Der Druckdialogtitel enthält `[96 dpi]` beziehungsweise `[216 dpi]`. Die
Startskripte prüfen Datei-Hashes, Prozesspfad und Startzeit nach dem Build.
`preview.jsonl` im jeweiligen Testordner protokolliert Seitenindex,
Quelldaten-DPI, native `DrawingSession.Dpi` und daraus berechnete Rastermaße.
Die Konstante 216 beschreibt hier die Bildschirm-DPI; die protokollierte
Seitenvorschau-DPI hängt zusätzlich von deren angeforderter Darstellungsgröße ab.
Bei gleicher Größe sollen Rastermaße und Vorschau-DPI um ungefähr Faktor 2,25
steigen, während die Quelldaten-DPI gleich bleibt.

### Messergebnis

Die aus den nativen Callbacks protokollierten Rastermaße betrugen bei der
geprüften Dialoggröße 377 × 533 Pixel in Variante A und 848 × 1199 Pixel in
Variante B. Die native Seitenvorschau-DPI stieg entsprechend von rund 45,58 auf
102,54; die Quelldaten blieben in beiden Fällen bei 300 dpi. Die korrigierte
Variante liefert damit etwa fünfmal so viele Vorschaupixel. Der manuelle
Vergleich bestätigt eine deutlich schärfere Darstellung.

Das bestätigt die zu klein angelegte native Vorschaufläche als Ursache im
getesteten Fall. Die konkreten Rastermaße sind Messwerte dieser Dialoggröße,
keine festen Produktwerte. Für den sauberen Fix ist die tatsächliche Fenster-DPI
zu verwenden; 216 darf nicht als allgemeine Konstante übernommen werden.

## Vorbereiteter Fix: Bildschirm-DPI in Win2Ds Vorschau

### Belegter Fehlerpfad und Einordnung

[`CanvasPrintDocument`](https://github.com/microsoft/Win2D/blob/winappsdk/main/winrt/lib/printing/CanvasPrintDocument.cpp)
speichert bei der Konstruktion `m_displayDpi`. `CalculateDpiForPreviewBitmap`
benutzt diesen Wert, um die vom Druckdialog angeforderte Vorschaugröße von DIPs
in Pixel umzurechnen. `MakePageImpl` erzeugt die native Vorschaufläche bereits
vor dem Aufruf unseres `Preview`-Handlers. Die Quellbitmap-DPI kommt in dieser
Größenberechnung nicht vor.

Der
[`Desktop-Adapter`](https://github.com/microsoft/Win2D/blob/winappsdk/main/winrt/lib/printing/CanvasPrintDocumentAdapter.cpp)
liest weiterhin `DisplayInformation.GetForCurrentView` und fällt beim Fehlschlag
auf 96 dpi zurück. Microsoft dokumentiert diese
[`ForCurrentView`-APIs als für Desktop-Apps nicht unterstützt](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-api-desktop-app-support).
Eine eigene DispatcherQueue stellt keinen unterstützten `ApplicationView` bereit.
Der Fehler ist damit kein spezielles Problem unseres Hintergrundthreads.

Bei beispielsweise 225 % Skalierung wären 216 Bildschirm-DPI erforderlich.
Der Fallback erzeugt nur `96 / 216` der nötigen Pixel pro Richtung, also knapp
20 % der Pixel insgesamt. Die anschließende Vergrößerung erklärt die unveränderte
Unschärfe trotz höher aufgelöster Quelldaten. Der native A/B-Messwert und der
sichtbare Schärfegewinn bestätigen diesen Fehlerpfad im getesteten Fall.

Die Header des eingesetzten Win2D-Pakets und die öffentliche
[`CanvasPrintDocument`-API](https://microsoft.github.io/Win2D/WinUI3/html/T_Microsoft_Graphics_Canvas_Printing_CanvasPrintDocument.htm)
enthalten keinen Parameter oder Setter für die Vorschau-DPI.
[`CanvasDrawingSession.Dpi`](https://microsoft.github.io/Win2D/WinUI3/html/P_Microsoft_Graphics_Canvas_CanvasDrawingSession_Dpi.htm)
ist nur lesbar und stammt von der bereits erzeugten Zielfläche.
[`CanvasPrintEventArgs.Dpi`](https://microsoft.github.io/Win2D/WinUI3/html/P_Microsoft_Graphics_Canvas_Printing_CanvasPrintEventArgs_Dpi.htm)
betrifft den späteren Druck und korrigiert die Vorschau nicht.

### Konkrete Änderung an der Abhängigkeit

Der gezielte Fix gehört in Win2D und benötigt ein korrigiertes Paket. Die
vorhandene native Dokumentquelle und ihr Renderer werden dabei weiterverwendet.

1. Win2D um eine neue Factory-Schnittstelle und einen Konstruktor-Overload
   `CanvasPrintDocument(CanvasDevice device, float previewDisplayDpi)` erweitern.
   Bestehende COM-Schnittstellen und Konstruktoren unverändert lassen. Der neue
   Parameter muss endlich und größer als null sein und wird direkt als
   `m_displayDpi` übernommen. Die vorhandene Vorschaugrößenberechnung bleibt erhalten.
2. SpanDraft liest beim Start jedes Druckdialogs die tatsächliche Fenster-DPI
   über [`GetDpiForWindow(ownerHwnd)`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getdpiforwindow)
   auf dem Avalonia-Thread. Ein Rückgabewert von null ist ein Fehler. Den gültigen
   Wert zur eigenen Druck-Queue weiterreichen und dort den neuen Konstruktor nutzen.
   Keine feste Skalierung und keine globale Änderung der DPI-Awareness verwenden.
3. Im optionalen Windows-Spike das korrigierte Win2D-Paket einbinden. Native DLL,
   WinRT-Metadaten und verwaltete Projektion müssen aus demselben Paketbuild stammen;
   kein isolierter DLL-Austausch. Die normale Paketauflösung bleibt unverändert.
4. Beim erneuten Öffnen des Dialogs die DPI neu lesen, damit ein zwischenzeitlicher
   Monitorwechsel berücksichtigt wird. Ein Wechsel während eines geöffneten
   Druckdialogs ist separat zu prüfen und durch einen einmaligen Konstruktorwert
   noch nicht automatisch gelöst.

`GetDpiForWindow` allein im aktuellen C#-Adapter aufzurufen reicht nicht: Die
ermittelte DPI kann mit der ausgelieferten Win2D-API noch nicht an deren
Vorschaugrößenberechnung übergeben werden. Auch ein separates
[`DisplayInformationInterop.GetForWindow`](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.display.interop/nf-windows-graphics-display-interop-idisplayinformationstaticsinterop-getforwindow)
ändert Win2Ds internen `GetForCurrentView`-Aufruf nicht.

### Regression und Freigabe

- Im Win2D-Test mit fest vorgegebenen Vorschau-DIPs die nativen Zielpixel bei
  96, 144 und 216 Bildschirm-DPI prüfen. Ein angefordertes Seitenformat von
  600 × 800 DIPs muss bei passendem Seitenverhältnis zu 600 × 800,
  900 × 1200 beziehungsweise 1350 × 1800 Pixeln führen.
- Ein Wechsel der Quellbitmap-DPI darf diese Zielpixelmaße nicht beeinflussen.
  Bestehende Konstruktoren und der eigentliche Druck müssen kompatibel bleiben.
- Im realen `Preview`-Callback Ziel-DPI und aus Seitengröße und Ziel-DPI berechnete
  Rastermaße erfassen. Zusammen mit der verwendeten Fenster-DPI und einem frischen
  Prozess lässt sich dadurch ausschließen, dass erneut nur die Quellbitmap geändert wurde.
- Unter Windows bei 100 %, 150 % und 225 % beide Seiten, vergrößerte Vorschau,
  Drucker-/Papierformatwechsel, Abbrechen und erneutes Öffnen prüfen. Danach
  Standard-/Spike-Tests und die unveränderten plattformübergreifenden Builds prüfen.

Damit liegt eine Änderungsspezifikation für Win2D vor, kein funktionierender
C#-Workaround. Der Build und die Pflege einer eigenen nativen Win2D-Version
wären eine zusätzliche Abhängigkeitspflege über den bisherigen Spike hinaus.
Ohne ein korrigiertes Paket wird dieser Pfad nicht als Produktionslösung freigegeben.

Falls die echte Vorschau trotz dieses Adapters nur mit WinUI-XAML-Hosting,
XAML Islands oder einer eigenen nativen Druckquelle funktioniert, wird der
Spike verworfen. Dann sind die experimentellen Pakete, der Adapter und der
Testbutton zu entfernen und die konkret beobachtete API-Grenze hier festzuhalten.
Ein Dialog ohne echte Reportvorschau zählt nicht als erfolgreicher Nachweis.

## Manuelle Prüfung auf Windows 11

1. Den Test-Build erstellen, die resultierende EXE per absolutem Pfad frisch
   starten und Prozesspfad sowie Startzeit nach dem Build verifizieren.
2. Einen mehrseitigen Report öffnen und „Drucken (Test)“ wählen. Seitenzahl,
   Schärfe und Navigation auf Seite 2 prüfen, auch nach Drucker-/Papierformatwechsel.
3. Abbrechen und erneut öffnen; optional alle Seiten testweise drucken.
4. Bestehendes „Drucken“, PDF-Ausgabe und SpanDraft-Vorschau gegenprüfen.
