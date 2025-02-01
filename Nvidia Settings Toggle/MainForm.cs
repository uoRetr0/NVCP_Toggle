using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using NvAPIWrapper;
using NvAPIWrapper.GPU;
using WindowsDisplayAPI;
using WindowsDisplayAPI.DisplayConfig;
using Newtonsoft.Json;

// Alias to avoid ambiguity with Reflection.Emit.Label
using FormsLabel = System.Windows.Forms.Label;

namespace NVCP_Toggle
{
    public partial class MainForm : Form
    {
        #region Default Display Settings

        private const int DefaultVibrance = 50;
        private const int DefaultHue = 0;
        private const float DefaultBrightness = 0.50f;
        private const float DefaultContrast = 0.50f;
        private const float DefaultGamma = 1.0f;
        private static readonly DisplayGammaRamp DefaultGammaRamp = new DisplayGammaRamp();

        // Saved default resolution values.
        private int defaultWidth, defaultHeight, defaultFrequency, defaultBpp;

        #endregion

        #region Profile Management Fields

        // Extended Profile definition (new resolution properties added)
        public class DisplayProfile
        {
            public string ProfileName { get; set; } = "";
            public string ProcessName { get; set; } = "";
            public int Vibrance { get; set; }
            public int Hue { get; set; }
            public float Brightness { get; set; }
            public float Contrast { get; set; }
            public float Gamma { get; set; }
            // New resolution settings; if 0 then resolution change is not applied.
            public int ResolutionWidth { get; set; }
            public int ResolutionHeight { get; set; }
            public int ResolutionFrequency { get; set; }
            public int ResolutionBpp { get; set; }
        }

        private List<DisplayProfile> profiles = new List<DisplayProfile>();
        private DisplayProfile? activeProfile = null;
        private bool isMonitoring = false;
        // Use Windows Forms Timer explicitly.
        private System.Windows.Forms.Timer profileCheckTimer = new System.Windows.Forms.Timer();

        #endregion

        #region Resolution Changing (P/Invoke)

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
        public static extern bool EnumDisplaySettings(
            string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int ChangeDisplaySettingsEx(
            string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

        const int ENUM_CURRENT_SETTINGS = -1;
        const int CDS_UPDATEREGISTRY = 0x00000001;
        const int CDS_TEST = 0x00000002;
        const int DISP_CHANGE_SUCCESSFUL = 0;
        const int DISP_CHANGE_RESTART = 1;

        // Helper method to change resolution given parameters.
        private int ChangeResolution(int width, int height, int frequency, int bpp)
        {
            DEVMODE dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));

            dm.dmPelsWidth = width;
            dm.dmPelsHeight = height;
            dm.dmDisplayFrequency = frequency;
            dm.dmBitsPerPel = bpp;
            // dmFields: DM_PELSWIDTH (0x80000) | DM_PELSHEIGHT (0x100000) | DM_DISPLAYFREQUENCY (0x400000)
            dm.dmFields = 0x80000 | 0x100000 | 0x400000;

            int ret = ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
            if (ret == DISP_CHANGE_SUCCESSFUL)
            {
                ret = ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
            }
            return ret;
        }

        #endregion

        #region UI Controls

        // Manual adjustment controls
        private NumericUpDown nudVibrance;
        private NumericUpDown nudHue;
        private NumericUpDown nudBrightness;
        private NumericUpDown nudContrast;
        private NumericUpDown nudGamma;
        private Button btnApplyManual;
        private Button btnReset;

        // Profile management controls
        private ListBox lstProfiles;
        private Button btnAddProfile;
        private Button btnEditProfile;
        private Button btnRemoveProfile;
        private Button btnApplyProfile;
        private CheckBox chkAutoSwitch;
        private FormsLabel lblStatus;

        // Resolution changer controls
        private ComboBox cmbResolutions;
        private Button btnApplyResolution;
        private Button btnResetResolution;  // New reset button for manual resolution change

        #endregion

        public MainForm()
        {
            InitializeComponent();
            Load += MainForm_Load;
        }

        #region Form Load and Initialization

        private void MainForm_Load(object sender, EventArgs e)
        {
            // Save the default resolution using ENUM_CURRENT_SETTINGS.
            DEVMODE dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm))
            {
                defaultWidth = dm.dmPelsWidth;
                defaultHeight = dm.dmPelsHeight;
                defaultFrequency = dm.dmDisplayFrequency;
                defaultBpp = dm.dmBitsPerPel;
            }

            // Initialize NVIDIA API
            try
            {
                NVIDIA.Initialize();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize NVIDIA API: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnApplyManual.Enabled = false;
                return;
            }

            // Load configuration and profiles
            LoadProfiles();
            UpdateProfileList();

            // Configure auto profile timer (fires every 5000 ms)
            profileCheckTimer.Interval = 5000;
            profileCheckTimer.Tick += (s, ev) => { CheckRunningProcesses(); };

            // Load manual settings and autoSwitch from appSettings.json (if exists)
            try
            {
                var config = new ConfigurationBuilder()
                    .SetBasePath(Environment.CurrentDirectory)
                    .AddJsonFile("appSettings.json", optional: true)
                    .Build();

                nudVibrance.Value = config.GetValue<int>("vibrance", DefaultVibrance);
                nudHue.Value = config.GetValue<int>("hue", DefaultHue);
                nudBrightness.Value = (decimal)config.GetValue<float>("brightness", DefaultBrightness);
                nudContrast.Value = (decimal)config.GetValue<float>("contrast", DefaultContrast);
                nudGamma.Value = (decimal)config.GetValue<float>("gamma", DefaultGamma);
                chkAutoSwitch.Checked = config.GetValue<bool>("autoSwitch", false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading manual settings: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            // Populate available resolutions for manual change.
            PopulateResolutions();

            // Update status display
            UpdateStatusDisplay();
        }

        #endregion

        #region UI Event Handlers

        private void btnApplyManual_Click(object sender, EventArgs e)
        {
            // Apply manual settings
            int vibrance = (int)nudVibrance.Value;
            int hue = (int)nudHue.Value;
            float brightness = (float)nudBrightness.Value;
            float contrast = (float)nudContrast.Value;
            float gamma = (float)nudGamma.Value;
            ApplyManualSettings(vibrance, hue, brightness, contrast, gamma);
            activeProfile = null;
            UpdateStatusDisplay();
            SaveManualSettings();
        }

        private void btnReset_Click(object sender, EventArgs e)
        {
            ResetToDefaults();
            activeProfile = null;
            UpdateStatusDisplay();
        }

        private void btnAddProfile_Click(object sender, EventArgs e)
        {
            using (var dlg = new AddEditProfileForm())
            {
                dlg.Text = "Add Profile";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    profiles.Add(dlg.Profile);
                    SaveProfiles();
                    UpdateProfileList();
                }
            }
        }

        private void btnEditProfile_Click(object sender, EventArgs e)
        {
            if (lstProfiles.SelectedIndex >= 0)
            {
                var selectedProfile = profiles[lstProfiles.SelectedIndex];
                using (var dlg = new AddEditProfileForm(selectedProfile))
                {
                    dlg.Text = "Edit Profile";
                    if (dlg.ShowDialog() == DialogResult.OK)
                    {
                        profiles[lstProfiles.SelectedIndex] = dlg.Profile;
                        SaveProfiles();
                        UpdateProfileList();
                    }
                }
            }
        }

        private void btnRemoveProfile_Click(object sender, EventArgs e)
        {
            if (lstProfiles.SelectedIndex >= 0)
            {
                profiles.RemoveAt(lstProfiles.SelectedIndex);
                SaveProfiles();
                UpdateProfileList();
            }
        }

        private void btnApplyProfile_Click(object sender, EventArgs e)
        {
            if (lstProfiles.SelectedIndex >= 0)
            {
                var selectedProfile = profiles[lstProfiles.SelectedIndex];
                // If the selected profile is already active, revert to default.
                if (activeProfile == selectedProfile)
                {
                    ResetToDefaults();
                    activeProfile = null;
                }
                else
                {
                    ApplyProfile(selectedProfile);
                    activeProfile = selectedProfile;
                }
                UpdateStatusDisplay();
            }
        }

        private void chkAutoSwitch_CheckedChanged(object sender, EventArgs e)
        {
            isMonitoring = chkAutoSwitch.Checked;
            if (isMonitoring)
                profileCheckTimer.Start();
            else
            {
                profileCheckTimer.Stop();
                activeProfile = null;
            }
            UpdateStatusDisplay();
            SaveManualSettings();
        }

        private void btnApplyResolution_Click(object sender, EventArgs e)
        {
            if (cmbResolutions.SelectedItem is ResolutionMode mode)
            {
                if (ChangeResolution(mode.Width, mode.Height, mode.Frequency, mode.BitsPerPel) == DISP_CHANGE_SUCCESSFUL)
                {
                    MessageBox.Show("Resolution changed successfully.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show("Failed to change resolution.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // New button to reset manual resolution change back to default.
        private void btnResetResolution_Click(object sender, EventArgs e)
        {
            if (ChangeResolution(defaultWidth, defaultHeight, defaultFrequency, defaultBpp) == DISP_CHANGE_SUCCESSFUL)
            {
                MessageBox.Show("Resolution reset to default.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("Failed to reset resolution.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        #endregion

        #region Profile Application Methods

        private void ApplyManualSettings(int vibrance, int hue, float brightness, float contrast, float gamma)
        {
            var nvDisplay = GetNvidiaMainDisplay();
            var windowsDisplay = GetWindowsDisplay();
            if (nvDisplay != null && windowsDisplay != null)
            {
                nvDisplay.DigitalVibranceControl.CurrentLevel = vibrance;
                nvDisplay.HUEControl.CurrentAngle = hue;
                windowsDisplay.GammaRamp = new DisplayGammaRamp(brightness, contrast, gamma);
            }
        }

        private void ApplyProfile(DisplayProfile profile)
        {
            var nvDisplay = GetNvidiaMainDisplay();
            var windowsDisplay = GetWindowsDisplay();
            if (nvDisplay != null && windowsDisplay != null)
            {
                nvDisplay.DigitalVibranceControl.CurrentLevel = profile.Vibrance;
                nvDisplay.HUEControl.CurrentAngle = profile.Hue;
                windowsDisplay.GammaRamp = new DisplayGammaRamp(profile.Brightness, profile.Contrast, profile.Gamma);
                // If resolution fields are nonzero, change resolution.
                if (profile.ResolutionWidth != 0 && profile.ResolutionHeight != 0 &&
                    profile.ResolutionFrequency != 0 && profile.ResolutionBpp != 0)
                {
                    ChangeResolution(profile.ResolutionWidth, profile.ResolutionHeight, profile.ResolutionFrequency, profile.ResolutionBpp);
                }
            }
        }

        private void ResetToDefaults()
        {
            var nvDisplay = GetNvidiaMainDisplay();
            var windowsDisplay = GetWindowsDisplay();
            if (nvDisplay != null && windowsDisplay != null)
            {
                nvDisplay.DigitalVibranceControl.CurrentLevel = DefaultVibrance;
                nvDisplay.HUEControl.CurrentAngle = DefaultHue;
                windowsDisplay.GammaRamp = new DisplayGammaRamp(DefaultBrightness, DefaultContrast, DefaultGamma);
                // Revert resolution to saved default.
                ChangeResolution(defaultWidth, defaultHeight, defaultFrequency, defaultBpp);
            }
        }

        private void CheckRunningProcesses()
        {
            foreach (var profile in profiles)
            {
                if (!string.IsNullOrEmpty(profile.ProcessName) &&
                    Process.GetProcessesByName(profile.ProcessName).Length > 0)
                {
                    if (activeProfile != profile)
                    {
                        activeProfile = profile;
                        ApplyProfile(profile);
                        this.Invoke((MethodInvoker)delegate {
                            UpdateStatusDisplay();
                        });
                    }
                    return;
                }
            }
            if (activeProfile != null)
            {
                activeProfile = null;
                ResetToDefaults();
                this.Invoke((MethodInvoker)delegate {
                    UpdateStatusDisplay();
                });
            }
        }

        #endregion

        #region Status Display and Profile List

        private void UpdateStatusDisplay()
        {
            var nvDisplay = GetNvidiaMainDisplay();
            var windowsDisplay = GetWindowsDisplay();

            string status;
            if (nvDisplay == null || windowsDisplay == null)
                status = "No display detected!";
            else
            {
                string gammaState = HasDefaultGammaRamp(windowsDisplay) ? "Default" : "Custom";
                status = $"Digital Vibrance: {nvDisplay.DigitalVibranceControl.CurrentLevel} (Default: {DefaultVibrance})\n" +
                         $"Hue Angle: {nvDisplay.HUEControl.CurrentAngle}° (Default: {DefaultHue}°)\n" +
                         $"Gamma: {gammaState}\n" +
                         $"Active Profile: {(activeProfile != null ? activeProfile.ProfileName : "None")}\n" +
                         $"Auto Profile Switching: {(isMonitoring ? "Enabled" : "Disabled")}\n" +
                         $"Default Resolution: {defaultWidth}x{defaultHeight}, {defaultFrequency} Hz, {defaultBpp} bpp";
            }
            lblStatus.Text = status;
        }

        private void UpdateProfileList()
        {
            lstProfiles.Items.Clear();
            foreach (var profile in profiles)
            {
                string resInfo = (profile.ResolutionWidth != 0)
                    ? $"{profile.ResolutionWidth}x{profile.ResolutionHeight}"
                    : "Unchanged";
                lstProfiles.Items.Add($"{profile.ProfileName} ({profile.ProcessName}.exe) - Res: {resInfo}");
            }
        }

        #endregion

        #region Profile Persistence and Manual Settings Saving

        private void LoadProfiles()
        {
            var profilePath = Path.Combine(Environment.CurrentDirectory, "profiles.json");
            if (File.Exists(profilePath))
            {
                var json = File.ReadAllText(profilePath);
                var data = JsonConvert.DeserializeObject<Dictionary<string, List<DisplayProfile>>>(json);
                if (data != null && data.ContainsKey("Profiles"))
                {
                    profiles = data["Profiles"];
                }
            }
        }

        private void SaveProfiles()
        {
            var json = JsonConvert.SerializeObject(new { Profiles = profiles }, Formatting.Indented);
            File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "profiles.json"), json);
        }

        private void SaveManualSettings()
        {
            var settings = new
            {
                vibrance = (int)nudVibrance.Value,
                hue = (int)nudHue.Value,
                brightness = (float)nudBrightness.Value,
                contrast = (float)nudContrast.Value,
                gamma = (float)nudGamma.Value,
                autoSwitch = chkAutoSwitch.Checked
            };
            string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "appSettings.json"), json);
        }

        #endregion

        #region Helper Methods for Display Access

        private bool HasDefaultGammaRamp(Display windowsDisplay)
        {
            var gammaRamp = windowsDisplay.GammaRamp;
            return gammaRamp.Red.SequenceEqual(DefaultGammaRamp.Red) &&
                   gammaRamp.Green.SequenceEqual(DefaultGammaRamp.Green) &&
                   gammaRamp.Blue.SequenceEqual(DefaultGammaRamp.Blue);
        }

        private Display? GetWindowsDisplay()
        {
            return Display.GetDisplays().FirstOrDefault(d => d.DisplayScreen.IsPrimary);
        }

        private NvAPIWrapper.Display.Display? GetNvidiaMainDisplay()
        {
            var allDisplays = NvAPIWrapper.Display.Display.GetDisplays();
            var config = NvAPIWrapper.Display.PathInfo.GetDisplaysConfig();
            for (int i = 0; i < config.Length; i++)
            {
                if (config[i].IsGDIPrimary)
                    return allDisplays[i];
            }
            return allDisplays.FirstOrDefault();
        }

        #endregion

        #region Resolution Changer Methods

        // Helper class to represent a resolution mode.
        private class ResolutionMode
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

        private void PopulateResolutions()
        {
            availableModes.Clear();
            cmbResolutions.Items.Clear();

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
                if (!availableModes.Any(m => m.Width == mode.Width && m.Height == mode.Height &&
                                               m.Frequency == mode.Frequency && m.BitsPerPel == mode.BitsPerPel))
                {
                    availableModes.Add(mode);
                    cmbResolutions.Items.Add(mode);
                }
                modeNum++;
            }

            // Optionally select current resolution.
            var current = availableModes.FirstOrDefault();
            if (current != null)
            {
                cmbResolutions.SelectedItem = current;
            }
        }

        #endregion

        #region Designer Code

        private void InitializeComponent()
        {
            // Set up form – increased to 650x600 for more space
            this.Text = "NVCP Profile Manager";
            this.ClientSize = new System.Drawing.Size(650, 600);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            // --- Manual Controls ---
            FormsLabel lblManual = new FormsLabel { Text = "Manual Settings", Left = 20, Top = 20, AutoSize = true, Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold) };
            this.Controls.Add(lblManual);

            FormsLabel lblVibrance = new FormsLabel { Text = "Vibrance (0–100):", Left = 20, Top = 60, AutoSize = true };
            nudVibrance = new NumericUpDown { Left = 160, Top = 60, Minimum = 0, Maximum = 100 };
            this.Controls.Add(lblVibrance);
            this.Controls.Add(nudVibrance);

            FormsLabel lblHue = new FormsLabel { Text = "Hue (–180 to 180):", Left = 20, Top = 90, AutoSize = true };
            nudHue = new NumericUpDown { Left = 160, Top = 90, Minimum = -180, Maximum = 180 };
            this.Controls.Add(lblHue);
            this.Controls.Add(nudHue);

            FormsLabel lblBrightness = new FormsLabel { Text = "Brightness (0.0–2.0):", Left = 20, Top = 120, AutoSize = true };
            nudBrightness = new NumericUpDown { Left = 160, Top = 120, Minimum = 0, Maximum = 2, DecimalPlaces = 2, Increment = 0.1M };
            this.Controls.Add(lblBrightness);
            this.Controls.Add(nudBrightness);

            FormsLabel lblContrast = new FormsLabel { Text = "Contrast (0.0–2.0):", Left = 20, Top = 150, AutoSize = true };
            nudContrast = new NumericUpDown { Left = 160, Top = 150, Minimum = 0, Maximum = 2, DecimalPlaces = 2, Increment = 0.1M };
            this.Controls.Add(lblContrast);
            this.Controls.Add(nudContrast);

            FormsLabel lblGamma = new FormsLabel { Text = "Gamma (0.0–3.0):", Left = 20, Top = 180, AutoSize = true };
            nudGamma = new NumericUpDown { Left = 160, Top = 180, Minimum = 0, Maximum = 3, DecimalPlaces = 2, Increment = 0.1M };
            this.Controls.Add(lblGamma);
            this.Controls.Add(nudGamma);

            btnApplyManual = new Button { Text = "Apply Manual Settings", Left = 20, Top = 220, Width = 200 };
            btnApplyManual.Click += btnApplyManual_Click;
            this.Controls.Add(btnApplyManual);

            btnReset = new Button { Text = "Reset to Defaults", Left = 240, Top = 220, Width = 150 };
            btnReset.Click += btnReset_Click;
            this.Controls.Add(btnReset);

            // --- Profile Management Controls ---
            FormsLabel lblProfiles = new FormsLabel { Text = "Profiles", Left = 20, Top = 270, AutoSize = true, Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold) };
            this.Controls.Add(lblProfiles);

            lstProfiles = new ListBox { Left = 20, Top = 300, Width = 350, Height = 100 };
            this.Controls.Add(lstProfiles);

            btnAddProfile = new Button { Text = "Add Profile", Left = 380, Top = 300, Width = 150 };
            btnAddProfile.Click += btnAddProfile_Click;
            this.Controls.Add(btnAddProfile);

            btnEditProfile = new Button { Text = "Edit Profile", Left = 380, Top = 340, Width = 150 };
            btnEditProfile.Click += btnEditProfile_Click;
            this.Controls.Add(btnEditProfile);

            btnRemoveProfile = new Button { Text = "Remove Profile", Left = 380, Top = 380, Width = 150 };
            btnRemoveProfile.Click += btnRemoveProfile_Click;
            this.Controls.Add(btnRemoveProfile);

            btnApplyProfile = new Button { Text = "Apply Profile", Left = 380, Top = 420, Width = 150 };
            btnApplyProfile.Click += btnApplyProfile_Click;
            this.Controls.Add(btnApplyProfile);

            chkAutoSwitch = new CheckBox { Text = "Enable Auto Profile Switching", Left = 20, Top = 420, AutoSize = true };
            chkAutoSwitch.CheckedChanged += chkAutoSwitch_CheckedChanged;
            this.Controls.Add(chkAutoSwitch);

            // --- Resolution Changer Controls ---
            FormsLabel lblResolution = new FormsLabel { Text = "Resolution Changer", Left = 20, Top = 470, AutoSize = true, Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold) };
            this.Controls.Add(lblResolution);

            cmbResolutions = new ComboBox { Left = 20, Top = 500, Width = 350, DropDownStyle = ComboBoxStyle.DropDownList };
            this.Controls.Add(cmbResolutions);

            btnApplyResolution = new Button { Text = "Apply Resolution", Left = 380, Top = 500, Width = 150 };
            btnApplyResolution.Click += btnApplyResolution_Click;
            this.Controls.Add(btnApplyResolution);

            // New Reset button for resolution
            btnResetResolution = new Button { Text = "Reset Resolution", Left = 380, Top = 540, Width = 150 };
            btnResetResolution.Click += btnResetResolution_Click;
            this.Controls.Add(btnResetResolution);

            // --- Status ---
            lblStatus = new FormsLabel { Text = "Status", Left = 20, Top = 540, AutoSize = false, BorderStyle = BorderStyle.FixedSingle, Width = 350, Height = 50 };
            this.Controls.Add(lblStatus);
        }

        #endregion
    }
}
