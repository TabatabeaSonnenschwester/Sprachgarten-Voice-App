namespace Klartext;

internal sealed class ReadingHelpDialog : Form
{
    public ReadingHelpDialog()
    {
        Text = "Hilfe zum Vorlesen"; ClientSize = new Size(660, 490);
        MinimumSize = new Size(400, 300); AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent; MinimizeBox = false;
        var close = new GardenButton { Text = "Schließen", DialogResult = DialogResult.Cancel, Dock = DockStyle.Bottom, Height = 48 };
        var guide = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
            AccessibleName = "Anleitung zum lokalen Vorlesen", Text =
            "1. Eigene Stimme einrichten\r\nWähle eine klare WAV-Aufnahme mit 2–30 Sekunden, ohne Musik oder andere Stimmen. Über „Stimme vorbereiten“ lädst du die Stimme einmalig. Fehlt das lokale Vorlesepaket, steht die Einrichtung in docs/READING.md im App-Ordner.\r\n\r\n" +
            "2. Text vorlesen\r\nGib deinen Text ein und wähle „Vorlesen“. Pause hält die Wiedergabe an, Weiter setzt sie fort. Stopp beendet das Vorlesen; die vorbereitete Stimme bleibt normalerweise bereit.\r\n\r\n" +
            "3. Markierung aus einem anderen Programm\r\nStrg+Alt+V liest eine unterstützte Textmarkierung. Dein Text im Eingabefeld bleibt erhalten. Erneutes Drücken stoppt. Wird die Markierung nicht unterstützt, kopiere sie bewusst und wähle „Zwischenablage einfügen“.\r\n\r\n" +
            "4. Deinen Text schützen\r\nEinfügen fragt nach, bevor vorhandener Text ersetzt wird. Nein behält ihn. „Vorherigen Text zurückholen“ stellt den zuletzt ersetzten Text wieder her. Diese Rückholmöglichkeit gilt nur in der laufenden Sitzung. Texte und Ausgaben werden nicht dauerhaft gespeichert.\r\n\r\n" +
            "5. Wenn es nicht startet\r\nPrüfe den Hinweis direkt über dem Textfeld, die Stimmaufnahme und das Ausgabegerät. Ein Diktat hat Vorrang vor der Hintergrundvorbereitung. Bei belegtem Vorlese-Hotkey kannst du weiterhin die Knöpfe im Vorlesebereich verwenden." };
        Controls.Add(guide); Controls.Add(close); CancelButton = close;
        BrandTheme.Apply(this);
        Shown += (_, _) => guide.Select(0, 0);
    }
}
