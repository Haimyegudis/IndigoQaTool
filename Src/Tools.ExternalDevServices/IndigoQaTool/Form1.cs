using System;
using System.Drawing; // חובה למיקומים וצבעים
using System.Windows.Forms; // חובה לרכיבי UI
using System.Threading.Tasks;

namespace IndigoQaClient
{
    public partial class Form1 : Form
    {
        private QaService _service;

        // הגדרת משתנים לפקדים (Controls)
        private TextBox txtJiraKey;
        private TextBox txtManualLinks;
        private TextBox txtInstructions;
        private TextBox txtResult;
        private Button btnGenerate;

        private CheckBox chkSanity;
        private CheckBox chkNegative;
        private CheckBox chkScenarios;
        private CheckBox chkUi;
        private CheckBox chkValues;
        private CheckBox chkEvents;

        public Form1()
        {
            // בניית ה-UI באופן ידני
            SetupManualUI();

            // אתחול השירות
            _service = new QaService();
        }

        private void SetupManualUI()
        {
            this.Text = "Indigo QA Generator";
            this.Size = new Size(1000, 800);
            this.StartPosition = FormStartPosition.CenterScreen;

            // תוויות
            var lblJira = new Label { Text = "Jira Key:", Location = new Point(20, 20), AutoSize = true, Font = new Font("Arial", 10, FontStyle.Bold) };
            var lblLinks = new Label { Text = "Manual URLs:", Location = new Point(250, 20), AutoSize = true, Font = new Font("Arial", 10, FontStyle.Bold) };
            var lblInst = new Label { Text = "Instructions:", Location = new Point(20, 150), AutoSize = true, Font = new Font("Arial", 10, FontStyle.Bold) };

            // שדות טקסט
            txtJiraKey = new TextBox { Location = new Point(20, 45), Width = 200, Font = new Font("Arial", 10) };
            txtManualLinks = new TextBox { Location = new Point(250, 45), Width = 700, Height = 60, Multiline = true, ScrollBars = ScrollBars.Vertical };
            txtInstructions = new TextBox { Location = new Point(20, 175), Width = 930, Height = 50, Multiline = true };

            // צ'ק בוקסים
            chkSanity = new CheckBox { Text = "Sanity", Location = new Point(20, 120), Checked = true, AutoSize = true };
            chkNegative = new CheckBox { Text = "Negative", Location = new Point(100, 120), Checked = true, AutoSize = true };
            chkScenarios = new CheckBox { Text = "Scenarios", Location = new Point(200, 120), Checked = true, AutoSize = true };
            chkUi = new CheckBox { Text = "UI", Location = new Point(300, 120), Checked = true, AutoSize = true };
            chkValues = new CheckBox { Text = "Values", Location = new Point(350, 120), Checked = true, AutoSize = true };
            chkEvents = new CheckBox { Text = "Events", Location = new Point(430, 120), Checked = false, AutoSize = true };

            // כפתור
            btnGenerate = new Button { Text = "Generate Test Plan", Location = new Point(20, 240), Width = 930, Height = 40, BackColor = Color.CornflowerBlue, ForeColor = Color.White, Font = new Font("Arial", 12, FontStyle.Bold) };
            btnGenerate.Click += BtnGenerate_Click; // חיבור לאירוע

            // תוצאה
            txtResult = new TextBox { Location = new Point(20, 300), Width = 930, Height = 430, Multiline = true, ScrollBars = ScrollBars.Vertical, Font = new Font("Consolas", 10) };

            // הוספה לטופס
            this.Controls.Add(lblJira);
            this.Controls.Add(lblLinks);
            this.Controls.Add(lblInst);
            this.Controls.Add(txtJiraKey);
            this.Controls.Add(txtManualLinks);
            this.Controls.Add(txtInstructions);
            this.Controls.Add(chkSanity);
            this.Controls.Add(chkNegative);
            this.Controls.Add(chkScenarios);
            this.Controls.Add(chkUi);
            this.Controls.Add(chkValues);
            this.Controls.Add(chkEvents);
            this.Controls.Add(btnGenerate);
            this.Controls.Add(txtResult);
        }

        private async void BtnGenerate_Click(object sender, EventArgs e)
        {
            txtResult.Text = "Processing... Please wait.";
            btnGenerate.Enabled = false;

            try
            {
                var options = new QaOptions
                {
                    Sanity = chkSanity.Checked,
                    Negative = chkNegative.Checked,
                    Scenarios = chkScenarios.Checked,
                    Ui = chkUi.Checked,
                    Values = chkValues.Checked,
                    Events = chkEvents.Checked
                };

                string result = await _service.GeneratePlanAsync(
                    txtJiraKey.Text,
                    txtManualLinks.Text,
                    options,
                    txtInstructions.Text
                );

                txtResult.Text = result;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message);
            }
            finally
            {
                btnGenerate.Enabled = true;
            }
        }
    }
}