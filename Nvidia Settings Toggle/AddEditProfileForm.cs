using System;
using System.Collections.Generic;
using System.Drawing;
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
        private TrackBar trackBarVibrance;
        private NumericUpDown nudHue;
        private TrackBar trackBarHue;
        private NumericUpDown nudBrightness;
        private TrackBar trackBarBrightness;
        private NumericUpDown nudContrast;
        private TrackBar trackBarContrast;
        private NumericUpDown nudGamma;
        private TrackBar trackBarGamma;
        private ComboBox cmbProfileResolutions;
        private Button btnOK;
        private Button btnCancel;

        // Helper class for resolution modes.
        public class ResolutionMode
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public int Frequency { get; set; }
            public int BitsPerPel { get; set; }
            public override string ToString() => $"{Width} x {Height}, {Frequency} Hz, {BitsPerPel} bpp";
        }

        private List<ResolutionMode> availableModes = new List<ResolutionMode>();

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
            SetupSliderSync();
            ApplyDarkTheme();
        }

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
                cmbProfileResolutions.SelectedIndex = 0;
            }
        }

        private void SetupSliderSync()
        {
            // Vibrance.
            trackBarVibrance.Minimum = 0;
            trackBarVibrance.Maximum = 100;
            trackBarVibrance.Value = (int)nudVibrance.Value;
            trackBarVibrance.Scroll += (s, e) => { nudVibrance.Value = trackBarVibrance.Value; };
            nudVibrance.ValueChanged += (s, e) => { trackBarVibrance.Value = (int)nudVibrance.Value; };

            // Hue.
            trackBarHue.Minimum = -180;
            trackBarHue.Maximum = 180;
            trackBarHue.Value = (int)nudHue.Value;
            trackBarHue.Scroll += (s, e) => { nudHue.Value = trackBarHue.Value; };
            nudHue.ValueChanged += (s, e) => { trackBarHue.Value = (int)nudHue.Value; };

            // Brightness.
            trackBarBrightness.Minimum = 0;
            trackBarBrightness.Maximum = 200;
            trackBarBrightness.Value = (int)(nudBrightness.Value * 100);
            trackBarBrightness.Scroll += (s, e) => { nudBrightness.Value = (decimal)trackBarBrightness.Value / 100; };
            nudBrightness.ValueChanged += (s, e) => { trackBarBrightness.Value = (int)(nudBrightness.Value * 100); };

            // Contrast.
            trackBarContrast.Minimum = 0;
            trackBarContrast.Maximum = 200;
            trackBarContrast.Value = (int)(nudContrast.Value * 100);
            trackBarContrast.Scroll += (s, e) => { nudContrast.Value = (decimal)trackBarContrast.Value / 100; };
            nudContrast.ValueChanged += (s, e) => { trackBarContrast.Value = (int)(nudContrast.Value * 100); };

            // Gamma.
            trackBarGamma.Minimum = 0;
            trackBarGamma.Maximum = 300;
            trackBarGamma.Value = (int)(nudGamma.Value * 100);
            trackBarGamma.Scroll += (s, e) => { nudGamma.Value = (decimal)trackBarGamma.Value / 100; };
            nudGamma.ValueChanged += (s, e) => { trackBarGamma.Value = (int)(nudGamma.Value * 100); };
        }

        private void ApplyDarkTheme()
        {
            this.BackColor = Color.FromArgb(45, 45, 48);
            this.ForeColor = Color.White;
            foreach (Control ctl in this.Controls)
                ApplyDarkThemeRecursively(ctl);
        }

        private void ApplyDarkThemeRecursively(Control ctl)
        {
            ctl.BackColor = Color.FromArgb(45, 45, 48);
            ctl.ForeColor = Color.White;
            if (ctl is Button btn)
            {
                btn.BackColor = Color.FromArgb(63, 63, 70);
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderColor = Color.FromArgb(28, 28, 28);
            }
            if (ctl is NumericUpDown nud)
            {
                nud.BackColor = Color.FromArgb(63, 63, 70);
                nud.ForeColor = Color.White;
            }
            if (ctl is TrackBar tb)
            {
                tb.BackColor = Color.FromArgb(45, 45, 48);
            }
            if (ctl is ComboBox cb)
            {
                cb.BackColor = Color.FromArgb(63, 63, 70);
                cb.ForeColor = Color.White;
            }
            foreach (Control child in ctl.Controls)
                ApplyDarkThemeRecursively(child);
        }

        private void PopulateResolutions()
        {
            availableModes.Clear();
            cmbProfileResolutions.Items.Clear();
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
            if (cmbProfileResolutions.SelectedItem is ResolutionMode mode)
            {
                Profile.ResolutionWidth = mode.Width;
                Profile.ResolutionHeight = mode.Height;
                Profile.ResolutionFrequency = mode.Frequency;
                Profile.ResolutionBpp = mode.BitsPerPel;
            }
            else
            {
                Profile.ResolutionWidth = 0;
                Profile.ResolutionHeight = 0;
                Profile.ResolutionFrequency = 0;
                Profile.ResolutionBpp = 0;
            }
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void InitializeComponent()
        {
            this.Text = "Add/Edit Profile";
            this.ClientSize = new Size(500, 500);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimumSize = new Size(500, 500);
            this.MaximizeBox = true;
            int labelLeft = 20, controlLeft = 140;
            int currentTop = 20, gap = 30;

            // Profile Name.
            FormsLabel lblName = new FormsLabel { Text = "Profile Name:", Left = labelLeft, Top = currentTop, AutoSize = true };
            txtProfileName = new TextBox { Left = controlLeft, Top = currentTop, Width = 300 };
            this.Controls.Add(lblName);
            this.Controls.Add(txtProfileName);

            // Process Name.
            currentTop += gap;
            FormsLabel lblProcess = new FormsLabel { Text = "Process Name (without .exe):", Left = labelLeft, Top = currentTop, AutoSize = true };
            txtProcessName = new TextBox { Left = labelLeft, Top = currentTop + 20, Width = 430 };
            this.Controls.Add(lblProcess);
            this.Controls.Add(txtProcessName);
            currentTop += 40;

            // Vibrance.
            FormsLabel lblVibrance = new FormsLabel { Text = "Vibrance (0–100):", Left = labelLeft, Top = currentTop, AutoSize = true };
            nudVibrance = new NumericUpDown { Left = controlLeft, Top = currentTop, Minimum = 0, Maximum = 100, Width = 80 };
            this.Controls.Add(lblVibrance);
            this.Controls.Add(nudVibrance);
            trackBarVibrance = new TrackBar { Left = controlLeft + 90, Top = currentTop, Width = 200, TickStyle = TickStyle.None };
            this.Controls.Add(trackBarVibrance);
            currentTop += gap;

            // Hue.
            FormsLabel lblHue = new FormsLabel { Text = "Hue (–180 to 180):", Left = labelLeft, Top = currentTop, AutoSize = true };
            nudHue = new NumericUpDown { Left = controlLeft, Top = currentTop, Minimum = -180, Maximum = 180, Width = 80 };
            this.Controls.Add(lblHue);
            this.Controls.Add(nudHue);
            trackBarHue = new TrackBar { Left = controlLeft + 90, Top = currentTop, Width = 200, TickStyle = TickStyle.None };
            this.Controls.Add(trackBarHue);
            currentTop += gap;

            // Brightness.
            FormsLabel lblBrightness = new FormsLabel { Text = "Brightness (0.0–2.0):", Left = labelLeft, Top = currentTop, AutoSize = true };
            nudBrightness = new NumericUpDown { Left = controlLeft, Top = currentTop, Minimum = 0, Maximum = 2, DecimalPlaces = 2, Increment = 0.1M, Width = 80 };
            this.Controls.Add(lblBrightness);
            this.Controls.Add(nudBrightness);
            trackBarBrightness = new TrackBar { Left = controlLeft + 90, Top = currentTop, Width = 200, TickStyle = TickStyle.None };
            this.Controls.Add(trackBarBrightness);
            currentTop += gap;

            // Contrast.
            FormsLabel lblContrast = new FormsLabel { Text = "Contrast (0.0–2.0):", Left = labelLeft, Top = currentTop, AutoSize = true };
            nudContrast = new NumericUpDown { Left = controlLeft, Top = currentTop, Minimum = 0, Maximum = 2, DecimalPlaces = 2, Increment = 0.1M, Width = 80 };
            this.Controls.Add(lblContrast);
            this.Controls.Add(nudContrast);
            trackBarContrast = new TrackBar { Left = controlLeft + 90, Top = currentTop, Width = 200, TickStyle = TickStyle.None };
            this.Controls.Add(trackBarContrast);
            currentTop += gap;

            // Gamma.
            FormsLabel lblGamma = new FormsLabel { Text = "Gamma (0.0–3.0):", Left = labelLeft, Top = currentTop, AutoSize = true };
            nudGamma = new NumericUpDown { Left = controlLeft, Top = currentTop, Minimum = 0, Maximum = 3, DecimalPlaces = 2, Increment = 0.1M, Width = 80 };
            this.Controls.Add(lblGamma);
            this.Controls.Add(nudGamma);
            trackBarGamma = new TrackBar { Left = controlLeft + 90, Top = currentTop, Width = 200, TickStyle = TickStyle.None };
            this.Controls.Add(trackBarGamma);
            currentTop += gap;

            // Resolution selection.
            FormsLabel lblRes = new FormsLabel { Text = "Resolution:", Left = labelLeft, Top = currentTop, AutoSize = true };
            cmbProfileResolutions = new ComboBox { Left = controlLeft, Top = currentTop, Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
            this.Controls.Add(lblRes);
            this.Controls.Add(cmbProfileResolutions);
            currentTop += gap;

            // Instruction.
            FormsLabel lblInstr = new FormsLabel { Text = "Select a resolution or 'No Change'", Left = labelLeft, Top = currentTop, AutoSize = true, ForeColor = Color.LightBlue };
            this.Controls.Add(lblInstr);
            currentTop += gap;

            // OK and Cancel.
            btnOK = new Button { Text = "OK", Left = controlLeft, Top = currentTop, Width = 70 };
            btnOK.Click += BtnOK_Click;
            this.Controls.Add(btnOK);
            btnCancel = new Button { Text = "Cancel", Left = controlLeft + 100, Top = currentTop, Width = 70 };
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);
        }
    }
}
