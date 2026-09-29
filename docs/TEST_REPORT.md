# Öffentlicher Teststand

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
