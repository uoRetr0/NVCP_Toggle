using System;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Configuration;
using NvAPIWrapper;
using WindowsDisplayAPI;
using Newtonsoft.Json;

namespace NVCP_Toggle
{
    class Program
    {
        // Default settings
        static int DefaultVibrance = 50;
        static int DefaultHue = 0;
        static double DefaultBrightness = 0.50;
        static double DefaultContrast = 0.50;
        static double DefaultGamma = 1.0;
        static DisplayGammaRamp DefaultGammaRamp = new DisplayGammaRamp();

        // Profile management
        static List<DisplayProfile> profiles = new List<DisplayProfile>();
        static DisplayProfile? activeProfile = null;
        static Timer? profileCheckTimer = null;
        static bool isMonitoring = false;

        // Profile class
        class DisplayProfile
        {
            public string? ProfileName { get; set; }
            public string? ProcessName { get; set; }
            public int Vibrance { get; set; }
            public int Hue { get; set; }
            public float Brightness { get; set; }
            public float Contrast { get; set; }
            public float Gamma { get; set; }
        }

        static void Main(string[] args)
        {
            WriteColoredLine("NVCP Profile Manager", ConsoleColor.Cyan);

            // Initialize NVIDIA API
            try { NVIDIA.Initialize(); }
            catch (Exception e)
            {
                WriteColoredLine($"\nERROR: Unable to initialize NVIDIA API\n{e.StackTrace}", ConsoleColor.Red);
                ExitPrompt();
                return;
            }

            // Load configuration and profiles
            IConfigurationRoot config = LoadConfig();
            LoadProfiles();

            // Main menu loop
            while (true)
            {
                Console.Clear();
                WriteColoredLine("NVCP Profile Manager", ConsoleColor.Cyan);
                DisplayToggleStatus();
                WriteColoredLine(new string('-', 50), ConsoleColor.DarkGray);
                Console.WriteLine("0. Reset Display Settings (Default)");
                Console.WriteLine("1. Select Profile & Toggle");
                Console.WriteLine("2. Add New Profile");
                Console.WriteLine("3. Remove Profile");
                Console.WriteLine("4. List Profiles");
                Console.WriteLine("5. Toggle Auto Profile Switching");
                Console.WriteLine("6. Exit");
                Console.Write("\nEnter your choice: ");

                var key = Console.ReadKey(true).KeyChar;
                Console.WriteLine();
                switch (key)
                {
                    case '0':
                        ResetToDefaults();
                        WriteColoredLine("Display reset to default settings.", ConsoleColor.Green);
                        ExitPrompt();
                        break;
                    case '1': SelectAndToggleProfile(); break;
                    case '2': AddProfile(); break;
                    case '3': RemoveProfile(); break;
                    case '4': ListProfiles(); break;
                    case '5': ToggleAutoSwitching(); break;
                    case '6': Environment.Exit(0); break;
                    default:
                        WriteColoredLine("Invalid option. Please try again.", ConsoleColor.Yellow);
                        Thread.Sleep(1000);
                        break;
                }
            }
        }

        // Helper method for colored output
        static void WriteColoredLine(string text, ConsoleColor color)
        {
            var prevColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = prevColor;
        }

        static void DisplayToggleStatus()
        {
            var nvDisplay = GetNvidiaMainDisplay();
            var windowsDisplay = GetWindowsDisplay();

            WriteColoredLine("Current Status:", ConsoleColor.Green);
            WriteColoredLine(new string('-', 20), ConsoleColor.DarkGray);

            if (nvDisplay == null || windowsDisplay == null)
            {
                WriteColoredLine("Error: No display detected!", ConsoleColor.Red);
                return;
            }

            Console.WriteLine($"Digital Vibrance: {nvDisplay.DigitalVibranceControl.CurrentLevel} (Default: {DefaultVibrance})");
            Console.WriteLine($"Hue Angle: {nvDisplay.HUEControl.CurrentAngle}° (Default: {DefaultHue}°)");
            string gammaState = HasDefaultGammaRamp(windowsDisplay) ? "Default" : "Custom";
            Console.WriteLine($"Gamma State: {gammaState}");
            Console.WriteLine($"Auto Profile Switching: {(isMonitoring ? "Enabled" : "Disabled")}");
            if (activeProfile != null)
            {
                Console.WriteLine($"Active Profile: {activeProfile.ProfileName}");
            }
        }

        // New method: list profiles and let user select one to toggle
        static void SelectAndToggleProfile()
        {
            if (profiles.Count == 0)
            {
                WriteColoredLine("No profiles available. Please add a profile first.", ConsoleColor.Yellow);
                ExitPrompt();
                return;
            }

            // List profiles without requiring "Press any key"
            WriteColoredLine("\nSaved Profiles:", ConsoleColor.Cyan);
            for (int i = 0; i < profiles.Count; i++)
            {
                Console.WriteLine($"{i + 1}. {profiles[i].ProfileName} ({profiles[i].ProcessName}.exe)");
            }

            Console.Write("\nEnter profile number to toggle (or 0 to cancel): ");
            if (int.TryParse(Console.ReadLine(), out int selection))
            {
                if (selection == 0)
                {
                    WriteColoredLine("Profile selection canceled.", ConsoleColor.Yellow);
                }
                else if (selection > 0 && selection <= profiles.Count)
                {
                    var selectedProfile = profiles[selection - 1];
                    // If the selected profile is already active, reset to defaults.
                    if (activeProfile == selectedProfile)
                    {
                        ResetToDefaults();
                        activeProfile = null;
                        WriteColoredLine("Profile was active. Reverted to default settings.", ConsoleColor.Green);
                    }
                    else
                    {
                        ApplyProfile(selectedProfile);
                        activeProfile = selectedProfile;
                        WriteColoredLine($"Profile '{selectedProfile.ProfileName}' applied.", ConsoleColor.Green);
                    }
                }
                else
                {
                    WriteColoredLine("Invalid profile selection.", ConsoleColor.Red);
                }
            }
            else
            {
                WriteColoredLine("Invalid input. Please enter a number.", ConsoleColor.Red);
            }

            ExitPrompt();
        }

        static void ToggleAutoSwitching()
        {
            isMonitoring = !isMonitoring;
            WriteColoredLine($"Auto profile switching {(isMonitoring ? "ENABLED" : "DISABLED")}", ConsoleColor.Magenta);
            if (isMonitoring)
                EnableAutoSwitching();
            else
                DisableAutoSwitching();
            Thread.Sleep(1500);
        }

        static void EnableAutoSwitching()
        {
            profileCheckTimer = new Timer(_ => { CheckRunningProcesses(); }, null, 0, 5000);
        }

        static void DisableAutoSwitching()
        {
            profileCheckTimer?.Dispose();
            activeProfile = null;
        }

        static void CheckRunningProcesses()
        {
            foreach (var profile in profiles)
            {
                if (Process.GetProcessesByName(profile.ProcessName ?? "").Length > 0)
                {
                    if (activeProfile != profile)
                    {
                        activeProfile = profile;
                        ApplyProfile(profile);
                        WriteColoredLine($"Auto-applied profile: {profile.ProfileName}", ConsoleColor.Green);
                    }
                    return;
                }
            }
            if (activeProfile != null)
            {
                activeProfile = null;
                ResetToDefaults();
                WriteColoredLine("No matching processes found. Reverted to default settings.", ConsoleColor.Yellow);
            }
        }

        static void ApplyProfile(DisplayProfile profile)
        {
            var nvDisplay = GetNvidiaMainDisplay();
            var windowsDisplay = GetWindowsDisplay();
            if (nvDisplay != null && windowsDisplay != null)
            {
                nvDisplay.DigitalVibranceControl.CurrentLevel = profile.Vibrance;
                nvDisplay.HUEControl.CurrentAngle = profile.Hue;
                windowsDisplay.GammaRamp = new DisplayGammaRamp(
                    profile.Brightness,
                    profile.Contrast,
                    profile.Gamma
                );
            }
        }

        static void AddProfile()
        {
            var newProfile = new DisplayProfile();

            Console.Write("Enter profile name: ");
            newProfile.ProfileName = Console.ReadLine();
            Console.Write("Enter process name (without .exe): ");
            newProfile.ProcessName = Console.ReadLine();
            Console.Write("Enter vibrance (0-100): ");
            if (int.TryParse(Console.ReadLine(), out int vibrance))
                newProfile.Vibrance = vibrance;
            Console.Write("Enter hue (-180 to 180): ");
            if (int.TryParse(Console.ReadLine(), out int hue))
                newProfile.Hue = hue;
            Console.Write("Enter brightness (0.0-1.0): ");
            if (float.TryParse(Console.ReadLine(), out float brightness))
                newProfile.Brightness = brightness;
            Console.Write("Enter contrast (0.0-1.0): ");
            if (float.TryParse(Console.ReadLine(), out float contrast))
                newProfile.Contrast = contrast;
            Console.Write("Enter gamma (0.1-3.0): ");
            if (float.TryParse(Console.ReadLine(), out float gamma))
                newProfile.Gamma = gamma;

            profiles.Add(newProfile);
            SaveProfiles();
            WriteColoredLine("Profile saved!", ConsoleColor.Green);
            Thread.Sleep(1000);
        }

        static void RemoveProfile()
        {
            if (profiles.Count == 0)
            {
                WriteColoredLine("No profiles available to remove.", ConsoleColor.Yellow);
                ExitPrompt();
                return;
            }

            WriteColoredLine("\nSaved Profiles:", ConsoleColor.Cyan);
            for (int i = 0; i < profiles.Count; i++)
            {
                Console.WriteLine($"{i + 1}. {profiles[i].ProfileName} ({profiles[i].ProcessName}.exe)");
            }

            Console.Write("\nEnter profile number to remove (or 0 to cancel): ");
            if (int.TryParse(Console.ReadLine(), out int index))
            {
                if (index == 0)
                {
                    WriteColoredLine("Profile removal canceled.", ConsoleColor.Yellow);
                }
                else if (index > 0 && index <= profiles.Count)
                {
                    profiles.RemoveAt(index - 1);
                    SaveProfiles();
                    WriteColoredLine("Profile removed!", ConsoleColor.Green);
                }
                else
                {
                    WriteColoredLine("Invalid selection!", ConsoleColor.Red);
                }
            }
            else
            {
                WriteColoredLine("Invalid input. Please enter a number.", ConsoleColor.Red);
            }

            Thread.Sleep(1000);
        }

        static void ListProfiles()
        {
            if (profiles.Count == 0)
            {
                WriteColoredLine("No profiles available.", ConsoleColor.Yellow);
            }
            else
            {
                WriteColoredLine("\nSaved Profiles:", ConsoleColor.Cyan);
                for (int i = 0; i < profiles.Count; i++)
                {
                    Console.WriteLine($"{i + 1}. {profiles[i].ProfileName} ({profiles[i].ProcessName}.exe)");
                }
            }

            ExitPrompt();
        }

        static void LoadProfiles()
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

        static void SaveProfiles()
        {
            var json = JsonConvert.SerializeObject(new { Profiles = profiles }, Formatting.Indented);
            File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "profiles.json"), json);
        }

        static bool HasDefaultGammaRamp(Display windowsDisplay)
        {
            var gammaRamp = windowsDisplay.GammaRamp;
            return gammaRamp.Red.SequenceEqual(DefaultGammaRamp.Red) &&
                   gammaRamp.Green.SequenceEqual(DefaultGammaRamp.Green) &&
                   gammaRamp.Blue.SequenceEqual(DefaultGammaRamp.Blue);
        }

        static void ResetToDefaults()
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

        static IConfigurationRoot LoadConfig()
        {
            return new ConfigurationBuilder()
                .SetBasePath(Environment.CurrentDirectory)
                .AddJsonFile("appSettings.json", optional: false)
                .Build();
        }

        static Display? GetWindowsDisplay()
        {
            return Display.GetDisplays().FirstOrDefault(d => d.DisplayScreen.IsPrimary);
        }

        static NvAPIWrapper.Display.Display? GetNvidiaMainDisplay()
        {
            var allDisplays = NvAPIWrapper.Display.Display.GetDisplays();
            var config = NvAPIWrapper.Display.PathInfo.GetDisplaysConfig();
            for (int i = 0; i < config.Length; i++)
            {
                if (config[i].IsGDIPrimary)
                    return allDisplays[i];
            }
            return null;
        }

        static void ExitPrompt()
        {
            WriteColoredLine("\nPress any key to continue...", ConsoleColor.DarkGray);
            Console.ReadKey();
        }
    }
}