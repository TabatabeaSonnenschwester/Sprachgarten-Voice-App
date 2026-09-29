# Testdaten

Es werden keine persönlichen Sprachaufnahmen ausgeliefert. Die normalen Komponentensuiten verwenden künstliche Texte. Ergebnisse und temporäre Testprofile liegen unter `artifacts/` und werden nicht veröffentlicht.

`scripts/test.ps1` ohne einen `Only`-Schalter kann native Integrationen einschließlich der Zwischenablage prüfen. Die ausdrücklich gewählten Komponenten-Schalter in der Hauptanleitung vermeiden diesen allgemeinen Lauf. Tests mit hörbarer Wiedergabe oder Mikrofon immer vorher ankündigen; physische Mikrofonabnahme erfolgt separat.

## Hörbarer Vorlesetest

Pocket TTS zuerst nach `docs/READING.md` einrichten. `SPRACHGARTEN_TEST_VOICE` im aktuellen Terminal auf den Pfad einer eigenen, zur Nutzung freigegebenen WAV setzen, anschließend `scripts/test.ps1 -ReadingLive`. Der Test kopiert diese Vorlage in sein lokales Profil und spielt künstliche Testsätze ab. Er benötigt ein Windows-Audiogerät und verwendet kein Mikrofon.

## Optionale Datei-ASR

`SPRACHGARTEN_ASR_FIXTURES` auf einen lokalen Ordner mit eigenen WAV-Dateien und `cases.json` setzen. Format:

```json
[
  {"file":"satz.wav","text":"Ein kurzer künstlicher Testsatz."},
  {"file":"stille.wav","silence":true}
]
```

Mindestens eine Sprachdatei angeben. Whisper Small muss im Testprofil `artifacts/test-profile/models/ggml-small.bin` liegen. `scripts/test.ps1 -Asr` führt zusätzlich allgemeine native Integrationen aus. Lokale Testberichte können Referenztexte und erkannte Inhalte enthalten und dürfen nicht ungeprüft veröffentlicht werden. Die unveränderten Audioquelldateien bleiben im angegebenen Ordner.
