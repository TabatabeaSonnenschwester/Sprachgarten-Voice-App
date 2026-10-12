# Sprachgarten Voice-App

**Aktuell: Moonshine-Diktat und neue Werkstatt.** Die Sprachverarbeitung beginnt bereits während der Aufnahme. [Moonshine einrichten](docs/MOONSHINE.md).

**Werkstatt:** Ein zentraler Prompt-Editor, freiwillige Prüf- und Ergänzungsbereiche sowie Vorschauen vor Übernahmen. Die letzte Übernahme lässt sich während derselben Sitzung zurücknehmen. Entwurfsicherung und Bibliotheksfassung werden getrennt angezeigt. [Bedienung der Werkstatt](docs/WORKSHOP.md).

Eine deutsche Windows-App zum lokalen Diktieren, Bearbeiten von Texten und Vorlesen mit einer eigenen Stimmvorlage.

**Entwicklungsstand: 0.4.0-preview.19.** Vorschauversion für Windows 11 x64. Dieses Repository enthält Quellcode und Tests. Modelle, Sprachaufnahmen, Benutzerprofile und eingerichtete Python-Umgebungen sind nicht enthalten.

Die neue Oberfläche ordnet die sieben Bereiche in einer linken Seitenleiste. Die Diktatseite zeigt den Einrichtungszustand der separat vorzubereitenden Moonshine-Laufzeit; vorhandene Texte und wartende Aufnahmen behalten Vorrang. Strg+Tab und Strg+Umschalt+Tab wechseln zwischen den Einträgen der Seitenleiste.

Die Vorlesestimme wird ohne exklusive Diktatsperre vorbereitet. Ein Diktat unterbricht eine noch laufende Vorbereitung. Pausen unterbrechen die Spracherzeugung nicht durch eine Gesamtzeitgrenze; fehlende Worker-Antworten und Nutzer-Stopp werden getrennt gemeldet. Fehlende Einrichtung wird direkt im Bereich Vorlesen erklärt.

Die gebündelten Moonshine-Modellartefakte werden vor dem Start des Erkennungsprozesses per SHA-256 geprüft. Ein Prozess bleibt für weitere Diktate bereit. Vor einem manuellen Modellaustausch die App beenden. Online-Prüfaktionen sind ausdrücklich als online gekennzeichnet; eine gespeicherte Zustimmung gilt bis zum Deaktivieren der Verbindung.

Der Vorlese-Hotkey lässt den eigenen Eingabetext unverändert. „Zwischenablage einfügen“ fragt vor dem Ersetzen nach; „Vorherigen Text zurückholen“ stellt den zuletzt ersetzten Text in derselben Sitzung wieder her. Dessen Speicherung endet beim Schließen der App; es wird keine Texthistorie auf Festplatte angelegt. Hilfe ist direkt im Vorlesebereich erreichbar. Für das Diktat werden verfügbare Tastenkürzel vorgeschlagen und erst bei bewusster Auswahl übernommen.

## Funktionen

- **Diktat:** Mikrofonaufnahme per Hotkey, lokale Moonshine-Streaming-Erkennung und bewusste Textübernahme über die Zwischenablage. Standard-Hotkey: Strg+Alt+Leertaste; in den Einstellungen änderbar, beispielsweise auf Strg+Leertaste. Persönliche Einstellungen werden nicht mitgeliefert.
- **Ersetzungen:** selbst bestätigte persönliche Korrekturregeln.
- **Audiodateien und Sprachlabor:** Dateien transkribieren und Erkennung gezielt prüfen.
- **Promptwerkstatt:** Ideen und freiwillige Ergänzungen bearbeiten, Prompts bewusst in einer Bibliothek speichern. Eine optionale Claude-API-Prüfung ist online und kann Kosten verursachen; lokale Diktate benötigen keinen Cloud-Dienst.
- **Vorlesen:** lokale Pocket-TTS-Ausgabe mit selbst gewählter WAV-Stimmvorlage, Streaming, Pause/Weiter/Stopp. Strg+Alt+V liest unterstützte Textmarkierungen. Ein systemweites Rechtsklick-Menü ist nicht enthalten.

Die automatische Textauswahl hängt vom jeweiligen Programm und dessen Windows-Zugänglichkeitsschnittstelle ab. Falls sie nicht verfügbar ist, Text selbst kopieren und im Bereich Vorlesen einfügen. Einträge in Passwortfeldern werden abgewiesen. Diktieren und Vorlesen laufen nicht gleichzeitig.

## Lokal bauen

Voraussetzungen: Windows 11 x64, PowerShell 7 und .NET SDK 10.0.401 (oder passender neuerer Patch gemäß `global.json`). Native Spracherkennung benötigt gegebenenfalls die Microsoft Visual C++ 2022 x64 Runtime.

```powershell
git clone https://github.com/TabatabeaSonnenschwester/Sprachgarten-Voice-App.git
cd Sprachgarten-Voice-App
./scripts/build.ps1

```

Alternativ richtet `./scripts/bootstrap-sdk.ps1` das festgelegte SDK im ignorierten Ordner `.tools` ein. Der erste Build lädt NuGet-Abhängigkeiten. Für ein eigenständig startbares App-Verzeichnis inklusive .NET-Laufzeit:

```powershell
./scripts/build.ps1 -Publish

```

Vor dem ersten Diktat die [Moonshine-Laufzeit und das deutsche Modell einrichten](docs/MOONSHINE.md). Der Build allein enthält diese Abhängigkeiten noch nicht. Der Prüfknopf der App prüft eine vorhandene Einrichtung. Modelle werden nicht aus diesem Git-Repository geladen. Die optionale lokale Prompt-KI benötigt eine zusätzliche Einrichtung; `scripts/setup-prompt-model-test.ps1 -InstructCoach` und `scripts/stage-prompt-runtime.ps1` sind Entwicklungswerkzeuge, keine Qualitätsfreigabe des Modells.

## Vorlesen einrichten

Pocket TTS wird separat installiert. Die vollständigen Schritte und Modell-Prüfwerte stehen unter [Lokales Vorlesen](docs/READING.md). Nach der Einrichtung eine eigene, zur Nutzung freigegebene WAV mit 2–30 Sekunden klarer Sprache wählen. Die App übernimmt eine lokale Kopie der Stimmvorlage. Beim Start ist eine einmalige Modellvorbereitung nötig; anschließend bleibt die Stimme im Arbeitsspeicher.

## Datenschutz

Standardprofil: `%LOCALAPPDATA%\Klartext`. Diktate und Vorlesen werden lokal verarbeitet. Vorlesetexte und erzeugtes Audio werden nicht als Verlauf gespeichert. Die bewusst gewählte Stimmvorlage liegt lokal als WAV und ist nicht verschlüsselt. Optionen zur Online-Prüfung sind gesondert und bewusst zu aktivieren. Details: [Daten und Veröffentlichung](docs/PRIVACY.md).

Keine Aufnahmen, Benutzerprofile, API-Schlüssel oder `speech-runtime.json` in Issues, Pull Requests oder Releases hochladen. Die eigene Stimme wird nicht mit diesem Repository ausgeliefert.

## Tests und Grenzen

```powershell
./scripts/test.ps1 -CorrectionsOnly
./scripts/test.ps1 -ExtensionsOnly
./scripts/test.ps1 -ExtensionUiOnly
./scripts/test.ps1 -ThemeOnly
./scripts/test.ps1 -UxOnly
./scripts/test.ps1 -CloudHandoff
./scripts/test.ps1 -ReadingOnly
./scripts/test.ps1 -ReviewFixes
./scripts/test.ps1 -UxSafety
./scripts/test.ps1 -WorkshopUx
```

Tests benötigen Windows; manche prüfen Windows-DPAPI oder native Steuerelemente. `-ReadingLive` erzeugt hörbare Sprache und benötigt zusätzlich eine eigene freigegebene Stimmvorlage. Weitere Audio-/Zwischenablageprüfungen und Testdatenformat: [test-data/README.md](test-data/README.md). Testberichte können Testtexte enthalten und bleiben im ignorierten Ordner `artifacts`.

Die aktuelle Entwicklungsfassung ist kein Nachweis fehlerfreier Spracherkennung, Stimmähnlichkeit oder universeller Hotkey-Unterstützung. Prüfungsumfang und offene Grenzen: [Teststand](docs/TEST_REPORT.md).

## Mitwirken

Hinweise und Änderungen sind willkommen. Bitte [CONTRIBUTING.md](CONTRIBUTING.md) lesen und ausschließlich künstliche oder ausdrücklich freigegebene Beispiele verwenden.

Projekt: **TabatabeaSonnenschwester**. Genannter Mitwirkender: **[Dennis Westermann / CubeTribe](https://github.com/cubetribe)**. Die Namensnennung ist unabhängig von GitHubs automatisch aus Commits gebildeter Contributors-Anzeige.

## Lizenz

Der eigene Projektcode steht unter der [MIT-Lizenz](LICENSE). Hinweise und Lizenzen verwendeter Komponenten stehen in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) und `licenses/`. Separat bezogene Modelle und Schriften behalten ihre jeweiligen Bedingungen.
