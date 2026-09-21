# TenzoraX — Projektwissen

Werkzeug, das Gamecontroller-Eingaben in Tastatur- und Mausaktionen übersetzt (Spiele, Emulatoren, eigene Tastenkürzel).
Windows-Desktop-Anwendung, Hobby-Projekt mit KI-Unterstützung, MIT-Lizenz. Bedienoberfläche und Logs: **Deutsch**; README und Code-Bezeichner: Englisch.

## Stack & Struktur

- .NET 8 (`net8.0-windows10.0.19041.0`), WPF **und** WinForms, C#, nullable aktiviert.
- `TenzoraX.sln` · Anwendung: `src/TenzoraX` · Tests: `tests/TenzoraX.Tests` (xUnit).
- Einzige Paketabhängigkeit: `Nefarius.ViGEm.Client` (virtueller Xbox-360-Controller), Version in `src/TenzoraX/TenzoraX.csproj`.
- HidHide (Verstecken physischer Geräte) und ViGEmBus werden **nicht** als Paket eingebunden, sondern über die installierte CLI bzw. den Treiber angesprochen.
- `docs/`, `publish/`, `Profiles/`, `*.exe`, `*.log` sind per `.gitignore` ausgenommen.

## Bauen, testen, veröffentlichen

```bash
dotnet build TenzoraX.sln -v m --nologo
dotnet test  TenzoraX.sln --nologo -v q        # Stand: 243 Tests, alle grün
dotnet publish src/TenzoraX/TenzoraX.csproj -c Release -o <zielordner>
```

- Der Publish erzeugt eine **einzelne, self-contained EXE** (~189 MB); `PublishSingleFile`, `SelfContained`, `win-x64` und `IncludeNativeLibrariesForSelfExtract` sind in der csproj gesetzt.
- Ausgabeordner-Konvention des Projekts: `publish/vX.Y.Z/` (nicht versioniert).
- **Falle:** Bauen/Publizieren scheitert, solange die Anwendung läuft (die EXE ist gesperrt). Vorher die Instanz beenden.
- **Falle:** `taskkill /IM TenzoraX.exe` trifft die Testversion nicht — der laufende Prozess heißt `TenzoraX-Test.exe` (siehe Autostart). Immer den exakten Image-Namen prüfen.

## Version

- Die Version steht an **zwei** Stellen und muss dort gleich sein: `src/TenzoraX/AppVersion.cs` und `TenzoraX.csproj` (`Version`, `FileVersion`, `AssemblyVersion` = Version + `.0`).
- `tests/TenzoraX.Tests/VersionTests.cs` erzwingt diese Konsistenz — ein Versionssprung ohne beide Dateien lässt die Suite rot werden.
- Aktueller Stand: **1.7.1**.
- Die sichtbare Versionsnummer im Fenstertitel kommt aus `AppVersion.Current`.

## Laufzeitorte

- Datenverzeichnis: `%USERPROFILE%\Documents\TenzoraX\` — `config.json`, `Logs\app.log`, `diagnostics.txt`.
- Diagnose ohne Oberfläche: `TenzoraX.exe --diagnose` schreibt `diagnostics.txt` (API-Befunde je Schnittstelle, dazu seit 1.4.2 den Block Eingabe-Endpunkte). Läuft **vor** der Einzelinstanz-Sperre, braucht keine Adminrechte.
- Wichtige Konfigurationsschlüssel: `UseVigemXbox` (Spiegelung), `StartMinimized`, `RunAsAdministrator`.
- Nach jedem Experiment den Ausgangszustand der `config.json` wiederherstellen — die Datei gehört dem Nutzer.

## Autostart

Die Aufgabenplanung **`TenzoraX`** (Auslöser: Anmeldung, höchste Rechte) startet:

```
"C:\Users\Arimtak\Desktop\TenzoraX-Test.exe" --minimized
```

- Diese Desktop-Datei ist die **lebende** Version des Nutzers: nicht umbenennen, nicht löschen, nur ersetzen (vorher laufende Instanz beenden).
- Ein neuer Build wird dort abgelegt; die Versionsnummer im Fenstertitel verrät, was läuft.
- Beim Beenden gibt die Anwendung ihre Geräte-Versteck-Einträge selbst frei (`HidHide: -N freigegeben, Cloaking=False`), damit nie ein unsichtbares Pad zurückbleibt; solange sie nicht läuft, ist nichts versteckt.

## Sprache und Texte

- Kommentare, Log-Zeilen und Diagnoseberichte auf Deutsch, mit Präfixen wie `HidHide:`, `ViGEm:`, `WinMM:`, `DI-`, `RI-`.
- Jeder **sichtbare** Text existiert zweisprachig in `src/TenzoraX/LanguageManager.cs`. Ein neuer Text ohne beide Sprachen fällt stillschweigend auf den anderen zurück.

## Wo was liegt (Orientierung)

- `src/TenzoraX/MainWindow.xaml.cs` — Oberfläche und Verdrahtung der Schalter (Spiegelung, Duplikat-Modus, Status, Rückfragen).
- `src/TenzoraX/ControllerManager.cs` — Eingabe-Pipeline, Auswahl des aktiven Geräts, Diagnosebericht.
- `src/TenzoraX/HidHideAutomation.cs` — Aufrufe der HidHide-CLI; `ApplyDeviceState(hide, unhide, cloakOn)` bündelt alles in **einen** erhöhten Aufruf.
- `src/TenzoraX/VigemXboxBridge.cs` — virtuelle Xbox 360 (ViGEm), meldet unter anderem den belegten XInput-Slot.
- `src/TenzoraX/InputSimulator.cs` — SendInput, Rechteprüfung.
- `src/TenzoraX/Detection/` — Erkennung über HID, DirectInput, XInput, Raw Input, WinMM; dazu `PhysicalDeviceResolver` (alle Schnittstellen eines Pads), `XInputSlotFallback` (Entscheidung „spiegeln" vs. „nur Duplikat verstecken"), `ControllerDiagnostics`.
- `src/TenzoraX/App.xaml.cs` — Start, `--diagnose`, Einzelinstanz-Mechanismus.
- `src/TenzoraX/Detection/InputEndpoints.cs` — Live-Sicht der Eingabe-Endpunkte: XInput-Slots, DirectInput-Geräte (inkl. Markierung leerer Sammelgeräte), in Windows deaktivierte Schnittstellen desselben Pads (samt Gerätepfad) und die Ableitung, was Windows-Panel und Steam daraus zählen. Die Rechenregeln sind rein und getestet.
- `.freebuff/` — Ablage für temporäre Hilfsskripte; nach Gebrauch löschen, gehört nicht ins Repo.

## Umgebung dieses Rechners (gemessen)

- Pad: 8BitDo Ultimate 2 Wireless, `VID_2DC8&PID_310B`. Es meldet mehrere HID-Sammlungen; nur `…&IG_00` („HID-konformer Gamecontroller") liefert Eingaben, `…&MI_01&COL02` (Multimedia) und `…&MI_02` (Hersteller) sind funktionslos und wurden **deaktiviert**, damit das Windows-Panel nur einen Controller zeigt. Umkehrbar über `Enable-PnpDevice`.
- XInput-Slot 0 gehört dem echten Pad; ein per ViGEm erzeugtes Pad landet auf Slot 1, weil versteckte Geräte ihren Slot behalten — Spiele, die nur Slot 0 lesen, sehen mit reiner Spiegelung keinen Controller. Deshalb gibt es den Duplikat-Modus.
- HidHide-CLI: `C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe`; erwartet geschlossenes stdin (`echo. | …`), sonst blockiert der Aufruf. Lesebefehle (`--dev-list`, `--cloak-state`, `--app-list`) ohne Admin, Schreibbefehle nur erhöht.
- Die Ausnahmeliste enthält die App, HidHide selbst und `rundll32.exe` (Windows-Panel „Gamecontroller"). HidHide arbeitet **pro Programm**, nicht pro Gerätesammlung.
- Messen immer aus der passenden Sicht: nicht freigegebener Prozess = Sicht von Steam und Spielen; freigegebener Prozess bzw. `--diagnose` = Sicht des Panels.

## Pad-Duplikat in Steam (gemessen am 13.09.2026)

- Das Pad meldet Windows **zwei vollwertige Eingabegeräte**: den XInput-Zweig `USB\VID_2DC8&PID_310B&MI_00\…` (belegt XInput-Slot 0, SubType GAMEPAD) und eine eigene HID-Gamepad-Sammlung `HID\VID_2DC8&PID_310B&IG_00\…` (UsagePage `0x0001`, Usage `0x0005`, 5 Achsen, 10 Tasten, 1 POV) — das DirectInput-Gesicht.
- **Deshalb zählt Steam zwei Controller** (`Steam\logs\controller.txt`, Zeilen `Local Device Found`): SDL erfasst beide Endpunkte, und weil das Pad keine Seriennummer meldet (Invalid or missing unit serial number), bekommen sie zwei getrennte Identitäten (`2dc8-310b-563ec04` und `…g`). Das passiert ohne jeden Treiber und ohne Verstecken — es ist die Bauart des Pads.
- Steam liest Geräte **nur beim Start** neu ein: Weder Hinzufügen noch Entfernen eines Endpunkts erscheinen im laufenden Betrieb im Log. Für jede Messung Steam neu starten (`taskkill /IM steam.exe`, dann `steam.exe`), sonst misst man die alte Liste.
- Panel Gamecontroller (`joy.cpl` in `rundll32.exe`): eine Zeile je aktivem DirectInput-Gerät plus eine Zeile `Controller` für den XInput-Zweig. Die Liste wird nur beim Öffnen aufgebaut, nie live — immer frisch öffnen. Auslesen per MSAA; erhöht geöffnete Fenster sind für normale Prozesse gesperrt (UIPI), deshalb das Panel-Lesen in einen erhöhten Lauf bündeln.
- Funktionslose Sammlungen `…&MI_01&COL02` (Multimedia) und `…&MI_02` (Hersteller) sind **deaktiviert** (`Error`); wären sie aktiv, stünden 3 DirectInput-Geräte und 4 Panel-Zeilen an. Umkehrbar über `Enable-PnpDevice` — Vorsicht: auf **alle** nicht-OK-Knoten angewandt holt das auch diese zwei zurück. Die App zeigt sie seit 1.4.2 im Diagnosefenster unter „In Windows deaktivierte Schnittstellen“ an (Zustand über SetupAPI **ohne** `DIGCF_PRESENT` + `CM_Get_DevNode_Status`, Problemcode 22), damit sichtbar bleibt, was absichtlich fehlt.
- Anzeigenamen von Geräteknoten kommen aus `SPDRP_FRIENDLYNAME` (Rückfall: `SPDRP_DEVICEDESC`). Achtung: Der Aufruf mit Puffergröße 0 liefert die nötige Länge **nicht** zuverlässig — mit fester Puffergröße lesen, sonst bleiben die Namen leer.
- Treiberfreier Weg gegen das Duplikat (gemessen): `Disable-PnpDevice` auf den HID-Knoten `HID\…&IG_00\…` → DirectInput 0 Geräte, **XInput-Slot 0 bleibt belegt** (Pad voll spielbar), Steam zeigt nach Neustart **1** Controller, Panel **1** Zeile (die XInput-Zeile). Preis: DirectInput-Spiele und der benannte Panel-Eintrag fallen weg. Rückweg `Enable-PnpDevice`.
- Nach dieser Umschaltung meldet SDL den HID-Endpunkt auch nach `pnputil /restart-device` nicht wieder an (Steam bleibt bei 1), obwohl Windows ihn wieder führt. Offen: Verhalten nach echtem Reboot oder physischem Ab- und Anstecken.
- Das USB-Verbundgerät `USB\VID_2DC8&PID_310B\…` lässt sich **nicht** per `Disable-PnpDevice` abschalten (Nicht unterstützt). Geräteneustart geht mit `pnputil /restart-device "USB\VID_2DC8&PID_310B\…"` (kann Reboot verlangen, Exitcode 3010).
- **HidHide ist auf diesem Rechner deinstalliert** (Stand 13.09.2026): keine `HidHideCLI.exe`, kein Dienst — der Schalter `HideDuplicateHid` ist damit wirkungslos. Die Setup-Dateien liegen weiter in `%USERPROFILE%\Documents\TenzoraX\drivers\` und `drivers/` (beide 26.08.). Kommt HidHide zurück: CLI unter `…\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe`, erwartet geschlossenes stdin (`echo. | …`), Lesebefehle ohne Admin, Schreibbefehle nur erhöht; Ausnahmeliste mit App, HidHide und `rundll32.exe` (Panel). HidHide arbeitet **pro Programm**, nicht pro Gerätesammlung.
- Nach jedem Zurückstellen nachmessen (`Get-PnpDevice`, DirectInput-Sonde, Panel-Zeilen) — Windows meldet Gerätezustände verzögert.
- Oberfläche prüfen: Das Diagnosefenster lässt sich über UI-Automation öffnen und auslesen (`AutomationElement.FromHandle` → `AutomationId` `BtnDiagnostics` drücken, dann Feld `TxtEndpoints`). Wichtig: aus normaler Sicht liefert UIA/MSAA zu erhöhten Fenstern **keine Kinder** (UIPI) — Prüfläufe erhöht ausführen. Die App startet laut `config.json` im Tray; sichtbar wird das Fenster nur durch einen zweiten Start (Einzelinstanz-Signal).

## Animierter Hintergrund „GhostFibers“ (13.09.2026)

- **Herkunft:** react-bits-Registry-Eintrag `@react-bits/GhostFibers-JS-CSS`. Die Quelle wurde nicht per CLI installiert (das Projekt ist WPF/C#, nicht React), sondern der Shader direkt aus dem Registry-JSON geholt: <https://reactbits.dev/r/GhostFibers-JS-CSS.json>. Es ist ein **WebGL2/GLSL-ES-3.0-Fragment-Shader** (Bibliothek `ogl`): mehrschichtiges Faserfeld mit Welle, radialem Twist, Glow-Bändern, zentraler Atmosphäre, Vignette, Belichtung, Blau-Anhebung und mehrstufigem Grain.
- **Portierung:** `src/TenzoraX/Effects/GhostFibers.fx` (HLSL, Shader Model 3.0) – Mathematik unverändert, nur die Umgebung übersetzt: `gl_FragCoord` → `uv (0..1, oben links) * Resolution`, Uniforms → je ein eigenes Register. Der kompilierte Bytecode `Effects/GhostFibers.ps` liegt als **Resource** in der csproj; WPF hat keinen Shader-Compiler zur Laufzeit.
- **Neu bauen** (Windows SDK ist installiert):
  ```
  cd src/TenzoraX/Effects
  "C:/Program Files (x86)/Windows Kits/10/bin/10.0.26100.0/x64/fxc.exe" /T ps_3_0 /E main /Fo GhostFibers.ps GhostFibers.fx
  ```
  Nach jeder Änderung an der `.fx` also neu kompilieren – die `.ps` ist das Artefakt, das ausgeliefert wird.
- **Anbindung:** `Effects/GhostFibersEffect.cs` (`ShaderEffect`). Jede Konstante wird über `PixelShaderConstantCallback(register)` fest zugeordnet (Register 0/1 Farben, 2–24 Skalare, 25 Auflösung) – so muss keine WPF-Pack-Regel erraten werden. Klassen laden den Shader über `new PixelShader().SetStreamSource(stream)`; schlägt das fehl, läuft das Fenster ohne Effekt weiter (`GhostFibersEffect.IsAvailable`).
- **Einbau:** Wurzel-Grid in `MainWindow.xaml` → `Border x:Name="GhostFibersBackground"` (Rückfallfarbe `#12101A`) mit `BackgroundEffect` **hinter** dem gesamten Inhalt. Animation über `CompositionTarget.Rendering`, gedrosselt auf 30 Bilder/s (WPF rendert nur bei geändertem Uniform), Zeit akkumuliert mit 0,1-s-Sprungbegrenzung; **pausiert**, wenn das Fenster minimiert oder im Tray ist. Die Auflösung wird in **Gerätepixeln** übergeben (`VisualTreeHelper.GetDpi`), sonst skaliert das Muster mit der Windows-Zoomstufe.
- **Schalter:** `AnimatedBackgroundEnabled` (Standard `true`), Abschnitt "🎛 OBERFLÄCHE" im Einstellungsfenster, Texte `Background_Section`/`Background_EnableTitle`/`Background_EnableHint` (DE+EN).
- **Gemessen (13.09.2026, RTX 4070):** Log `GhostFibers: Pixel-Shader geladen`, kein Absturz; zwei Aufnahmen 1,2 s auseinander → **748 von 3582** Pixeln im unteren Hintergrundrand geändert (Summe der Differenzen 212850) → die Fläche animiert wirklich, nicht nur statisch gerendert; CPU 0,77 s Zeit in 5 s ≈ 15 % eines Kerns (inklusive der 250-Hz-Controller-Abfrage).
- **Durchscheinende Panels:** Die vier großen Flächen des Hauptfensters nutzen `GlassPanelBrush` (`#D916161E`, ~85 % Deckkraft, in `App.xaml`) statt `PanelBgBrush`; kleine Bedienelemente bleiben bewusst deckend. Gemessen danach: **18,2 %** aller Fensterpixel ändern sich innerhalb von 1,2 s, und zwar in **allen 832 Zeilen** – das Feld scheint also durch die Panels, nicht nur in den Rändern (vorher: nur ~20 % der Randstreifen-Pixel). Bei Bedarf wieder umstellbar, indem die vier Borders zurück auf `PanelBgBrush` gesetzt werden.
- **Sparleiter statt Abschaltung** (`Effects/BackgroundPerformancePolicy.cs`, reine Logik, 18 Tests): vier Stufen, die zwei Dinge auf einmal senken – die **Bildrate** (der einzige hier messbar wirksame Hebel) und die **Arbeit je Pixel** (Faserschichten, Grain):

  | Stufe | Bilder/s | Faserschichten | Grain | Einsatz |
  |---|---|---|---|---|
  | `Full` | 30 | 4 | 0,05 | Standard (wie das Original) |
  | `Reduced` | 20 | 3 | 0,03 | erste Stufe, wenn die Anzeige nicht mitkommt |
  | `Minimal` | 15 | 2 | 0 | zweite Stufe |
  | `Off` | – | – | – | keine Hardware-Beschleunigung oder auch die letzte Stufe zu teuer |

  Entscheidungsregeln: (1) WPF-Renderstufe unter 1 → sofort `Off` (Software-Rendering); (2) Akkubetrieb deckelt die Bildrate auf **15**; (3) fällt der **Anzeigetakt** über ein ganzes 5-s-Fenster unter **25 Takte/s**, wird eine Stufe gespart, nach der letzten abgeschaltet; ab **40 Takten/s** darf wieder aufgestuft werden, dazwischen bleibt die Stufe stehen. Dazu Flatter-Schutz (Aufstufen erst nach 60 s, dann 120/240/300 s, höchstens dreimal) und Lücken-Erkennung (Tray/Standby über mehr als zwei Fenster wird verworfen statt als „viel zu langsam“ gedeutet).
- **Messwerte der Leiter am 13.09.2026** (RTX 4070, Zähler `GPU Engine(pid_…)`): voll 3,82 %, Sparsam (20/s, 3 Schichten) 3,09 %, Minimal (15/s, 2 Schichten) 1,65 %, ohne Effekt 2,37 %; im Log stand dabei `gezeichnet 30,0/s`, `20,0/s`, `15,0/s`. **Nur die Bildrate liefert eine wiederholbare Einsparung** – die Schichten liegen mit erwarteten ~1 Punkt unter der Streuung der Zähler (dieselbe Variante zweimal gemessen: 11,0 % gegen 6,7 %, also ±60 %). Sie werden trotzdem mitgenommen, weil sie garantiert Arbeit sparen und nichts kosten. Auf dem Entwicklungsrechner ist die Leiter **nicht auslösbar**; nachgewiesen ist sie über einen Prüfstand mit festen Zeiten (Stufenfolge normal / 33 Takte/s / 15 Takte/s / Akku / Renderstufe 0) und über die gemessenen Stufen.
- **Negativ gemessen: „weniger Pixel“ hilft in WPF nicht.** Eine echte Auflösungsreduktion ist ohne Direct3D-Interop nicht zu erreichen: `ScaleTransform` spart nichts (9,5 % gegen 9,4 %), `BitmapCache(0.5)` ebenso (9,5 %), weil WPF den Effekt weiter in voller Geräteauflösung rasterisiert. Eine klein gerenderte Fläche mit `VisualBrush` sparte in einer Sonde (9,4 % → 2,8 %), in der laufenden App aber **nichts** (4,6–4,8 % gegen 4,8 %); feste `Width`/`Height` auf dieser Fläche machten es sogar teurer als volle Auflösung (12,5 % gegen 7,3 %). Die XAML-Konstruktion dafür ist wieder entfernt. Die Auflösung, die im Log steht, ist die Fenstergröße in Gerätepixeln (nur Maßstab für das Fasermuster, **keine** Sparmaßnahme).
- **Manuelle Stufe:** `BackgroundQualityOverride` (0 = automatisch, 1–3 = fest) mit Auswahlfeld **„Qualität“** im Abschnitt 🎛 OBERFLÄCHE (Texte `Background_Quality*`, DE+EN). Eine feste Vorgabe schlägt die Regel, `Off` bleibt immer `Off`. Bei jeder Änderung steht `Hintergrund: <Stufe> | Regel sagt: <…>, gerechnet wird: <…>` im Log, dazu alle 30 s der Messbeleg.
- **Wichtige Lehre: keine relative Referenz.** Zuerst verglich die Regel den Takt mit der nativen Bildwiederholrate des Monitors. Der Live-Test am 13.09.2026 widerlegte das: WPF lieferte auf einem **120-Hz**-Bildschirm je nach Zustand **32,6 bis 118,7 Takte/s** – der niedrigste Wert sogar bei völlig **abgeschaltetem** Effekt. Gegen 120 Hz gemessen galt der Effekt deshalb fälschlich als „vielfach zu langsam“ und wurde abgeschaltet (Log 13:08 `Hintergrund: aus (Anzeige kommt auch gedrosselt nicht mit)`). Absolute Ruckelgrenzen sind hier die einzige belastbare Grundlage; `Effects/DisplayRefresh.cs` bleibt nur fürs Log (ordnet die gemessenen Takte ein) und entscheidet nichts.
- **Takt-Sonde** (`.freebuff/ticks-probe`, temporär): kleines WPF-Fenster (1300×820 DIP, Zoom 1,5 → Shader rechnet auf 1950×1230 Pixeln), das fünf Phasen fährt und den Anzeigetakt zählt:

  | Phase | Takte/s | gezeichnet |
  |---|---|---|
  | ohne Effekt (erster Aufbau) | 32,6 | 0 |
  | Effekt ungebremst | **118,7** | 594 |
  | Effekt gedrosselt 30 fps | 63,9 | 107 → **21,4/s** |
  | Effekt gedrosselt 15 fps | 63,8 | 64 → **12,8/s** |
  | wieder ohne Effekt | 63,7 | 0 |

  Der Shader erreicht ungebremst praktisch die volle Bildwiederholrate (118,7 von 120) – er kostet auf dieser Hardware nichts. A/B am ganzen Programm: **CPU ±0 bis −2,2 Prozentpunkte eines Kerns, GPU +1,6 bis +3,4 Punkte** (Zähler `GPU Engine(pid_…)`).
- **Drosselungs-Rundungsfehler** (von der Sonde gefunden): Die Drosselung prüfte `now − letztesBild < Zielabstand`, bei 64 Takten/s also nur jedes dritte Bild → **21 statt 30** Bilder/s. Jetzt wird der Zeitplan aus dem **Ziel** fortgeschrieben (`_backgroundNextFrameAt += TargetInterval`, nach Pausen > 1 s neu gesetzt); im Log steht `gezeichnet 30,0/s (Ziel 30 fps)`.
- **Flatter-Schutz:** Nach einem nachgewiesenen Rücksturz wird „voll“ erst nach einer Wartezeit erneut versucht (60 s, dann 120 s, 240 s, 300 s; höchstens dreimal). Ohne das pendelte der Effekt im Minutentakt und eine schwache GPU würde dauerhaft belastet.
- **Lücken-Erkennung:** Liegt zwischen zwei Takten mehr als zwei Auswertungsfenster (Tray, Standby, blockierte Oberfläche), wird der Wert **verworfen** statt als „viel zu langsam“ gedeutet – sonst hätte der 70-s-Sprung im Test bzw. 10 Minuten Tray den Effekt abgeschaltet.
- **Dauerhaftes Messprotokoll:** Alle 30 s eine Log-Zeile `Hintergrund: Anzeige X Takte/s (Monitor Y Hz), gezeichnet Z/s (Ziel N fps, Stufe)` – damit ist belegt, dass die Drosselung greift und die Oberfläche nicht ausgebremst wird, statt es nur einzustellen.
- **Stolpersteine beim Portieren:** `point` ist in HLSL ein reserviertes Wort (Variable umbenennen); in WPF gibt es **kein** `PixelShader.FromStream` (nur `SetStreamSource`); durch `ImplicitUsings` + WinForms sind `Color` und `Point` mehrdeutig – Alias-Direktiven im Effekt nötig, und in `MainWindow.xaml.cs` `System.Windows.Point` ausschreiben; GLSL-`mat2(c,-s,s,c)` ist spaltenweise, HLSL `float2x2` zeilenweise (Argumente getauscht).

## Akku, Laden und Anzeige (gemessen am 13.09.2026)

- **Kein Ladezustand über XInput.** `XInputGetBatteryInformation` kennt nur vier Stufen (`EMPTY/LOW/MEDIUM/FULL`) und **keinen** Ladezustand. Bei diesem Pad meldet es in **beiden** USB-Zuständen `BATTERY_TYPE_WIRED` (0x01 = „USB verbunden“) **und immer** `LEVEL_FULL`: am Kabel **und** über den 2,4-GHz-Empfänger. `WIRED` ist hier also kein Kabelbeweis (der Empfänger ist selbst USB) und `FULL` keine Messung – bei real 57 % meldete XInput „100 %“. Seit 1.4.3 wird ein WIRED-Stufenwert deshalb als **unbekannt** behandelt (`BatteryEstimate.IsXInputLevelMeaningful`), sonst überschreibt XInputs Platzhalter die Schätzfunktion dauerhaft.
- **Das HID-Report-Layout hängt vom Modus ab.** Historisch gelesen: 11 Byte mit Akkustand in `Byte[10]` (`HID-Batterie erkannt: 57% (HID-Input-Report Byte[10])`, Log 05:31–21:52). Im aktuellen USB-Modus liefert `HID\VID_2DC8&PID_310B&IG_00\…` dagegen **15-Byte-Reports** (gemessen: 4609 Reports in 25 s bei aktiver Bedienung), Aufbau: `[0..1]=00`, `[2]=80`, `[3]=FF`, `[4]=7F`, `[5..8]` = Analogwerte (16-Bit), `[9]=00`, `[10]=80`, `[11]` = Tastenzustände (variiert), `[12..14]=00`. **Kein Akku-Feld**: Bytes 9/10/12/13/14 sind konstant. `ManualReportParser.ReadBattery` akzeptiert nur 11 Byte und liefert daher `null` – für dieses Pad ist der Prozentwert im USB-Modus nicht lesbar, und ohne echten Wert kann auch die Ladeanimation nicht auslösen.
- **Reihenfolge der Anzeige** (1.4.3): echter Messwert → Timer-Schätzung (wenn `BatteryEnabled`) → letzter bekannter Wert mit Altersangabe (`57% (vor 42 min)`) → `N/A`. Damit ist der Timer-Modus wieder sichtbar; vorher verdeckte XInputs Platzhalter ihn.
- **Ladeanimation** (`⚡ Lädt`, wandernder Lichtstreifen im Balken, animierte Tray-Icons): nur bei **nachgewiesenem Anstieg** eines echten Akkuwerts (≥ 1 Prozentpunkt im 10-Minuten-Fenster, klebrig für 5 Minuten). Zusätzlich erkannt: „zwischendurch geladen“ – Pad weg, kommt mit höherem Wert zurück. Der Schalter „Akku-Ladeanimation anzeigen“ (`BatteryAnimationEnabled`) war bis 1.4.3 **tot** (wurde gespeichert, aber nie gelesen) und steuert jetzt Fenster- und Tray-Animation.
- **Timer-Kopplung:** Beim nachgewiesenen Laden zählt der Verbrauch nicht weiter, und wenn ein echter Wert anliegt, wird `BatteryActiveMinutes` auf diesen Wert **nachgezogen** – sonst liefe die Schätzung nach einer Ladung hinterher. Eine bloße USB-Verbindung pausiert absichtlich **nicht** (kabelloses Spielen läuft ebenfalls über den Empfänger).
- **XInput-Geräte bekommen jetzt einen HID-Kanal** (1.4.3): XInputs vier Stufen sind grob, der HID-Report desselben Pads ist feiner (`BatteryEstimate.PreferMorePrecise`). Log beim Öffnen: `HID-Akku-Kanal geöffnet: …`. Für dieses Pad bringt es derzeit keinen Wert (siehe 15-Byte-Layout), die Struktur ist für andere Pads aber richtig.
- **Offene Abkürzung zu einem echten Akkuwert:** Die Hersteller-Schnittstelle `HID\VID_2DC8&PID_310B&MI_02\…` („HID-konformes, vom Hersteller definiertes Gerät“, in der Registry ohne Gamepad-Usage) ist auf diesem Rechner **deaktiviert** – sie wäre der Ort, an dem 8BitDo eigene Status-/Akku-Daten bereitstellt. Sie wieder zu aktivieren kostet allerdings wieder eine zusätzliche Zeile in DirectInput und im Windows-Panel (früher gemessen: 3 DirectInput-Geräte, 4 Panel-Zeilen) und damit vermutlich das Steam-Duplikat. Das ist eine bewusste Abwägung, keine technische Unmöglichkeit.
- Rohdaten-Werkzeug für solche Messungen: HID-Pfad über `SetupDiGetClassDevs(GUID_DEVINTERFACE_HID)` holen, mit `CreateFile(GENERIC_READ)` öffnen, `ReadFile` in eigenem Thread, Ergebnis **vor** `CloseHandle` ausgeben (ein blockierendes `CloseHandle` kann den Prozess aufhängen und die Ausgabe verschlucken). Bei PowerShell-5.1-Sonden nur C# 5: kein `out var`.

## Arbeitsweise in diesem Repo

- Erst messen, dann behaupten — Ergebnisse mit Zahl oder Log-Zeile belegen.
- Keine Änderung ohne Testlauf; gefundene Fehler sofort beheben, nicht „später".
- Tests und Build vor jeder Fertigmeldung; Ergebnis nennen.
- Nichts außerhalb des Projektordners verändern ohne Auftrag; Nutzer-Konfiguration und Gerätezustände immer zurücksetzen.
- Erhöhte Schritte in **einem** Aufruf bündeln (ein UAC-Dialog) und den Grund nennen.
- Hilfsskripte unter `.freebuff/` anlegen, ASCII halten (Windows PowerShell 5.1 liest ANSI) und danach löschen.

## Stand

Version 1.7.2, 227 Tests grün. Neu in 1.7.2: Duplikat-Erkennung komplett entfernt (UI-Checkbox `ChkHideDuplicate`, `XInputSlotFallback`-Klasse, `Slot0FallbackAsked`-Config, 12 Sprachschlüssel, 16 Tests); `PhysicalDeviceResolver` bleibt für Diagnose erhalten. Neu in 1.7.1: die Auswahlliste nennt jetzt die echten Zahlen (`Voll – 30 Bilder/s, 4 Faserschichten`, `Sparsam – 20 Bilder/s, 3 Schichten`, `Minimal – 15 Bilder/s, 2 Schichten, ohne Grain`); der Hinweistext behauptete vorher fälschlich „Jede Stufe behält 30 Bilder/s" (DE+EN korrigiert). Neu in 1.7.0: der animierte Hintergrund **spart Arbeit, statt sich abzuschalten** – Qualitätsstufen `Full` (30 Bilder/s, 4 Faserschichten) → `Reduced` (20/s, 3) → `Minimal` (15/s, 2, ohne Grain) → `Off`, manuell wählbar (Einstellungen → 🎛 OBERFLÄCHE → „Qualität") oder automatisch aus Messung, Akku und Renderstufe; dazu die negativ gemessene Pixel-Reduktion und die gemessene Leiter (GPU 3,82 % → 1,65 %). Neu in 1.4.8: automatische Absicherung des animierten Hintergrunds (Renderstufe, Akkubetrieb, absolute Ruckelgrenze 25 Takte/s, Flatter-Schutz, 30-s-Messprotokoll in `Effects/BackgroundPerformancePolicy.cs`), Takt-Sonde mit gemessenen Zahlen und korrigierte Drosselungs-Rundung (30 statt 21 Bilder/s). Neu in 1.4.4: animierter Hintergrund „GhostFibers" (portierter WebGL-Shader als WPF-Pixel-Shader, `Effects/`, ein-/ausschaltbar). Neu in 1.4.3: Lade-/Akku-Erkennung (`Detection/BatteryChargeDetector.cs` mit `BatteryEstimate`), Ladeanimation in Fenster und Tray (vorhandener Schalter endlich verdrahtet), „letzter Wert mit Altersangabe" statt `N/A`, XInput-Stufenwert am USB als unbekannt behandelt, HID-Akku-Kanal für XInput-Geräte. Neu in 1.4.2: die Live-Diagnose der Eingabe-Endpunkte (`Detection/InputEndpoints.cs`, Bereich „Eingabe-Endpunkte" im Diagnosefenster und im `--diagnose`-Bericht) und der **DIDEVCAPS-Fix** — in der Struktur fehlte `dwDevType` (40 statt 44 Bytes), deshalb scheiterte `GetCapabilities` für jedes Gerät mit `0x80070057` und die App kannte Achsen/Tasten/POV nie (im Bericht stand `Achsen: ?`).
