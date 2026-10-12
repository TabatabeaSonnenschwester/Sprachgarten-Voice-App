# Öffentlicher Teststand

## Veröffentlichungsstand preview.19 (12.10.2026)

Die bereinigte Repository-Fassung integriert Moonshine Deutsch Small Streaming, die Variante-2-Navigation, die Vorlesekorrekturen, geschützte Textübernahmen und die zentrale Werkstatt. Die folgenden Prüfungen wurden frisch an dieser Fassung ausgeführt; ältere Abschnitte dokumentieren ihren damaligen Stand.

- Release-Build des Prüfläufers und eigenständige Windows-x64-Ausgabe erfolgreich, keine Compilerfehler. Eine zunächst wegen nicht erreichbarer NuGet-Sicherheitsdaten gemeldete NU1900-Warnung wurde anschließend mit erreichbarer Quelle nachgeprüft: `dotnet list src/Klartext/Klartext.csproj package --vulnerable --include-transitive` meldete keine bekannten anfälligen NuGet-Pakete. Das ist keine Schwachstellenprüfung der separat eingerichteten Python-Pakete.
- 322 gezielte Regressionen bestanden, 0 fehlgeschlagen: Werkstatt 35, UX 34, Review-Fixes 22, Vorlesetextschutz 26, Vorlesekomponenten 9, ExtensionUI 120 und Theme 76. Reproduktion nach dem Testbuild mit `--workshop-ux`, `--ux-only`, `--review-fixes`, `--ux-safety`, `--reading-tests`, `--extension-ui-only` und `--theme-only`, jeweils gefolgt von der Projektwurzel. Keine vollständige Wiederholung aller historischen Suiten behauptet.
- Neues Moonshine-Staging mit ausdrücklich bereitgestellter Python-/Paketinstallation und vorhandenem Modell erfolgreich; alle acht Modelldateien nach SHA-256 geprüft. Der frische Download-/pip-Installationsweg wurde nicht erneut ausgeführt.
- 16 Moonshine-ASR-/Prozessprüfungen bestanden: Streaming in zwei Audioformaten, Stille, Abbruch, Wiederholung und Prozessende. Ausschließlich künstliche Audiodateien; keine Mikrofonaufnahme und keine Zwischenablage. Ein finaler Abschluss benötigte in dieser Stichprobe 0,45 Sekunden; keine allgemeine Latenzgarantie.
- Native Fensterprüfung mit temporärem Profil bestanden: preview.19, Modell bereit, Startschaltfläche aktiv, keine globalen Hotkeys registriert und kein Mikrofon gestartet.
- Unabhängige statische Prüfung von Setup, CLI-Verträgen und Datenschutzgrenzen bestanden. Die für den strikten Veröffentlichungsscan umbenannten Abbruchvariablen wurden zusätzlich auf semantische Gleichheit geprüft.

Die separat durchgeführte persönliche Diktatprobe wurde hinsichtlich Qualität und Geschwindigkeit angenommen. Sie ersetzt keinen automatisierten Genauigkeitsbenchmark. Offen bleiben insbesondere ein vollständiger Screenreader-Durchgang, der globale Vorlese-Hotkey in Fremdprogrammen sowie weitere Klang- und optionale KI-Qualitätsbewertungen. In dieser Veröffentlichungsrunde gab es keinen neuen Hörtest und keinen neuen physischen Mikrofon-/Hotkey-/Einfügetest.

Quellcode, Anleitungen und Tests sind der Veröffentlichungsumfang. Persönliche Profile, Stimmen, Audiodaten, lokale Testprotokolle, Modelle und Laufzeitpakete werden nicht in Git aufgenommen. Eine ausführbare GitHub-Release-Datei ist nicht Bestandteil dieses Stands.

## Lokale Vorschau18 – zentrale Werkstatt (12.10.2026)

369 unterschiedliche Prüfungen im Entwicklungsbaum bestanden:35 neue Werkstattfälle,34 UX,120 ExtensionUI,76 Theme,60 Extensions,44 simulierte Cloud/Handoff-Fälle. Bereinigte Kopie separat erfolgreich gebaut (0 Warnungen/0 Fehler) und35 Werkstattfälle bestanden. Reproduktion: Klartext.Tests.exe --workshop-ux <Projektwurzel>; ausschließlich künstliche Texte und isolierte Testprofile.

Prüft zentrale Bedienstruktur, aktueller Prompt als Ergänzungsbasis, Vorschau vor Übernahmen, vollständige Rücknahme, spätere Änderungen, veraltete Vorschau, Aktivitätssperre, fehlgeschlagene Sicherung und Wiederöffnung eines bewusst leeren Prompts. Unabhängige statische Schlussprüfung PASS nach Korrektur zweier Zustandsfehler. Bibliotheksnavigation korrigiert und leere ausgeblendete Karten im Layoutkriterium berücksichtigt; alle betroffenen Endrunden erfolgreich.

WinForms-Renderings der echten Produktsteuerelemente visuell betrachtet. Kein vollständiger Narrator-/Computer-Use-Klickdurchgang, keine persönliche Mikrofon-/Klang-/Onlineabnahme. Rücknahme und manuelle Vorschau gelten für die laufende Sitzung. Nur lokale Änderungen; kein Commit oder Push.

## Lokale Vorschau17 – Vorlesetextschutz (11.10.2026)

287 unterschiedliche Prüfungen im Entwicklungsbaum bestanden:26 Textschutz,22 Review-Fixes,9 Reading-Komponenten,34 UX,76 Theme und120 ExtensionUI. Bereinigte Kopie zusätzlich gebaut (0 Fehler/0 Warnungen) und mit26 Textschutzfällen geprüft. Reproduktion: Klartext.Tests.exe --ux-safety <Projektwurzel>; künstliche Texte und kontrollierter Worker, keine private Stimmreferenz erforderlich.

Markierungsvorlesen erhält den Editorinhalt. Ersetzen fragt mit Standard Nein; letzte Ersetzung ist während derselben Sitzung rückholbar, spätere Bearbeitung wird nochmals geschützt. Native Dialogbedienung bestätigte Nein per Enter, bewusstes Ersetzen und Rückholen. Unabhängige statische Schlussprüfung ohne verbleibenden wesentlichen Befund nach zwei korrigierten Erstbefunden. Die erste Theme-Runde fand eine uneindeutige Testauswahl nach Umbenennung; korrigiert und vollständig erfolgreich wiederholt.

Kein neuer Mikrofon-/Klang-/vollständiger Narrator- oder Fremdprogramm-Hotkeytest. Rückholung bleibt im Arbeitsspeicher und endet mit der Sitzung. Größere Werkstatt- und Gestaltungsfragen bleiben offen. Nur lokale Änderungen; kein Commit oder Push.

## Lokale Vorschau16 – Review-Korrekturen (11.10.2026)

374 unterschiedliche Prüfungen im Entwicklungsbaum bestanden:22 Review-Fixes,76 Theme,34 UX,120 ExtensionUI,60 Extensions,44 Cloud/Handoff,18 ReadingLive. Die bereinigte Kopie wurde zusätzlich erfolgreich gebaut (0Fehler/0Warnungen) und mit22 Review-Fix-Fällen geprüft. Lange Pause als beschleunigte Worker-Regression; echte lokale Synthese mit künstlichen Texten zusätzlich geprüft. Keine neue Mikrofon-/Cloud-/vollständige Narratorabnahme.

Die Vorleseprobe bestätigte Pause/Weiter/Stopp, Wiederverwendung des Workers und Prozessende; erster warmer Audioblock0,56s, Kaltstart27,9s in einer einzelnen Stichprobe. Keine physische Lautsprecherlatenzmessung oder Klangfreigabe. Unabhängige statische Prüfung ohne verbleibenden wesentlichen Befund; ein zusätzlich gefundener Abbruchfehler wurde behoben.

Reproduktion: nach Testbuild Klartext.Tests.exe --review-fixes <Projektwurzel>. Für --reading-live die separat dokumentierte lokale TTS-Einrichtung und eine bewusst bereitgestellte Testreferenz verwenden; Sprachausgabe ist hörbar. Keine privaten Testdaten werden mitgeliefert.


Ausgangspunkt ist die Entwicklungsfassung 0.4.0-preview.14. Dort wurden 350 bestehende Komponenten-/Regressionsfälle und 18 Vorlesefälle erfolgreich ausgeführt. Die Vorleseprüfungen umfassten echte lokale Synthese, Windows-Audioausgabe, Pause/Stopp und Worker-Lebenszyklus. Diese historische Aussage ersetzt keinen erneuten Build oder Test dieser bereinigten Repository-Fassung.

Die öffentliche Fassung entfernt persönliche Testbeispiele und private Testdatenpfade. ASR- und Vorleseprüfungen benötigen jetzt ausdrücklich selbst bereitgestellte Daten. Nicht mitveröffentlichte Entwicklungsberichte, Aufnahmen und Protokolle bleiben privat.

Offen bleiben insbesondere die persönliche Klangbewertung, der globale Vorlese-Hotkey in tatsächlichen Fremdprogrammen, vollständige physische Mikrofonabnahme und die Qualität optionaler KI-Ergebnisse. UI-Renderings sind keine Desktop-Screenshots. API-Simulationen sind keine echten Online-Aufträge.

## Prüfung der bereinigten Fassung am 29.09.2026

- Release-Build von Anwendung und Prüfläufer erfolgreich: 0 Compilerfehler, 0 Warnungen. Der Build verwendete den vorhandenen lokalen NuGet-Cache mit deaktiviertem NuGet-Audit; daraus folgt keine aktuelle Schwachstellenprüfung.
- Korrekturen: 30 PASS / 0 FAIL, einschließlich der anonymisierten Beispiele und Windows-DPAPI im normalen Benutzerkontext.
- Cloud- und Übergabekomponenten: 44 PASS / 0 FAIL nach Umstellung des Auth-Fixtures auf einen eindeutigen Platzhalter. HTTP-Verhalten wurde simuliert; keine echte Anbieteranfrage. Windows-Schlüsselschutz, Einwilligungsbedienung und Dialogdarstellung wurden lokal geprüft.
- Vorlesekomponenten: 9 PASS / 0 FAIL. Eingabegrenzen, ActivityGate, Bedienelemente und UIA-Auswahl im ausdrücklich adressierten synthetischen Testfeld. Kein erneuter Hörtest und kein Mikrofonzugriff.
- Ein vorheriger Sandbox-Lauf der Korrekturen scheiterte am Zugriff auf Windows-DPAPI. Der anschließende Lauf im normalen Benutzerkontext mit isoliertem Testprofil bestand.
- Die allgemeine Datei-ASR-Suite wurde auf bewusst bereitgestellte lokale Fixtures umgestellt; dieses neue Fixture-Format wurde kompiliert, aber mangels mitgelieferter Audiodaten noch nicht mit echten Dateien ausgeführt.

Die restlichen historischen Ergebnisse oben wurden für diese Repository-Kopie nicht erneut vollständig ausgeführt. Vor einer ausführbaren Veröffentlichung weitere Funktions-, Paket- und native Bedienprüfungen durchführen.
