using System;
using System.Windows.Forms;
using FormsLabel = System.Windows.Forms.Label;

namespace NVCP_Toggle
{
    public class AddEditProfileForm : Form
    {
        public MainForm.DisplayProfile Profile { get; private set; } = new MainForm.DisplayProfile();

        private TextBox txtProfileName;
        private TextBox txtProcessName;
        private NumericUpDown nudVibrance;
        private NumericUpDown nudHue;
        private NumericUpDown nudBrightness;
        private NumericUpDown nudContrast;
        private NumericUpDown nudGamma;
        private Button btnOK;
        private Button btnCancel;

        public AddEditProfileForm()
        {
            InitializeComponent();
        }

        // Overloaded constructor for editing an existing profile.
        public AddEditProfileForm(MainForm.DisplayProfile existingProfile) : this()
        {
            Profile = existingProfile;
            txtProfileName.Text = Profile.ProfileName;
            txtProcessName.Text = Profile.ProcessName;
            nudVibrance.Value = Profile.Vibrance;
            nudHue.Value = Profile.Hue;
            nudBrightness.Value = (decimal)Profile.Brightness;
            nudContrast.Value = (decimal)Profile.Contrast;
            nudGamma.Value = (decimal)Profile.Gamma;
        }

        private void InitializeComponent()
        {
            this.Text = "Add/Edit Profile";
            this.ClientSize = new System.Drawing.Size(350, 320);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            FormsLabel lblName = new FormsLabel { Text = "Profile Name:", Left = 20, Top = 20, AutoSize = true };
            txtProfileName = new TextBox { Left = 140, Top = 20, Width = 170 };
            this.Controls.Add(lblName);
            this.Controls.Add(txtProfileName);

            FormsLabel lblProcess = new FormsLabel { Text = "Process Name (without .exe):", Left = 20, Top = 50, AutoSize = true };
            txtProcessName = new TextBox { Left = 20, Top = 75, Width = 290 };
            this.Controls.Add(lblProcess);
            this.Controls.Add(txtProcessName);

            FormsLabel lblVibrance = new FormsLabel { Text = "Vibrance (0–100):", Left = 20, Top = 110, AutoSize = true };
            nudVibrance = new NumericUpDown { Left = 140, Top = 110, Minimum = 0, Maximum = 100 };
            this.Controls.Add(lblVibrance);
            this.Controls.Add(nudVibrance);

            FormsLabel lblHue = new FormsLabel { Text = "Hue (–180 to 180):", Left = 20, Top = 140, AutoSize = true };
            nudHue = new NumericUpDown { Left = 140, Top = 140, Minimum = -180, Maximum = 180 };
            this.Controls.Add(lblHue);
            this.Controls.Add(nudHue);

            FormsLabel lblBrightness = new FormsLabel { Text = "Brightness (0.0–2.0):", Left = 20, Top = 170, AutoSize = true };
            nudBrightness = new NumericUpDown { Left = 140, Top = 170, Minimum = 0, Maximum = 2, DecimalPlaces = 2, Increment = 0.1M };
            this.Controls.Add(lblBrightness);
            this.Controls.Add(nudBrightness);

            FormsLabel lblContrast = new FormsLabel { Text = "Contrast (0.0–2.0):", Left = 20, Top = 200, AutoSize = true };
            nudContrast = new NumericUpDown { Left = 140, Top = 200, Minimum = 0, Maximum = 2, DecimalPlaces = 2, Increment = 0.1M };
            this.Controls.Add(lblContrast);
            this.Controls.Add(nudContrast);

            FormsLabel lblGamma = new FormsLabel { Text = "Gamma (0.0–3.0):", Left = 20, Top = 230, AutoSize = true };
            nudGamma = new NumericUpDown { Left = 140, Top = 230, Minimum = 0, Maximum = 3, DecimalPlaces = 2, Increment = 0.1M };
            this.Controls.Add(lblGamma);
            this.Controls.Add(nudGamma);

            btnOK = new Button { Text = "OK", Left = 140, Top = 270, Width = 70 };
            btnOK.Click += BtnOK_Click;
            this.Controls.Add(btnOK);

            btnCancel = new Button { Text = "Cancel", Left = 240, Top = 270, Width = 70 };
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            Profile.ProfileName = txtProfileName.Text;
            Profile.ProcessName = txtProcessName.Text;
            Profile.Vibrance = (int)nudVibrance.Value;
            Profile.Hue = (int)nudHue.Value;
            Profile.Brightness = (float)nudBrightness.Value;
            Profile.Contrast = (float)nudContrast.Value;
            Profile.Gamma = (float)nudGamma.Value;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
