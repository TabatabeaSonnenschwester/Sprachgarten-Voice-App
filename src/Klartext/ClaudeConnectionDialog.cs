namespace Klartext;

public sealed class ClaudeConnectionDialog : Form
{
    public ClaudeConnectionDialog()
    {
        // Sprint C (S9): skaliert wie der Korrekturdialog; Escape bricht ab, Enter speichert.
        SuspendLayout();
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Claude für die Promptprüfung"; Font = BrandTheme.Body; Size = new(700, 660); MinimumSize = new(540, 440); StartPosition = FormStartPosition.CenterParent;
        var settings = ClaudeConnection.Load();
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new(16) }; Controls.Add(flow);
        var intro = WorkshopUi.Hint("Claude Sonnet 5 · mittlerer Denkaufwand\nEmpfehlung: ausgewogene Prüfung mit begrenztem Verbrauch. Die tatsächliche Qualität muss an deinen Aufgaben geprüft werden. Diktate bleiben lokal."); intro.MaximumSize = new(570, 0); flow.Controls.Add(intro);
        flow.Controls.Add(WorkshopUi.Hint("API-Schlüssel aus deiner Claude Console · nur lokal verschlüsselt speichern"));
        var key = new TextBox { Width = 550, UseSystemPasswordChar = true, MaxLength = 512, AccessibleName = "Claude API-Schlüssel", Text = settings.ApiKey }; flow.Controls.Add(key);
        var enabled = new CheckBox { AutoSize = false, MaximumSize = new(550, 0), Text = "Online-Prüfung aktivieren: ausgewählten Prompt an Anthropic senden und Console-Guthaben verwenden", Checked = settings.Enabled }; flow.Controls.Add(enabled);
        flow.Controls.Add(WorkshopUi.Hint("Monatliches Budget dieser App in US-Dollar (UTC-Kalendermonat, also Monat nach Weltzeit: Der Zähler beginnt am Monatsersten um 1 Uhr, im Sommer um 2 Uhr, wieder bei null)"));
        var budget = new NumericUpDown { DecimalPlaces = 2, Minimum = 0.1m, Maximum = 100, Increment = 0.5m, Value = settings.MonthlyBudgetUsd, Width = 160, AccessibleName = "Claude App-Budget in USD" }; flow.Controls.Add(budget);
        var usage = WorkshopUi.Hint($"In dieser App verbraucht oder vorsorglich reserviert: {settings.ChargedOrReservedUsd:F4} USD.\nDies ist kein Console-Guthabenstand. Abgebrochene Anfragen können berechnet werden. Andere Apps zählen nicht zu diesem Budget. Keine automatische Aufladung durch Sprachgarten."); usage.MaximumSize = new(560, 0); flow.Controls.Add(usage);
        var status = WorkshopUi.Hint(""); status.MaximumSize = new(550, 0);
        var save = WorkshopUi.Button("Speichern", (_, _) =>
        {
            try
            {
                string value = key.Text.Trim(); if (enabled.Checked && (!value.StartsWith("sk-ant-", StringComparison.Ordinal) || value.Any(char.IsWhiteSpace))) throw new InvalidDataException("Bitte einen gültigen Claude-API-Schlüssel eingeben.");
                settings.ApiKey = value; settings.Enabled = enabled.Checked; settings.MonthlyBudgetUsd = budget.Value; settings.Save(); DialogResult = DialogResult.OK; Close();
            }
            catch { status.Text = "Speichern fehlgeschlagen. Schlüssel/Windows-Speicherzugriff prüfen. Es wurde keine Anfrage gesendet."; }
        });
        flow.Controls.Add(save);
        flow.Controls.Add(WorkshopUi.Button("Schlüssel entfernen und deaktivieren", (_, _) => { try { settings.ApiKey = ""; settings.Enabled = false; settings.Save(); DialogResult = DialogResult.OK; Close(); } catch { status.Text = "Entfernen fehlgeschlagen. Datei bleibt erhalten."; } }));
        var cancel = new GardenButton { Text = "Abbrechen", DialogResult = DialogResult.Cancel };
        flow.Controls.Add(cancel);
        flow.Controls.Add(status);
        BrandTheme.Apply(this);
        WorkshopDialogLayout.Attach(flow);
        // Sprint E (7b): Der Text des Ankreuzfelds bricht um statt rechts abgeschnitten zu werden.
        AgentHandoffDialog.WrapCheckBoxText(enabled, flow);
        AcceptButton = save; CancelButton = cancel;
        ResumeLayout(true);
    }
    protected override void OnLoad(EventArgs e) { base.OnLoad(e); BrandTheme.FitToWorkingArea(this); }
}
