using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
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

        #endregion

        #region Profile Management Fields

        // Profile definition
        public class DisplayProfile
        {
            public string ProfileName { get; set; } = "";
            public string ProcessName { get; set; } = "";
            public int Vibrance { get; set; }
            public int Hue { get; set; }
            public float Brightness { get; set; }
            public float Contrast { get; set; }
            public float Gamma { get; set; }
        }

        private List<DisplayProfile> profiles = new List<DisplayProfile>();
        private DisplayProfile? activeProfile = null;
        private bool isMonitoring = false;
        // Use a Windows Forms Timer for UI thread safety.
        private System.Windows.Forms.Timer profileCheckTimer = new System.Windows.Forms.Timer();


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
        private Button btnRemoveProfile;
        private Button btnApplyProfile;
        private CheckBox chkAutoSwitch;
        private FormsLabel lblStatus;

        #endregion

        public MainForm()
        {
            InitializeComponent();
            Load += MainForm_Load;
        }

        #region Form Load and Initialization

        private void MainForm_Load(object sender, EventArgs e)
        {
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

            // Load configuration (if needed) and profiles
            LoadProfiles();
            UpdateProfileList();

            // Configure auto profile timer (fires every 5000 ms)
            profileCheckTimer.Interval = 5000;
            profileCheckTimer.Tick += (s, ev) => { CheckRunningProcesses(); };

            // Load manual settings from appSettings.json (if exists)
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
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading manual settings: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

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
        }

        private void btnReset_Click(object sender, EventArgs e)
        {
            ResetToDefaults();
            activeProfile = null;
            UpdateStatusDisplay();
        }

        private void btnAddProfile_Click(object sender, EventArgs e)
        {
            // Open a dialog (or use InputBox-style prompts) to add a profile.
            using (var dlg = new AddProfileForm())
            {
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    profiles.Add(dlg.Profile);
                    SaveProfiles();
                    UpdateProfileList();
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
            }
        }

        private void CheckRunningProcesses()
        {
            // Check if any profile's process is running
            foreach (var profile in profiles)
            {
                if (!string.IsNullOrEmpty(profile.ProcessName) &&
                    Process.GetProcessesByName(profile.ProcessName).Length > 0)
                {
                    // If found and not already active, apply it.
                    if (activeProfile != profile)
                    {
                        activeProfile = profile;
                        ApplyProfile(profile);
                        // Use Invoke to update UI safely.
                        this.Invoke((MethodInvoker)delegate {
                            UpdateStatusDisplay();
                        });
                    }
                    return;
                }
            }
            // If no matching process is found and a profile was active, revert.
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
                         $"Auto Profile Switching: {(isMonitoring ? "Enabled" : "Disabled")}";
            }
            lblStatus.Text = status;
        }

        private void UpdateProfileList()
        {
            lstProfiles.Items.Clear();
            foreach (var profile in profiles)
            {
                lstProfiles.Items.Add($"{profile.ProfileName} ({profile.ProcessName}.exe)");
            }
        }

        #endregion

        #region Profile Persistence

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
            // This implementation assumes the primary GDI display corresponds to the first display.
            for (int i = 0; i < config.Length; i++)
            {
                if (config[i].IsGDIPrimary)
                    return allDisplays[i];
            }
            return allDisplays.FirstOrDefault();
        }

        #endregion

        #region Designer Code

        private void InitializeComponent()
        {
            // Set up form
            this.Text = "NVCP Profile Manager";
            this.ClientSize = new System.Drawing.Size(600, 500);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;

            // --- Manual Controls ---
            FormsLabel lblManual = new FormsLabel { Text = "Manual Settings", Left = 20, Top = 20, AutoSize = true, Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold) };
            this.Controls.Add(lblManual);

            FormsLabel lblVibrance = new FormsLabel { Text = "Vibrance:", Left = 20, Top = 60, AutoSize = true };
            nudVibrance = new NumericUpDown { Left = 120, Top = 60, Minimum = 0, Maximum = 100 };
            this.Controls.Add(lblVibrance);
            this.Controls.Add(nudVibrance);

            FormsLabel lblHue = new FormsLabel { Text = "Hue:", Left = 20, Top = 90, AutoSize = true };
            nudHue = new NumericUpDown { Left = 120, Top = 90, Minimum = -180, Maximum = 180 };
            this.Controls.Add(lblHue);
            this.Controls.Add(nudHue);

            FormsLabel lblBrightness = new FormsLabel { Text = "Brightness:", Left = 20, Top = 120, AutoSize = true };
            nudBrightness = new NumericUpDown { Left = 120, Top = 120, Minimum = 0, Maximum = 2, DecimalPlaces = 2, Increment = 0.1M };
            this.Controls.Add(lblBrightness);
            this.Controls.Add(nudBrightness);

            FormsLabel lblContrast = new FormsLabel { Text = "Contrast:", Left = 20, Top = 150, AutoSize = true };
            nudContrast = new NumericUpDown { Left = 120, Top = 150, Minimum = 0, Maximum = 2, DecimalPlaces = 2, Increment = 0.1M };
            this.Controls.Add(lblContrast);
            this.Controls.Add(nudContrast);

            FormsLabel lblGamma = new FormsLabel { Text = "Gamma:", Left = 20, Top = 180, AutoSize = true };
            nudGamma = new NumericUpDown { Left = 120, Top = 180, Minimum = 0, Maximum = 3, DecimalPlaces = 2, Increment = 0.1M };
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

            btnRemoveProfile = new Button { Text = "Remove Profile", Left = 380, Top = 340, Width = 150 };
            btnRemoveProfile.Click += btnRemoveProfile_Click;
            this.Controls.Add(btnRemoveProfile);

            btnApplyProfile = new Button { Text = "Apply Profile", Left = 380, Top = 380, Width = 150 };
            btnApplyProfile.Click += btnApplyProfile_Click;
            this.Controls.Add(btnApplyProfile);

            chkAutoSwitch = new CheckBox { Text = "Enable Auto Profile Switching", Left = 20, Top = 420, AutoSize = true };
            chkAutoSwitch.CheckedChanged += chkAutoSwitch_CheckedChanged;
            this.Controls.Add(chkAutoSwitch);

            lblStatus = new FormsLabel { Text = "Status", Left = 20, Top = 460, AutoSize = true, BorderStyle = BorderStyle.FixedSingle, Width = 550, Height = 30 };
            this.Controls.Add(lblStatus);
        }

        #endregion
    }
}
