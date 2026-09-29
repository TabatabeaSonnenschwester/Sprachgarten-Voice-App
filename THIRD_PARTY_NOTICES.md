# Verwendete Komponenten und Gestaltung

Die MIT-Lizenz dieses Projekts ersetzt keine Lizenzen seiner Abhängigkeiten. Die NuGet-Versionen sind in `src/Klartext/packages.lock.json`, die lokale Vorleseumgebung in `scripts/requirements-reading.txt` dokumentiert.

- .NET: MIT und enthaltene Drittanbieterhinweise, siehe `licenses/dotnet-*`.
- NAudio und Whisper.net: MIT; entsprechende Hinweise unter `licenses/`.
- whisper.cpp und separat geladene Whisper-Modelle: jeweilige MIT-Hinweise unter `licenses/`.
- Optionale llama.cpp-/Qwen-Komponenten: eigene Bedingungen und Herkunftshinweise unter `licenses/` sowie im Setup-Skript. Die Modelle sind nicht Teil dieses Repositories.
- Pocket TTS und seine Modelle: separat zu installieren beziehungsweise beim offiziellen Anbieter zu beziehen; dessen Bedingungen gelten zusätzlich. Es werden keine Gewichte oder fremden Sprachvorlagen ausgeliefert.
- Schrift Indie Flower: Copyright 2010 The Indie Flower Authors (kimberlygeswein.com), SIL Open Font License 1.1. Originalhinweise unter `src/Klartext/Assets/Fonts/OFL.txt` und `licenses/Indie-Flower-OFL.txt`.
- App-Mikrofon und Wortmarke sind für Sprachgarten erzeugte Projektgrafiken. Das Windows-Icon ist daraus abgeleitet. Private Referenzbilder und frühere Entwürfe sind nicht enthalten.

Vor der Weitergabe eines ausführbaren Komplettpakets auch die tatsächlich enthaltenen Laufzeiten und Modellbedingungen prüfen; ein Quellcode-Repository allein liefert diese Bestandteile nicht aus.
