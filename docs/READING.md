# Lokales Vorlesen einrichten

Die Vorlesefunktion verwendet Pocket TTS 3.3.0 mit deutschem Modell und INT8 auf der CPU. Sie benötigt keine Audioübertragung an einen Dienst. Die Modellbeschaffung ist ein gesonderter Online-Schritt; Anbieterbedingungen und Zugangsfreigabe gegebenenfalls selbst bestätigen.

1. Python 3.12 installieren und im Repository eine Umgebung anlegen:

   ```powershell
   py -3.12 -m venv .tools/pocket-tts-env
   ./.tools/pocket-tts-env/Scripts/python.exe -m pip install -r scripts/requirements-reading.txt
   ```

2. Im offiziellen Modellrepository [kyutai/pocket-tts](https://huggingface.co/kyutai/pocket-tts) die deutschen Dateien beschaffen. Getestete Revision: `2dd944b099d06bb9edbd221fef138685711d9bf0`. Dateien `languages/german/model.safetensors` und `languages/german/tokenizer.json` lokal unter `.tools/tts-experiment/pocket-german/` ablegen. Keine Tokens in Befehle, Quellcode oder Chat schreiben.

3. Die erwarteten SHA256-Werte sind:

   | Datei | SHA256 |
   |---|---|
   | model.safetensors | `6b6a8004c17c7b4ddf6c58f309e9e58ba01daa35ad6b2e9761756ce62efbac77` |
   | tokenizer.json | `2d77849811e3d1b6ae80e5b11b203f57e1847901104d8a0573b34f46f8dddb53` |

4. App bauen und lokal verbinden:

   ```powershell
   ./scripts/build.ps1 -Publish
   ./scripts/configure-reading.ps1 -AppDirectory ./dist/Sprachgarten-0.4.0-preview.18-win-x64
   ```

5. App öffnen, im Bereich Vorlesen eine eigene freigegebene WAV wählen: 2–30 Sekunden, 16-Bit PCM oder 32-Bit Float, Mono/Stereo, eine Person ohne Musik. „Stimme vorbereiten“ wählen und anschließend einen kurzen Text testen.

Die Verbindung nutzt absolute lokale Pfade. Nach Verschieben des Repositories erneut konfigurieren. Die erzeugte `speech-runtime.json`, Python-Umgebung, Modellgewichte und Stimmvorlage bleiben außerhalb von Git. Ein nacktes App-ZIP enthält keine fertig eingerichtete Vorlesestimme.

Der lokale Worker nutzt Standardein-/ausgabe, keinen Netzwerkport; Hugging Face läuft im Offline-Modus, Python-Socket-Verbindungen werden abgewiesen. Das ist keine allgemeine Betriebssystem-Firewall für andere Prozesse.

## Vorbereitung, Pause und Abbruch

Beim App-Start wird die eingerichtete Stimme im Hintergrund vorbereitet. Ein Diktat hat Vorrang und unterbricht diese Vorbereitung; beim nächsten Vorlesen wird sie erneut gestartet. Vorlesen während der Vorbereitung wartet auf denselben Worker.

Eine Pause hat keine feste Zeitgrenze. Nur wenn der Worker während einer angeforderten Antwort 90 Sekunden lang keine Daten liefert, meldet Sprachgarten einen Fehler. Ein bestätigter Stopp behält die vorbereitete Stimme. Bleibt die Bestätigung aus, wird der Worker nach drei Sekunden beendet. Die nächste Wiedergabe startet ihn erneut.

Fehlende Einrichtung oder Stimmvorlage wird direkt beim Textfeld erklärt; Vorlesen und Vorbereiten bleiben dann deaktiviert. Eine ausgewählte WAV wird vor dem Modellstart zusätzlich auf ungültige oder stille Audiodaten geprüft.

## Eigener Text bleibt erhalten

Strg+Alt+V liest die Markierung aus einem unterstützten Programm, ohne das eigene Eingabefeld zu ersetzen. Der Status nennt die Textmarkierung. Das gilt auch bei Abbruch und Fehlern.

„Zwischenablage einfügen“ fragt nach, wenn ein anderer Text bereits im Feld steht. Die Standardantwort ist Nein. Nach bestätigtem Ersetzen erscheint „Vorherigen Text zurückholen“. Bei danach vorgenommenen Änderungen fragt auch das Zurückholen nach; diese Änderungen gehen bei Bestätigung verloren. Es gibt genau einen Rückholstand im Arbeitsspeicher, keine dauerhafte Vorlesetext-Historie. Beim Schließen endet diese Rückholmöglichkeit.

„Hilfe zum Vorlesen“ erklärt Einrichtung, Bedienung, Textschutz und Fehlersuche direkt in einem lokalen Fenster.
