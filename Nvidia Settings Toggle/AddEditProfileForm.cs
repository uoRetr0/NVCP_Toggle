using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
        private ComboBox cmbProfileResolutions; // Drop-down for resolution
        private Button btnOK;
        private Button btnCancel;

        // Helper class for resolution modes (same as in MainForm)
        public class ResolutionMode
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public int Frequency { get; set; }
            public int BitsPerPel { get; set; }
            public override string ToString()
            {
                return $"{Width} x {Height}, {Frequency} Hz, {BitsPerPel} bpp";
            }
        }

        private List<ResolutionMode> availableModes = new List<ResolutionMode>();

        // P/Invoke definitions (same as MainForm)
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        public AddEditProfileForm()
        {
            InitializeComponent();
            PopulateResolutions();
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

            // If the profile has a resolution set, try to select it.
            if (Profile.ResolutionWidth != 0 && Profile.ResolutionHeight != 0 &&
                Profile.ResolutionFrequency != 0 && Profile.ResolutionBpp != 0)
            {
                foreach (var mode in availableModes)
                {
                    if (mode.Width == Profile.ResolutionWidth &&
                        mode.Height == Profile.ResolutionHeight &&
                        mode.Frequency == Profile.ResolutionFrequency &&
                        mode.BitsPerPel == Profile.ResolutionBpp)
                    {
                        cmbProfileResolutions.SelectedItem = mode;
                        break;
                    }
                }
            }
            else
            {
                cmbProfileResolutions.SelectedIndex = 0; // "No Change" option
            }
        }

        private void InitializeComponent()
        {
            this.Text = "Add/Edit Profile";
            this.ClientSize = new System.Drawing.Size(350, 380);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            int labelLeft = 20, controlLeft = 140;
            int currentTop = 20, gap = 30;

            // Profile Name
            FormsLabel lblName = new FormsLabel { Text = "Profile Name:", Left = labelLeft, Top = currentTop, AutoSize = true };
            txtProfileName = new TextBox { Left = controlLeft, Top = currentTop, Width = 170 };
            this.Controls.Add(lblName);
            this.Controls.Add(txtProfileName);

            // Process Name
            currentTop += gap;
            FormsLabel lblProcess = new FormsLabel { Text = "Process Name (without .exe):", Left = labelLeft, Top = currentTop, AutoSize = true };
            txtProcessName = new TextBox { Left = labelLeft, Top = currentTop + 20, Width = 290 };
            this.Controls.Add(lblProcess);
            this.Controls.Add(txtProcessName);
            currentTop += 40;

            // Vibrance
            FormsLabel lblVibrance = new FormsLabel { Text = "Vibrance (0–100):", Left = labelLeft, Top = currentTop, AutoSize = true };
            nudVibrance = new NumericUpDown { Left = controlLeft, Top = currentTop, Minimum = 0, Maximum = 100 };
            this.Controls.Add(lblVibrance);
            this.Controls.Add(nudVibrance);
            currentTop += gap;

            // Hue
            FormsLabel lblHue = new FormsLabel { Text = "Hue (–180 to 180):", Left = labelLeft, Top = currentTop, AutoSize = true };
            nudHue = new NumericUpDown { Left = controlLeft, Top = currentTop, Minimum = -180, Maximum = 180 };
            this.Controls.Add(lblHue);
            this.Controls.Add(nudHue);
            currentTop += gap;

            // Brightness
            FormsLabel lblBrightness = new FormsLabel { Text = "Brightness (0.0–2.0):", Left = labelLeft, Top = currentTop, AutoSize = true };
            nudBrightness = new NumericUpDown { Left = controlLeft, Top = currentTop, Minimum = 0, Maximum = 2, DecimalPlaces = 2, Increment = 0.1M };
            this.Controls.Add(lblBrightness);
            this.Controls.Add(nudBrightness);
            currentTop += gap;

            // Contrast
            FormsLabel lblContrast = new FormsLabel { Text = "Contrast (0.0–2.0):", Left = labelLeft, Top = currentTop, AutoSize = true };
            nudContrast = new NumericUpDown { Left = controlLeft, Top = currentTop, Minimum = 0, Maximum = 2, DecimalPlaces = 2, Increment = 0.1M };
            this.Controls.Add(lblContrast);
            this.Controls.Add(nudContrast);
            currentTop += gap;

            // Gamma
            FormsLabel lblGamma = new FormsLabel { Text = "Gamma (0.0–3.0):", Left = labelLeft, Top = currentTop, AutoSize = true };
            nudGamma = new NumericUpDown { Left = controlLeft, Top = currentTop, Minimum = 0, Maximum = 3, DecimalPlaces = 2, Increment = 0.1M };
            this.Controls.Add(lblGamma);
            this.Controls.Add(nudGamma);
            currentTop += gap;

            // Resolution Selection
            FormsLabel lblRes = new FormsLabel { Text = "Resolution:", Left = labelLeft, Top = currentTop, AutoSize = true };
            cmbProfileResolutions = new ComboBox { Left = controlLeft, Top = currentTop, Width = 170, DropDownStyle = ComboBoxStyle.DropDownList };
            this.Controls.Add(lblRes);
            this.Controls.Add(cmbProfileResolutions);
            currentTop += gap;

            // Instruction for resolution drop-down
            FormsLabel lblInstr = new FormsLabel { Text = "Select a resolution or 'No Change'", Left = labelLeft, Top = currentTop, AutoSize = true, ForeColor = System.Drawing.Color.DarkBlue };
            this.Controls.Add(lblInstr);
            currentTop += gap;

            // OK and Cancel buttons
            btnOK = new Button { Text = "OK", Left = controlLeft, Top = currentTop, Width = 70 };
            btnOK.Click += BtnOK_Click;
            this.Controls.Add(btnOK);

            btnCancel = new Button { Text = "Cancel", Left = controlLeft + 100, Top = currentTop, Width = 70 };
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);
        }

        private void PopulateResolutions()
        {
            availableModes.Clear();
            cmbProfileResolutions.Items.Clear();
            // Add "No Change" option as the first item.
            cmbProfileResolutions.Items.Add("No Change");

            DEVMODE dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            int modeNum = 0;
            while (EnumDisplaySettings(null, modeNum, ref dm))
            {
                var mode = new ResolutionMode
                {
                    Width = dm.dmPelsWidth,
                    Height = dm.dmPelsHeight,
                    Frequency = dm.dmDisplayFrequency,
                    BitsPerPel = dm.dmBitsPerPel
                };
                if (!availableModes.Exists(m => m.Width == mode.Width && m.Height == mode.Height &&
                                                  m.Frequency == mode.Frequency && m.BitsPerPel == mode.BitsPerPel))
                {
                    availableModes.Add(mode);
                    cmbProfileResolutions.Items.Add(mode);
                }
                modeNum++;
            }
            // Default select "No Change"
            cmbProfileResolutions.SelectedIndex = 0;
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

            // If a resolution mode (other than "No Change") is selected, save its values.
            if (cmbProfileResolutions.SelectedItem is ResolutionMode mode)
            {
                Profile.ResolutionWidth = mode.Width;
                Profile.ResolutionHeight = mode.Height;
                Profile.ResolutionFrequency = mode.Frequency;
                Profile.ResolutionBpp = mode.BitsPerPel;
            }
            else
            {
                // "No Change" selected; set to 0.
                Profile.ResolutionWidth = 0;
                Profile.ResolutionHeight = 0;
                Profile.ResolutionFrequency = 0;
                Profile.ResolutionBpp = 0;
            }
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
