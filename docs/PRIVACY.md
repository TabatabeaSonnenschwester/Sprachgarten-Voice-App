# Daten und Veröffentlichung

Das Git-Repository enthält Quellcode, synthetische Tests, App-Grafiken und öffentliche Dokumentation. Es enthält keine persönlichen Sprachvorlagen, Diktate, Transkripte, Kontoeinstellungen, internen Projektjournale oder vorbereiteten Benutzerprofile.

## Lokale App

- Benutzerprofil unter `%LOCALAPPDATA%\Klartext`.
- Erfolgreich erkannte Diktataufnahmen werden gelöscht; Fehleraufnahmen bleiben für Wiederholen/Verwerfen verfügbar.
- Noch nicht kopierte Ergebnisse und bestimmte Einstellungen werden mit Windows-DPAPI für das Benutzerkonto gesichert.
- Eine bewusst ausgewählte Vorlesestimme wird als lokale, unverschlüsselte WAV kopiert. Vorlesetexte und Ausgabesprache bleiben flüchtig.
- `speech-runtime.json` enthält lokale Pfade. Diese Datei wird pro Installation erzeugt und gehört nicht in Git oder öffentliche Pakete.
- Die optionale Claude-Prüfung sendet nach bewusster Aktivierung Inhalte an den Anbieter. Betriebssystem-Zwischenablageverlauf und Synchronisierung liegen außerhalb der App.

## Änderungen veröffentlichen

`.gitignore` schließt unter anderem `.tools`, `artifacts`, `dist`, Build-Ergebnisse, Audio, Modelle, Profile, lokale Konfiguration und Zugangsdaten aus. Das ersetzt keine Inhaltsprüfung: Namen, persönliche Beispiele oder Schlüssel können auch in Quellcode, Dokumentation, Bildern und Commit-Metadaten stehen. Änderungen deshalb vor jedem Upload prüfen. Eine bereits veröffentlichte Datei wird durch einen späteren Ausschluss nicht rückwirkend entfernt.

Vollständige Anonymität wird nicht versprochen: Der öffentliche GitHub-Account, bewusst gewählte Autorennamen und ausdrücklich genannte Mitwirkende bleiben sichtbar.
