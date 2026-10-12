# Lokales Diktieren mit Moonshine

Seit preview.19 übernimmt **Moonshine Deutsch Small Streaming** Mikrofon, Audioimport und Sprachlabor. Die Erkennung verarbeitet Audioblöcke während der Aufnahme; nach Stopp wird das Ergebnis abgeschlossen und kopiert. Das Modell bleibt unverändert, ohne eigene Wortschatzhinweise oder Training. Englische Begriffe und Eigennamen können unterschiedlich gut erkannt werden.

## Eine eigene Ausgabe vorbereiten

Das Repository enthält keine Gewichte, Python-Installation oder persönlichen Profile. Benötigt werden Windows x64, PowerShell 7, .NET gemäß `global.json` und eine vollständige Python-3.12-x64-Installation (lokal geprüft: 3.12.14). Die folgenden Installationsschritte benötigen Internet; nach der Einrichtung ist Diktieren offline möglich.

Im Repository ausführen:

```powershell
./scripts/build.ps1 -Publish
py -3.12 -m pip install --target .tools/moonshine-packages -r scripts/requirements-dictation.txt
$pythonRoot = py -3.12 -c "import sys; print(sys.base_prefix)"
py -3.12 scripts/stage-moonshine.py dist/Sprachgarten-0.4.0-preview.19-win-x64 --python-root $pythonRoot --site-packages .tools/moonshine-packages --download-model
./dist/Sprachgarten-0.4.0-preview.19-win-x64/Sprachgarten.exe
```

Für vorhandene Modelldateien statt `--download-model` den Parameter `--model-source <Ordner>` verwenden. Der feste Download kommt von Moonshine; alle acht Dateien werden gegen die in `src/Klartext/moonshine-config.json` hinterlegten SHA-256-Werte geprüft (zusammen 121.800.823 Bytes). Den Zielordner frisch wählen; vorhandene Laufzeiten werden nicht überschrieben. Das Staging kopiert ausschließlich aus den ausdrücklich übergebenen Laufzeit-/Abhängigkeitsordnern. Dafür einen eigenen pip-Zielordner verwenden, niemals einen Benutzerprofil- oder Audioordner.

Für einen Entwicklungsbuild ohne Publish kann alternativ dessen Ausgabeverzeichnis lokal eingerichtet werden; das Staging-Skript ist bewusst auf Pakete unter `dist` begrenzt. Ohne eingerichtete Laufzeit lässt sich die Oberfläche bauen, Diktieren bleibt bis zur vollständigen Einrichtung gesperrt. Der Prüfknopf in der App lädt keine fehlende Python-Laufzeit herunter.

## Bedienung und Daten

- Start/Stopp per Schaltfläche oder globalem Tastenkürzel. Werksstandard: **Strg+Alt+Leertaste**. Unter Einstellungen → Hotkey ändern kann etwa **Strg+Leertaste** gewählt werden, sofern Windows es freigibt. Gespeicherte eigene Kombinationen bleiben erhalten.
- Nach „Text kopiert“ im gewünschten Programm selbst Strg+V drücken. Die angezeigte Zeit nach Stopp umfasst Abschluss, Konvertierung und Kopieren.
- Standardprofil: `%LOCALAPPDATA%\Klartext`. Fehlgeschlagene Aufnahmen bleiben zur Wiederholung oder zum bewussten Verwerfen erhalten; keine automatische Audiohistorie und keine Transkript-Logs.
- Eigene Korrekturen werden nur durch bestätigtes „Immer ersetzen“ zu wiederverwendbaren Regeln. Damit wird das Sprachmodell nicht nachtrainiert.
- Vorlesen bleibt separat über Pocket TTS eingerichtet. Ein fehlendes Vorlesemodell verhindert kein Diktat.

Vor dem Wechsel zwischen regulären Ausgaben offene Texte sichern und die bisherige Instanz beenden. Die App schützt das reguläre Profil mit einer Einzelsperre. Vorherige Pakete können als Rückfall behalten werden; keine automatische Migration einer separaten Testinstallation.

## Prüfung

`Sprachgarten.exe --moonshine-window-check <Bericht.json>` prüft ein eigenes temporäres Profil ohne Mikrofonaufnahme, Zwischenablage oder globale Hotkeys. Für Dateiprüfungen: `--moonshine-selftest <Audioordner> <Bericht.json>`. Benötigt werden künstliche/freigegebene `short.wav` (PCM16, Mono, 16 kHz; „Morgen treffen wir uns um neun Uhr im Garten.“) und eine längere `long.wav` für den Abbruchfall. Berichte außerhalb des Pakets ablegen; Testaudio nicht veröffentlichen.

Die persönlichen Mikrofon-, Fremdprogramm- und Qualitätstests sind von den automatisierten Komponentenprüfungen zu unterscheiden. Siehe [Teststand](TEST_REPORT.md).
