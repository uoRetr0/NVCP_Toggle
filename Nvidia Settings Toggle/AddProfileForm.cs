using System;
using System.Windows.Forms;
using FormsLabel = System.Windows.Forms.Label;

namespace NVCP_Toggle
{
    public class AddProfileForm : Form
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

        public AddProfileForm()
        {
            InitializeComponent();
        }

        private void InitializeComponent()
        {
            this.Text = "Add Profile";
            this.ClientSize = new System.Drawing.Size(350, 300);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            FormsLabel lblName = new FormsLabel { Text = "Profile Name:", Left = 20, Top = 20, AutoSize = true };
            txtProfileName = new TextBox { Left = 140, Top = 20, Width = 170 };
            this.Controls.Add(lblName);
            this.Controls.Add(txtProfileName);

            FormsLabel lblProcess = new FormsLabel { Text = "Process Name:", Left = 20, Top = 60, AutoSize = true };
            txtProcessName = new TextBox { Left = 140, Top = 60, Width = 170 };
            this.Controls.Add(lblProcess);
            this.Controls.Add(txtProcessName);

            FormsLabel lblVibrance = new FormsLabel { Text = "Vibrance:", Left = 20, Top = 100, AutoSize = true };
            nudVibrance = new NumericUpDown { Left = 140, Top = 100, Minimum = 0, Maximum = 100 };
            this.Controls.Add(lblVibrance);
            this.Controls.Add(nudVibrance);

            FormsLabel lblHue = new FormsLabel { Text = "Hue:", Left = 20, Top = 130, AutoSize = true };
            nudHue = new NumericUpDown { Left = 140, Top = 130, Minimum = -180, Maximum = 180 };
            this.Controls.Add(lblHue);
            this.Controls.Add(nudHue);

            FormsLabel lblBrightness = new FormsLabel { Text = "Brightness:", Left = 20, Top = 160, AutoSize = true };
            nudBrightness = new NumericUpDown { Left = 140, Top = 160, Minimum = 0, Maximum = 2, DecimalPlaces = 2, Increment = 0.1M };
            this.Controls.Add(lblBrightness);
            this.Controls.Add(nudBrightness);

            FormsLabel lblContrast = new FormsLabel { Text = "Contrast:", Left = 20, Top = 190, AutoSize = true };
            nudContrast = new NumericUpDown { Left = 140, Top = 190, Minimum = 0, Maximum = 2, DecimalPlaces = 2, Increment = 0.1M };
            this.Controls.Add(lblContrast);
            this.Controls.Add(nudContrast);

            FormsLabel lblGamma = new FormsLabel { Text = "Gamma:", Left = 20, Top = 220, AutoSize = true };
            nudGamma = new NumericUpDown { Left = 140, Top = 220, Minimum = 0, Maximum = 3, DecimalPlaces = 2, Increment = 0.1M };
            this.Controls.Add(lblGamma);
            this.Controls.Add(nudGamma);

            btnOK = new Button { Text = "OK", Left = 140, Top = 260, Width = 70 };
            btnOK.Click += BtnOK_Click;
            this.Controls.Add(btnOK);

            btnCancel = new Button { Text = "Cancel", Left = 240, Top = 260, Width = 70 };
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
