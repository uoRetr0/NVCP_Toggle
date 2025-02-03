using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Win32; // For registry access
using NvAPIWrapper;
using NvAPIWrapper.GPU;
using WindowsDisplayAPI;
using WindowsDisplayAPI.DisplayConfig;
using Newtonsoft.Json;

// Alias for clarity.
using FormsLabel = System.Windows.Forms.Label;

namespace NVCP_Toggle
{
    public partial class MainForm : Form
    {
        #region Default Display Settings

        private static int DefaultVibrance = 50;
        private static int DefaultHue = 0;
        private static float DefaultBrightness = 50f; // percent (0–100)
        private static float DefaultContrast = 50f;   // percent (0–100)
        private static float DefaultGamma = 1.0f;       // 0.30–2.80
        private static readonly DisplayGammaRamp DefaultGammaRamp = new DisplayGammaRamp();

        // Saved default resolution values.
        private int defaultWidth, defaultHeight, defaultFrequency, defaultBpp;

        #endregion

        #region Profile Management Fields

        public class DisplayProfile
        {
            public string ProfileName { get; set; } = "";
            public string ProcessName { get; set; } = "";
            public int Vibrance { get; set; }
            public int Hue { get; set; }
            public float Brightness { get; set; }
            public float Contrast { get; set; }
            public float Gamma { get; set; }
            // If 0 then no resolution change is applied.
            public int ResolutionWidth { get; set; }
            public int ResolutionHeight { get; set; }
            public int ResolutionFrequency { get; set; }
            public int ResolutionBpp { get; set; }
        }

        private List<DisplayProfile> profiles = new List<DisplayProfile>();
        private DisplayProfile? activeProfile = null;
        private bool isMonitoring = false;
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
        public static extern bool EnumDisplaySettings(string lpszDeviceName, int iModeNum, ref DEVMODE lpDevMode);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int ChangeDisplaySettingsEx(string lpszDeviceName, ref DEVMODE lpDevMode, IntPtr hwnd, uint dwflags, IntPtr lParam);

        const int ENUM_CURRENT_SETTINGS = -1;
        const int CDS_UPDATEREGISTRY = 0x00000001;
        const int CDS_TEST = 0x00000002;
        const int DISP_CHANGE_SUCCESSFUL = 0;
        const int DISP_CHANGE_RESTART = 1;

        private int ChangeResolution(int width, int height, int frequency, int bpp)
        {
            DEVMODE dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            dm.dmPelsWidth = width;
            dm.dmPelsHeight = height;
            dm.dmDisplayFrequency = frequency;
            dm.dmBitsPerPel = bpp;
            dm.dmFields = 0x80000 | 0x100000 | 0x400000; // DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY

            int ret = ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
            if (ret == DISP_CHANGE_SUCCESSFUL)
                ret = ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
            return ret;
        }

        #endregion

        #region UI Controls

        // Manual controls.
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
        private Button btnApplyManual;
        private Button btnReset;
        private Button btnSaveChanges; // new save changes button
        private Button btnSetDefault; // new set default button

        // Profile management.
        private ListBox lstProfiles;
        private Button btnAddProfile;
        private Button btnEditProfile;
        private Button btnRemoveProfile;
        private Button btnApplyProfile;
        private CheckBox chkAutoSwitch;
        private CheckBox chkAutoStart; // New auto start option.
        private FormsLabel lblStatus;

        // Resolution changer.
        private ComboBox cmbResolutions;
        private Button btnApplyResolution;
        private Button btnResetResolution;
        private CheckBox chkAutoConfirmResolution; // New auto-confirm resolution checkbox.

        // Tray icon.
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;

        #endregion

        // Registry key and app name for auto start.
        private const string AutoStartRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "NVCP_Toggle";

        // Add fields for initial resolution.
        private int initialWidth, initialHeight, initialFrequency, initialBpp;

        // New fields for resolution backup when a profile changes it.
        private int backupWidth, backupHeight, backupFreq, backupBpp;
        private bool resolutionChangedByProfile = false;

        public MainForm()
        {
            InitializeComponent();
            Load += MainForm_Load;
            this.Resize += MainForm_Resize;
        }

        #region Form Load and Initialization

        private void MainForm_Load(object? sender, EventArgs e)
        {
            if (!CheckNvidiaSupport())
            {
                MessageBox.Show("NVIDIA APIs are not available.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            LoadProfiles();
            UpdateProfileList();

            profileCheckTimer.Interval = 5000;
            profileCheckTimer.Tick += (s, ev) => { CheckRunningProcesses(); };

            try
            {
                var config = new ConfigurationBuilder()
                    .SetBasePath(Application.StartupPath) // changed from Environment.CurrentDirectory
                    .AddJsonFile("appSettings.json", optional: true)
                    .Build();

                nudVibrance.Value = config.GetValue<int>("vibrance", DefaultVibrance);
                nudHue.Value = config.GetValue<int>("hue", DefaultHue);
                nudBrightness.Value = (decimal)config.GetValue<float>("brightness", DefaultBrightness);
                nudContrast.Value = (decimal)config.GetValue<float>("contrast", DefaultContrast);
                nudGamma.Value = (decimal)config.GetValue<float>("gamma", DefaultGamma);
                chkAutoSwitch.Checked = config.GetValue<bool>("autoSwitch", false);
                chkAutoStart.Checked = config.GetValue<bool>("autoStart", false);
                chkAutoConfirmResolution.Checked = config.GetValue<bool>("autoConfirmResolution", false); // new setting
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading settings:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            // Apply auto-start and save settings setting.
            SetAutoStart(chkAutoStart.Checked);

            PopulateResolutions();
            SetDefaultResolutionValues(); // set and store original resolution
            SetupTrayIcon();
            ApplyDarkTheme();
            UpdateStatusDisplay();
            SetupSliderSync();

            // Removed applying manual settings on startup.
            // ApplyManualSettings((int)nudVibrance.Value, (int)nudHue.Value, (float)nudBrightness.Value, (float)nudContrast.Value, (float)nudGamma.Value);

            // Check command-line arguments to determine window state.
            if (Environment.GetCommandLineArgs().Contains("-minimized"))
            {
                this.WindowState = FormWindowState.Minimized;
                this.Hide();
                trayIcon.Visible = true;
            }
            else
            {
                // Open normally.
                this.WindowState = FormWindowState.Normal;
                this.Show();
            }
            LoadDefaultSettings();
        }

        private bool CheckNvidiaSupport()
        {
            try
            {
                var displays = NvAPIWrapper.Display.Display.GetDisplays();
                return displays != null && displays.Any();
            }
            catch
            {
                return false;
            }
        }

        private void SetAutoStart(bool enable)
        {
            try
            {
                string exePath = Application.ExecutablePath;
                using (var key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, writable: true))
                {
                    if (enable)
                        key?.SetValue(AppName, $"\"{exePath}\" -minimized");
                    else
                        key?.DeleteValue(AppName, false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update auto-start setting: {ex.Message}",
                                "Error",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
            }
        }

        private void SetupSliderSync()
        {
            // Vibrance (0–100).
            trackBarVibrance.Minimum = 0;
            trackBarVibrance.Maximum = 100;
            trackBarVibrance.Value = (int)nudVibrance.Value;
            trackBarVibrance.Scroll += (s, e) => { nudVibrance.Value = trackBarVibrance.Value; };
            nudVibrance.ValueChanged += (s, e) => { trackBarVibrance.Value = (int)nudVibrance.Value; };

            // Hue (0–359).
            trackBarHue.Minimum = 0;
            trackBarHue.Maximum = 359;
            trackBarHue.Value = (int)nudHue.Value;
            trackBarHue.Scroll += (s, e) => { nudHue.Value = trackBarHue.Value; };
            nudHue.ValueChanged += (s, e) => { trackBarHue.Value = (int)nudHue.Value; };

            // Brightness (0–100%).
            trackBarBrightness.Minimum = 0;
            trackBarBrightness.Maximum = 100;
            trackBarBrightness.Value = (int)nudBrightness.Value;
            trackBarBrightness.Scroll += (s, e) => { nudBrightness.Value = trackBarBrightness.Value; };
            nudBrightness.ValueChanged += (s, e) => { trackBarBrightness.Value = (int)nudBrightness.Value; };

            // Contrast (0–100%).
            trackBarContrast.Minimum = 0;
            trackBarContrast.Maximum = 100;
            trackBarContrast.Value = (int)nudContrast.Value;
            trackBarContrast.Scroll += (s, e) => { nudContrast.Value = trackBarContrast.Value; };
            nudContrast.ValueChanged += (s, e) => { trackBarContrast.Value = (int)nudContrast.Value; };

            // Gamma (0.30–2.80). TrackBar uses scaled values (x100).
            trackBarGamma.Minimum = 30;
            trackBarGamma.Maximum = 280;
            trackBarGamma.Value = (int)(nudGamma.Value * 100);
            trackBarGamma.Scroll += (s, e) => { nudGamma.Value = (decimal)trackBarGamma.Value / 100; };
            nudGamma.ValueChanged += (s, e) => { trackBarGamma.Value = (int)(nudGamma.Value * 100); };
        }

        // Apply dark theme to this form and all child controls.
        private void ApplyDarkTheme()
        {
            this.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
            this.ForeColor = System.Drawing.Color.White;
            foreach (Control ctl in this.Controls)
                ApplyDarkThemeRecursively(ctl);
        }

        private void ApplyDarkThemeRecursively(Control ctl)
        {
            ctl.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
            ctl.ForeColor = System.Drawing.Color.White;
            if (ctl is Button btn)
            {
                btn.BackColor = System.Drawing.Color.FromArgb(63, 63, 70);
                btn.FlatStyle = FlatStyle.Flat;
                btn.FlatAppearance.BorderColor = System.Drawing.Color.FromArgb(28, 28, 28);
            }
            if (ctl is NumericUpDown nud)
            {
                nud.BackColor = System.Drawing.Color.FromArgb(63, 63, 70);
                nud.ForeColor = System.Drawing.Color.White;
            }
            if (ctl is TrackBar tb)
            {
                tb.BackColor = System.Drawing.Color.FromArgb(45, 45, 48);
            }
            if (ctl is ComboBox cb)
            {
                cb.BackColor = System.Drawing.Color.FromArgb(63, 63, 70);
                cb.ForeColor = System.Drawing.Color.White;
            }
            foreach (Control child in ctl.Controls)
                ApplyDarkThemeRecursively(child);
        }

        #endregion

        #region UI Event Handlers

        private void btnApplyManual_Click(object? sender, EventArgs e)
        {
            ApplyManualSettings((int)nudVibrance.Value, (int)nudHue.Value, (float)nudBrightness.Value, (float)nudContrast.Value, (float)nudGamma.Value);
            activeProfile = null;
            UpdateStatusDisplay();
            SaveManualSettings();
        }

        private void btnReset_Click(object? sender, EventArgs e)
        {
            ResetToDefaults();
            activeProfile = null;
            UpdateStatusDisplay();
        }

        // New Save Changes event handler.
        private void btnSaveChanges_Click(object? sender, EventArgs e)
        {
            // Removed applying manual settings on saving.
            // ApplyManualSettings((int)nudVibrance.Value, (int)nudHue.Value, (float)nudBrightness.Value, (float)nudContrast.Value, (float)nudGamma.Value);
            SaveManualSettings();
            MessageBox.Show("Settings saved.", "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // New Set Default event handler.
        private void btnSetDefault_Click(object? sender, EventArgs e)
        {
            DefaultVibrance = (int)nudVibrance.Value;
            DefaultHue = (int)nudHue.Value;
            DefaultBrightness = (float)nudBrightness.Value;
            DefaultContrast = (float)nudContrast.Value;
            DefaultGamma = (float)nudGamma.Value;
            SaveDefaultSettings();
            MessageBox.Show("New default settings applied.", "Defaults Updated", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void btnAddProfile_Click(object? sender, EventArgs e)
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

        private void btnEditProfile_Click(object? sender, EventArgs e)
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

        private void btnRemoveProfile_Click(object? sender, EventArgs e)
        {
            if (lstProfiles.SelectedIndex >= 0)
            {
                profiles.RemoveAt(lstProfiles.SelectedIndex);
                SaveProfiles();
                UpdateProfileList();
            }
        }

        private void btnApplyProfile_Click(object? sender, EventArgs e)
        {
            if (lstProfiles.SelectedIndex >= 0)
            {
                var selectedProfile = profiles[lstProfiles.SelectedIndex];
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

        private void chkAutoSwitch_CheckedChanged(object? sender, EventArgs e)
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

        private void chkAutoStart_CheckedChanged(object? sender, EventArgs e)
        {
            SetAutoStart(chkAutoStart.Checked);
            SaveManualSettings();
        }

        private void btnApplyResolution_Click(object? sender, EventArgs e)
        {
            if (cmbResolutions.SelectedItem is ResolutionMode mode)
            {
                int backupWidth = defaultWidth, backupHeight = defaultHeight, backupFreq = defaultFrequency, backupBpp = defaultBpp;
                if (ChangeResolution(mode.Width, mode.Height, mode.Frequency, mode.BitsPerPel) == DISP_CHANGE_SUCCESSFUL)
                {
                    if (chkAutoConfirmResolution.Checked)
                    {
                        // Directly update default resolution without showing a message box.
                        defaultWidth = mode.Width;
                        defaultHeight = mode.Height;
                        defaultFrequency = mode.Frequency;
                        defaultBpp = mode.BitsPerPel;
                    }
                    else
                    {
                        using (var confirmDlg = new ResolutionConfirmForm(15))
                        {
                            if (confirmDlg.ShowDialog() == DialogResult.OK)
                            {
                                MessageBox.Show("Resolution change confirmed.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                defaultWidth = mode.Width;
                                defaultHeight = mode.Height;
                                defaultFrequency = mode.Frequency;
                                defaultBpp = mode.BitsPerPel;
                            }
                            else
                            {
                                ChangeResolution(backupWidth, backupHeight, backupFreq, backupBpp);
                                MessageBox.Show("Resolution change canceled. Reverted.", "Reverted", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            }
                        }
                    }
                }
                else
                    MessageBox.Show("Failed to change resolution.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnResetResolution_Click(object? sender, EventArgs e)
        {
            if (ChangeResolution(initialWidth, initialHeight, initialFrequency, initialBpp) == DISP_CHANGE_SUCCESSFUL)
            {
                // Update current default to initial.
                defaultWidth = initialWidth;
                defaultHeight = initialHeight;
                defaultFrequency = initialFrequency;
                defaultBpp = initialBpp;
            }
            else
                MessageBox.Show("Failed to reset resolution.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void MainForm_Resize(object? sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
                trayIcon.Visible = true;
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
                // Convert percent values (0–100) to ratios (0.0–1.0)
                float normBrightness = Math.Max(0.0f, Math.Min(1.0f, brightness / 100f));
                float normContrast = Math.Max(0.0f, Math.Min(1.0f, contrast / 100f));
                // Gamma remains unchanged, clamped to [0.30, 2.80]
                float clampedGamma = Math.Max(0.30f, Math.Min(2.80f, gamma));
                try
                {
                    windowsDisplay.GammaRamp = new DisplayGammaRamp(normBrightness, normContrast, clampedGamma);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error applying gamma ramp: {ex.Message}", "Gamma Ramp Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                nvDisplay.DigitalVibranceControl.CurrentLevel = vibrance;
                nvDisplay.HUEControl.CurrentAngle = hue;
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
                if (profile.ResolutionWidth != 0 && profile.ResolutionHeight != 0 &&
                    profile.ResolutionFrequency != 0 && profile.ResolutionBpp != 0)
                {
                    // Backup current/default resolution.
                    backupWidth = defaultWidth;
                    backupHeight = defaultHeight;
                    backupFreq = defaultFrequency;
                    backupBpp = defaultBpp;
                    
                    if (ChangeResolution(profile.ResolutionWidth, profile.ResolutionHeight, profile.ResolutionFrequency, profile.ResolutionBpp) == DISP_CHANGE_SUCCESSFUL)
                    {
                        if (chkAutoConfirmResolution.Checked)
                        {
                            // Auto-change: Store new resolution and mark flag for later reversion.
                            defaultWidth = profile.ResolutionWidth;
                            defaultHeight = profile.ResolutionHeight;
                            defaultFrequency = profile.ResolutionFrequency;
                            defaultBpp = profile.ResolutionBpp;
                            resolutionChangedByProfile = true;
                        }
                        else
                        {
                            using (var confirmDlg = new ResolutionConfirmForm(15))
                            {
                                if (confirmDlg.ShowDialog() == DialogResult.OK)
                                {
                                    MessageBox.Show("Resolution change confirmed.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                    defaultWidth = profile.ResolutionWidth;
                                    defaultHeight = profile.ResolutionHeight;
                                    defaultFrequency = profile.ResolutionFrequency;
                                    defaultBpp = profile.ResolutionBpp;
                                    resolutionChangedByProfile = true;
                                }
                                else
                                {
                                    ChangeResolution(backupWidth, backupHeight, backupFreq, backupBpp);
                                }
                            }
                        }
                    }
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
                windowsDisplay.GammaRamp = new DisplayGammaRamp(DefaultBrightness / 100f, DefaultContrast / 100f, DefaultGamma);
                ChangeResolution(defaultWidth, defaultHeight, defaultFrequency, defaultBpp);
            }
            if (resolutionChangedByProfile)
            {
                if (ChangeResolution(backupWidth, backupHeight, backupFreq, backupBpp) == DISP_CHANGE_SUCCESSFUL)
                {
                    defaultWidth = backupWidth;
                    defaultHeight = backupHeight;
                    defaultFrequency = backupFreq;
                    defaultBpp = backupBpp;
                    resolutionChangedByProfile = false;
                }
                else
                {
                    MessageBox.Show("Failed to revert resolution.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void CheckRunningProcesses()
        {
            if (activeProfile == null)
            {
                // No active profile: check if any profile's process is running.
                foreach (var profile in profiles)
                {
                    var procs = Process.GetProcessesByName(profile.ProcessName)
                                        .Where(p => p.MainWindowHandle != IntPtr.Zero);
                    if (procs.Any())
                    {
                        ApplyProfile(profile);
                        activeProfile = profile;
                        UpdateStatusDisplay();
                        break;
                    }
                }
            }
            else
            {
                // Active profile: verify its process is still running.
                var procs = Process.GetProcessesByName(activeProfile.ProcessName)
                                    .Where(p => p.MainWindowHandle != IntPtr.Zero);
                if (!procs.Any())
                {
                    ResetToDefaults();
                    activeProfile = null;
                    UpdateStatusDisplay();
                }
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
                string resInfo = (profile.ResolutionWidth != 0) ? $"{profile.ResolutionWidth}x{profile.ResolutionHeight}" : "Unchanged";
                lstProfiles.Items.Add($"{profile.ProfileName} ({profile.ProcessName}.exe) - Res: {resInfo}");
            }
        }

        #endregion

        #region Profile Persistence and Settings Saving

        private void LoadProfiles()
        {
            var profilePath = Path.Combine(Application.StartupPath, "profiles.json"); // changed from Environment.CurrentDirectory
            if (File.Exists(profilePath))
            {
                var json = File.ReadAllText(profilePath);
                var data = JsonConvert.DeserializeObject<Dictionary<string, List<DisplayProfile>>>(json);
                if (data != null && data.ContainsKey("Profiles"))
                    profiles = data["Profiles"];
            }
        }

        private void SaveProfiles()
        {
            var json = JsonConvert.SerializeObject(new { Profiles = profiles }, Formatting.Indented);
            File.WriteAllText(Path.Combine(Application.StartupPath, "profiles.json"), json); // changed path
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
                autoSwitch = chkAutoSwitch.Checked,
                autoStart = chkAutoStart.Checked,
                autoConfirmResolution = chkAutoConfirmResolution.Checked // new setting
            };
            string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(Path.Combine(Application.StartupPath, "appSettings.json"), json); // changed path
        }

        private void SaveDefaultSettings()
        {
            try
            {
                var defaults = new
                {
                    Vibrance = DefaultVibrance,
                    Hue = DefaultHue,
                    Brightness = DefaultBrightness,
                    Contrast = DefaultContrast,
                    Gamma = DefaultGamma
                };
                System.IO.File.WriteAllText("defaults.json", Newtonsoft.Json.JsonConvert.SerializeObject(defaults, Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving defaults: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadDefaultSettings()
        {
            try
            {
                if (System.IO.File.Exists("defaults.json"))
                {
                    var json = System.IO.File.ReadAllText("defaults.json");
                    dynamic defaults = Newtonsoft.Json.JsonConvert.DeserializeObject(json);
                    DefaultVibrance = (int)defaults.Vibrance;
                    DefaultHue = (int)defaults.Hue;
                    DefaultBrightness = (float)defaults.Brightness;
                    DefaultContrast = (float)defaults.Contrast;
                    DefaultGamma = (float)defaults.Gamma;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading defaults: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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

        private class ResolutionMode
        {
            public int Width { get; set; }
            public int Height { get; set; }
            public int Frequency { get; set; }
            public int BitsPerPel { get; set; }
            public override string ToString() => $"{Width} x {Height}, {Frequency} Hz, {BitsPerPel} bpp";
        }

        private List<ResolutionMode> availableModes = new List<ResolutionMode>();

        private void PopulateResolutions()
        {
            availableModes.Clear();
            cmbResolutions.Items.Clear();
            int modeNum = 0;
            DEVMODE dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
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
            if (availableModes.Any())
                cmbResolutions.SelectedItem = availableModes.First();
        }

        // New helper to store the original resolution.
        private void SetDefaultResolutionValues()
        {
            DEVMODE dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            if(EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm))
            {
                defaultWidth = dm.dmPelsWidth;
                defaultHeight = dm.dmPelsHeight;
                defaultFrequency = dm.dmDisplayFrequency;
                defaultBpp = dm.dmBitsPerPel;
                // Save the initial resolution.
                initialWidth = defaultWidth;
                initialHeight = defaultHeight;
                initialFrequency = defaultFrequency;
                initialBpp = defaultBpp;
            }
        }

        #endregion

        #region Tray Icon Setup

        private void SetupTrayIcon()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Show", null, (s, e) =>
            {
                this.Show();
                this.WindowState = FormWindowState.Normal;
                trayIcon.Visible = false;
            });
            trayMenu.Items.Add("Exit", null, (s, e) =>
            {
                trayIcon.Visible = false;
                Application.Exit();
            });

            trayIcon = new NotifyIcon
            {
                Text = "NVCP Profile Manager",
                Icon = this.Icon,
                ContextMenuStrip = trayMenu,
                Visible = false
            };

            trayIcon.DoubleClick += (s, e) =>
            {
                this.Show();
                this.WindowState = FormWindowState.Normal;
                trayIcon.Visible = false;
            };
        }

        #endregion

        #region Designer Code

        private void InitializeComponent()
        {
            this.Text = "NVCP Profile Manager";
            this.ClientSize = new System.Drawing.Size(650, 700);
            // Make window sizable.
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimumSize = new System.Drawing.Size(650, 700);
            this.MaximizeBox = true;
            // --- Manual Controls ---
            FormsLabel lblManual = new FormsLabel { Text = "Manual Settings", Left = 20, Top = 20, AutoSize = true, Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold) };
            this.Controls.Add(lblManual);
            FormsLabel lblVibrance = new FormsLabel { Text = "Vibrance (0–100):", Left = 20, Top = 60, AutoSize = true };
            nudVibrance = new NumericUpDown { Left = 160, Top = 60, Minimum = 0, Maximum = 100, Width = 80 };
            this.Controls.Add(lblVibrance);
            this.Controls.Add(nudVibrance);
            trackBarVibrance = new TrackBar { Left = 250, Top = 60, Width = 200, TickStyle = TickStyle.None };
            this.Controls.Add(trackBarVibrance);
            FormsLabel lblHue = new FormsLabel { Text = "Hue (0–359):", Left = 20, Top = 100, AutoSize = true };
            nudHue = new NumericUpDown { Left = 160, Top = 100, Minimum = 0, Maximum = 359, Width = 80 };
            this.Controls.Add(lblHue);
            this.Controls.Add(nudHue);
            trackBarHue = new TrackBar { Left = 250, Top = 100, Width = 200, TickStyle = TickStyle.None };
            this.Controls.Add(trackBarHue);
            FormsLabel lblBrightness = new FormsLabel { Text = "Brightness (0–100%):", Left = 20, Top = 140, AutoSize = true };
            nudBrightness = new NumericUpDown { Left = 160, Top = 140, Minimum = 0, Maximum = 100, DecimalPlaces = 0, Width = 80 };
            this.Controls.Add(lblBrightness);
            this.Controls.Add(nudBrightness);
            trackBarBrightness = new TrackBar { Left = 250, Top = 140, Width = 200, TickStyle = TickStyle.None };
            this.Controls.Add(trackBarBrightness);
            FormsLabel lblContrast = new FormsLabel { Text = "Contrast (0–100%):", Left = 20, Top = 180, AutoSize = true };
            nudContrast = new NumericUpDown { Left = 160, Top = 180, Minimum = 0, Maximum = 100, DecimalPlaces = 0, Width = 80 };
            this.Controls.Add(lblContrast);
            this.Controls.Add(nudContrast);
            trackBarContrast = new TrackBar { Left = 250, Top = 180, Width = 200, TickStyle = TickStyle.None };
            this.Controls.Add(trackBarContrast);
            FormsLabel lblGamma = new FormsLabel { Text = "Gamma (0.30–2.80):", Left = 20, Top = 220, AutoSize = true };
            nudGamma = new NumericUpDown { Left = 160, Top = 220, Minimum = 0.30M, Maximum = 2.80M, DecimalPlaces = 2, Increment = 0.1M, Width = 80 };
            this.Controls.Add(lblGamma);
            this.Controls.Add(nudGamma);
            trackBarGamma = new TrackBar { Left = 250, Top = 220, Width = 200, TickStyle = TickStyle.None };
            this.Controls.Add(trackBarGamma);
            btnApplyManual = new Button { Text = "Apply Manual Settings", Left = 20, Top = 280, Width = 200 };
            btnApplyManual.Click += btnApplyManual_Click;
            this.Controls.Add(btnApplyManual);
            btnReset = new Button { Text = "Reset to Defaults", Left = 240, Top = 280, Width = 150 };
            btnReset.Click += btnReset_Click;
            this.Controls.Add(btnReset);
            // Add the new Save Changes button.
            btnSaveChanges = new Button { Text = "Save Changes", Left = 400, Top = 280, Width = 150 };
            btnSaveChanges.Click += btnSaveChanges_Click;
            this.Controls.Add(btnSaveChanges);
            // Add new Set Defaults button.
            btnSetDefault = new Button { Text = "Set as Default", Left = 480, Top = 50, Width = 150 };
            btnSetDefault.Click += btnSetDefault_Click;
            this.Controls.Add(btnSetDefault);
            // --- Profile Management Controls ---
            FormsLabel lblProfiles = new FormsLabel { Text = "Profiles", Left = 20, Top = 310, AutoSize = true, Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold) };
            this.Controls.Add(lblProfiles);
            lstProfiles = new ListBox { Left = 20, Top = 340, Width = 350, Height = 100 };
            this.Controls.Add(lstProfiles);
            btnAddProfile = new Button { Text = "Add Profile", Left = 380, Top = 340, Width = 150 };
            btnAddProfile.Click += btnAddProfile_Click;
            this.Controls.Add(btnAddProfile);
            btnEditProfile = new Button { Text = "Edit Profile", Left = 380, Top = 380, Width = 150 };
            btnEditProfile.Click += btnEditProfile_Click;
            this.Controls.Add(btnEditProfile);
            btnRemoveProfile = new Button { Text = "Remove Profile", Left = 380, Top = 420, Width = 150 };
            btnRemoveProfile.Click += btnRemoveProfile_Click;
            this.Controls.Add(btnRemoveProfile);
            btnApplyProfile = new Button { Text = "Apply Profile", Left = 380, Top = 460, Width = 150 };
            btnApplyProfile.Click += btnApplyProfile_Click;
            this.Controls.Add(btnApplyProfile);
            chkAutoSwitch = new CheckBox { Text = "Enable Auto Profile Switching", Left = 20, Top = 460, AutoSize = true };
            chkAutoSwitch.CheckedChanged += chkAutoSwitch_CheckedChanged;
            this.Controls.Add(chkAutoSwitch);
            chkAutoStart = new CheckBox { Text = "Run at Startup", Left = 20, Top = 490, AutoSize = true };
            chkAutoStart.CheckedChanged += chkAutoStart_CheckedChanged;
            this.Controls.Add(chkAutoStart);
            // --- Resolution Changer Controls ---
            FormsLabel lblResolution = new FormsLabel { Text = "Resolution Changer", Left = 20, Top = 530, AutoSize = true, Font = new System.Drawing.Font("Segoe UI", 10, System.Drawing.FontStyle.Bold) };
            this.Controls.Add(lblResolution);
            cmbResolutions = new ComboBox { Left = 20, Top = 560, Width = 350, DropDownStyle = ComboBoxStyle.DropDownList };
            this.Controls.Add(cmbResolutions);
            btnApplyResolution = new Button { Text = "Apply Resolution", Left = 380, Top = 560, Width = 150 };
            btnApplyResolution.Click += btnApplyResolution_Click;
            this.Controls.Add(btnApplyResolution);
            btnResetResolution = new Button { Text = "Reset Resolution", Left = 380, Top = 600, Width = 150 };
            btnResetResolution.Click += btnResetResolution_Click;
            this.Controls.Add(btnResetResolution);
            // New auto-confirm resolution checkbox.
            FormsLabel lblAutoConfirm = new FormsLabel { Text = "Auto Confirm Resolution Changes:", Left = 20, Top = 510, AutoSize = true };
            chkAutoConfirmResolution = new CheckBox { Left = 240, Top = 510, Width = 20 };
            this.Controls.Add(lblAutoConfirm);
            this.Controls.Add(chkAutoConfirmResolution);
            // --- Status ---
            lblStatus = new FormsLabel { Text = "Status", Left = 20, Top = 600, AutoSize = false, BorderStyle = BorderStyle.FixedSingle, Width = 350, Height = 50 };
            this.Controls.Add(lblStatus);
        }

        #endregion
    }
}
