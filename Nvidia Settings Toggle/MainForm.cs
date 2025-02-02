using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Extensions.Configuration;
using Microsoft.Win32; // For registry auto-start.
using Newtonsoft.Json;
using NvAPIWrapper;
using NvAPIWrapper.GPU;
using WindowsDisplayAPI;
using WindowsDisplayAPI.DisplayConfig;

// Alias for clarity.
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
        private NumericUpDown nudVibrance = null!;
        private TrackBar trackBarVibrance = null!;
        private NumericUpDown nudHue = null!;
        private TrackBar trackBarHue = null!;
        private NumericUpDown nudBrightness = null!;
        private TrackBar trackBarBrightness = null!;
        private NumericUpDown nudContrast = null!;
        private TrackBar trackBarContrast = null!;
        private NumericUpDown nudGamma = null!;
        private TrackBar trackBarGamma = null!;
        private Button btnApplyManual = null!;
        private Button btnReset = null!;

        // Profile management.
        private ListBox lstProfiles = null!;
        private Button btnAddProfile = null!;
        private Button btnEditProfile = null!;
        private Button btnRemoveProfile = null!;
        private Button btnApplyProfile = null!;
        private CheckBox chkAutoSwitch = null!;
        private CheckBox chkAutoStart = null!;
        private FormsLabel lblStatus = null!;

        // Resolution changer.
        private ComboBox cmbResolutions = null!;
        private Button btnApplyResolution = null!;
        private Button btnResetResolution = null!;

        // Tray icon.
        private NotifyIcon trayIcon = null!;
        private ContextMenuStrip trayMenu = null!;

        // Registry key info.
        private const string AutoStartRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "NVCP_Toggle";

        #endregion

        public MainForm()
        {
            InitializeComponent();
            Load += MainForm_Load;
            this.Resize += MainForm_Resize;
        }

        #region Form Load and Initialization

        private void MainForm_Load(object sender, EventArgs e)
        {
            // Allow resizing.
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimumSize = new Size(700, 700);

            // Save the current resolution as default.
            DEVMODE dm = new DEVMODE();
            dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
            if (EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm))
            {
                defaultWidth = dm.dmPelsWidth;
                defaultHeight = dm.dmPelsHeight;
                defaultFrequency = dm.dmDisplayFrequency;
                defaultBpp = dm.dmBitsPerPel;
            }

            // Initialize NVIDIA API.
            try { NVIDIA.Initialize(); }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize NVIDIA API:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                btnApplyManual.Enabled = false;
                return;
            }

            LoadProfiles();
            UpdateProfileList();

            profileCheckTimer.Interval = 5000;
            profileCheckTimer.Tick += (s, ev) => { CheckRunningProcesses(); };

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
                chkAutoStart.Checked = config.GetValue<bool>("autoStart", false);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading settings:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            SetAutoStart(chkAutoStart.Checked);

            PopulateResolutions();
            SetupTrayIcon();
            ApplyDarkTheme();
            UpdateStatusDisplay();
            SetupSliderSync();
        }

        private void SetAutoStart(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, true))
                {
                    if (enable)
                        key.SetValue(AppName, Application.ExecutablePath);
                    else
                        key.DeleteValue(AppName, false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to update auto start setting:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        #endregion

        #region UI Event Handlers

        private void btnApplyManual_Click(object sender, EventArgs e)
        {
            ApplyManualSettings((int)nudVibrance.Value, (int)nudHue.Value, (float)nudBrightness.Value, (float)nudContrast.Value, (float)nudGamma.Value);
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

        private void chkAutoStart_CheckedChanged(object sender, EventArgs e)
        {
            SetAutoStart(chkAutoStart.Checked);
            SaveManualSettings();
        }

        private void btnApplyResolution_Click(object sender, EventArgs e)
        {
            if (cmbResolutions.SelectedItem is ResolutionMode mode)
            {
                int backupWidth = defaultWidth, backupHeight = defaultHeight, backupFreq = defaultFrequency, backupBpp = defaultBpp;
                if (ChangeResolution(mode.Width, mode.Height, mode.Frequency, mode.BitsPerPel) == DISP_CHANGE_SUCCESSFUL)
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
                else
                    MessageBox.Show("Failed to change resolution.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnResetResolution_Click(object sender, EventArgs e)
        {
            if (ChangeResolution(defaultWidth, defaultHeight, defaultFrequency, defaultBpp) == DISP_CHANGE_SUCCESSFUL)
                MessageBox.Show("Resolution reset to default.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                MessageBox.Show("Failed to reset resolution.", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void MainForm_Resize(object sender, EventArgs e)
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
                if (profile.ResolutionWidth != 0 && profile.ResolutionHeight != 0 &&
                    profile.ResolutionFrequency != 0 && profile.ResolutionBpp != 0)
                {
                    int backupWidth = defaultWidth, backupHeight = defaultHeight, backupFreq = defaultFrequency, backupBpp = defaultBpp;
                    if (ChangeResolution(profile.ResolutionWidth, profile.ResolutionHeight, profile.ResolutionFrequency, profile.ResolutionBpp) == DISP_CHANGE_SUCCESSFUL)
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
                            }
                            else
                            {
                                ChangeResolution(backupWidth, backupHeight, backupFreq, backupBpp);
                                MessageBox.Show("Resolution change canceled. Reverted.", "Reverted", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                windowsDisplay.GammaRamp = new DisplayGammaRamp(DefaultBrightness, DefaultContrast, DefaultGamma);
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
                        this.Invoke((MethodInvoker)(() => UpdateStatusDisplay()));
                    }
                    return;
                }
            }
            if (activeProfile != null)
            {
                activeProfile = null;
                ResetToDefaults();
                this.Invoke((MethodInvoker)(() => UpdateStatusDisplay()));
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
            var profilePath = Path.Combine(Environment.CurrentDirectory, "profiles.json");
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
                autoSwitch = chkAutoSwitch.Checked,
                autoStart = chkAutoStart.Checked
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
            this.SuspendLayout();
            // Main form properties.
            this.Text = "NVCP Profile Manager";
            this.ClientSize = new Size(700, 700);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimumSize = new Size(700, 700);
            this.MaximizeBox = true;
            this.BackColor = Color.FromArgb(45, 45, 48);
            this.ForeColor = Color.White;

            // Main TableLayoutPanel.
            TableLayoutPanel mainPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                RowCount = 4,
                ColumnCount = 1,
                Padding = new Padding(10),
                AutoScroll = true
            };
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            mainPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            this.Controls.Add(mainPanel);

            // --- Manual Settings Group ---
            GroupBox grpManual = new GroupBox
            {
                Text = "Manual Settings",
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White,
                Padding = new Padding(10)
            };
            TableLayoutPanel tblManual = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                AutoSize = true,
                Padding = new Padding(5)
            };
            tblManual.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            tblManual.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            tblManual.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

            // Manual settings controls.
            var lblManVibrance = CreateLabel("Vibrance (0–100):");
            nudVibrance = CreateNumericUpDown(0, 100, DefaultVibrance);
            trackBarVibrance = CreateTrackBar(0, 100, DefaultVibrance);
            var lblManHue = CreateLabel("Hue (–180 to 180):");
            nudHue = CreateNumericUpDown(-180, 180, DefaultHue);
            trackBarHue = CreateTrackBar(-180, 180, DefaultHue);
            var lblManBrightness = CreateLabel("Brightness (0.0–2.0):");
            nudBrightness = CreateNumericUpDown(0, 2, (int)(DefaultBrightness * 100));
            trackBarBrightness = CreateTrackBar(0, 200, (int)(DefaultBrightness * 100));
            var lblManContrast = CreateLabel("Contrast (0.0–2.0):");
            nudContrast = CreateNumericUpDown(0, 2, (int)(DefaultContrast * 100));
            trackBarContrast = CreateTrackBar(0, 200, (int)(DefaultContrast * 100));
            var lblManGamma = CreateLabel("Gamma (0.0–3.0):");
            nudGamma = CreateNumericUpDown(0, 3, (int)(DefaultGamma * 100));
            trackBarGamma = CreateTrackBar(0, 300, (int)(DefaultGamma * 100));

            tblManual.Controls.Add(lblManVibrance, 0, 0);
            tblManual.Controls.Add(nudVibrance, 1, 0);
            tblManual.Controls.Add(trackBarVibrance, 2, 0);

            tblManual.Controls.Add(lblManHue, 0, 1);
            tblManual.Controls.Add(nudHue, 1, 1);
            tblManual.Controls.Add(trackBarHue, 2, 1);

            tblManual.Controls.Add(lblManBrightness, 0, 2);
            tblManual.Controls.Add(nudBrightness, 1, 2);
            tblManual.Controls.Add(trackBarBrightness, 2, 2);

            tblManual.Controls.Add(lblManContrast, 0, 3);
            tblManual.Controls.Add(nudContrast, 1, 3);
            tblManual.Controls.Add(trackBarContrast, 2, 3);

            tblManual.Controls.Add(lblManGamma, 0, 4);
            tblManual.Controls.Add(nudGamma, 1, 4);
            tblManual.Controls.Add(trackBarGamma, 2, 4);

            FlowLayoutPanel pnlManualButtons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                Dock = DockStyle.Fill,
                AutoSize = true
            };
            btnApplyManual = CreateButton("Apply Manual Settings");
            btnReset = CreateButton("Reset to Defaults");
            pnlManualButtons.Controls.Add(btnApplyManual);
            pnlManualButtons.Controls.Add(btnReset);
            tblManual.Controls.Add(pnlManualButtons, 0, 5);
            tblManual.SetColumnSpan(pnlManualButtons, 3);
            grpManual.Controls.Add(tblManual);
            mainPanel.Controls.Add(grpManual);

            // --- Profile Management Group ---
            GroupBox grpProfiles = new GroupBox
            {
                Text = "Profile Management",
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White,
                Padding = new Padding(10)
            };
            TableLayoutPanel tblProfiles = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                AutoSize = true,
                Padding = new Padding(5)
            };
            tblProfiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70F));
            tblProfiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            lstProfiles = new ListBox { Dock = DockStyle.Fill, Height = 100 };
            tblProfiles.Controls.Add(lstProfiles, 0, 0);
            FlowLayoutPanel pnlProfileButtons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill, AutoSize = true };
            btnAddProfile = CreateButton("Add Profile");
            btnEditProfile = CreateButton("Edit Profile");
            btnRemoveProfile = CreateButton("Remove Profile");
            btnApplyProfile = CreateButton("Apply Profile");
            pnlProfileButtons.Controls.Add(btnAddProfile);
            pnlProfileButtons.Controls.Add(btnEditProfile);
            pnlProfileButtons.Controls.Add(btnRemoveProfile);
            pnlProfileButtons.Controls.Add(btnApplyProfile);
            tblProfiles.Controls.Add(pnlProfileButtons, 1, 0);
            FlowLayoutPanel pnlProfileOptions = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, Dock = DockStyle.Fill, AutoSize = true };
            chkAutoSwitch = new CheckBox { Text = "Enable Auto Profile Switching", AutoSize = true };
            chkAutoStart = new CheckBox { Text = "Run at Startup", AutoSize = true };
            pnlProfileOptions.Controls.Add(chkAutoSwitch);
            pnlProfileOptions.Controls.Add(chkAutoStart);
            tblProfiles.Controls.Add(pnlProfileOptions, 0, 1);
            tblProfiles.SetColumnSpan(pnlProfileOptions, 2);
            grpProfiles.Controls.Add(tblProfiles);
            mainPanel.Controls.Add(grpProfiles);

            // --- Resolution Changer Group ---
            GroupBox grpResolution = new GroupBox
            {
                Text = "Resolution Changer",
                Dock = DockStyle.Top,
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BackColor = Color.FromArgb(45, 45, 48),
                ForeColor = Color.White,
                Padding = new Padding(10)
            };
            TableLayoutPanel tblResolution = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                AutoSize = true,
                Padding = new Padding(5)
            };
            tblResolution.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70F));
            tblResolution.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
            cmbResolutions = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            tblResolution.Controls.Add(cmbResolutions, 0, 0);
            FlowLayoutPanel pnlResButtons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, Dock = DockStyle.Fill, AutoSize = true };
            btnApplyResolution = CreateButton("Apply Resolution");
            btnResetResolution = CreateButton("Reset Resolution");
            pnlResButtons.Controls.Add(btnApplyResolution);
            pnlResButtons.Controls.Add(btnResetResolution);
            tblResolution.Controls.Add(pnlResButtons, 1, 0);
            grpResolution.Controls.Add(tblResolution);
            mainPanel.Controls.Add(grpResolution);

            // --- Status Label ---
            lblStatus = new Label
            {
                Text = "Status",
                Dock = DockStyle.Fill,
                Height = 50,
                BorderStyle = BorderStyle.FixedSingle,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(5)
            };
            mainPanel.Controls.Add(lblStatus);

            // Wire up events.
            btnApplyManual.Click += btnApplyManual_Click;
            btnReset.Click += btnReset_Click;
            btnAddProfile.Click += btnAddProfile_Click;
            btnEditProfile.Click += btnEditProfile_Click;
            btnRemoveProfile.Click += btnRemoveProfile_Click;
            btnApplyProfile.Click += btnApplyProfile_Click;
            chkAutoSwitch.CheckedChanged += chkAutoSwitch_CheckedChanged;
            chkAutoStart.CheckedChanged += chkAutoStart_CheckedChanged;
            btnApplyResolution.Click += btnApplyResolution_Click;
            btnResetResolution.Click += btnResetResolution_Click;

            this.ResumeLayout(false);
        }

        // Helper methods for creating controls.
        private Label CreateLabel(string text)
        {
            return new Label
            {
                Text = text,
                AutoSize = true,
                Dock = DockStyle.Fill,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9)
            };
        }

        private NumericUpDown CreateNumericUpDown(decimal min, decimal max, int initial)
        {
            return new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                Value = initial,
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(63, 63, 70),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9)
            };
        }

        private TrackBar CreateTrackBar(int min, int max, int initial)
        {
            return new TrackBar
            {
                Minimum = min,
                Maximum = max,
                Value = initial,
                Dock = DockStyle.Fill,
                TickStyle = TickStyle.None,
                BackColor = Color.FromArgb(45, 45, 48)
            };
        }

        private Button CreateButton(string text)
        {
            return new Button
            {
                Text = text,
                AutoSize = true,
                BackColor = Color.FromArgb(63, 63, 70),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(3)
            };
        }

        #endregion
    }
}
