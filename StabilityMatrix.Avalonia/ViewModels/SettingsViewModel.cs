﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.Notifications;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluentAvalonia.UI.Controls;
using NLog;
using StabilityMatrix.Avalonia.Models;
using StabilityMatrix.Avalonia.Services;
using StabilityMatrix.Avalonia.Views;
using StabilityMatrix.Core.Attributes;
using StabilityMatrix.Core.Extensions;
using StabilityMatrix.Core.Helper;
using StabilityMatrix.Core.Models;
using StabilityMatrix.Core.Python;
using StabilityMatrix.Core.Services;
using Symbol = FluentIcons.Common.Symbol;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace StabilityMatrix.Avalonia.ViewModels;

[View(typeof(SettingsPage))]
public partial class SettingsViewModel : PageViewModelBase
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
    
    private readonly INotificationService notificationService;
    private readonly ISettingsManager settingsManager;
    private readonly IPrerequisiteHelper prerequisiteHelper;
    private readonly IPyRunner pyRunner;
    public SharedState SharedState { get; }
    
    public override string Title => "Settings";
    public override IconSource IconSource => new SymbolIconSource {Symbol = Symbol.Settings, IsFilled = true};
    
    // ReSharper disable once MemberCanBeMadeStatic.Global
    public string AppVersion => $"Version {Compat.AppVersion}";
    
    // Theme section
    [ObservableProperty] private string? selectedTheme;
    
    public IReadOnlyList<string> AvailableThemes { get; } = new[]
    {
        "Light",
        "Dark",
        "System",
    };
    
    // Shared folder options
    [ObservableProperty] private bool removeSymlinksOnShutdown;
    
    // Debug section
    [ObservableProperty] private string? debugPaths;
    [ObservableProperty] private string? debugCompatInfo;
    [ObservableProperty] private string? debugGpuInfo;
    
    // Info section
    private const int VersionTapCountThreshold = 7;
    [ObservableProperty, NotifyPropertyChangedFor(nameof(VersionFlyoutText))] private int versionTapCount;
    [ObservableProperty] private bool isVersionTapTeachingTipOpen;
    public string VersionFlyoutText => $"You are {VersionTapCountThreshold - VersionTapCount} clicks away from enabling Debug options.";
    
    public SettingsViewModel(
        INotificationService notificationService, 
        ISettingsManager settingsManager,
        IPrerequisiteHelper prerequisiteHelper,
        IPyRunner pyRunner,
        SharedState sharedState)
    {
        this.notificationService = notificationService;
        this.settingsManager = settingsManager;
        this.prerequisiteHelper = prerequisiteHelper;
        this.pyRunner = pyRunner;
        SharedState = sharedState;

        SelectedTheme = AvailableThemes[1];
        RemoveSymlinksOnShutdown = settingsManager.Settings.RemoveFolderLinksOnShutdown;
    }
    
    partial void OnSelectedThemeChanged(string? value)
    {
        // In case design / tests
        if (Application.Current is null) return;
        // Change theme
        Application.Current.RequestedThemeVariant = value switch
        {
            "Dark" => ThemeVariant.Dark,
            "Light" => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
    }
    
    partial void OnRemoveSymlinksOnShutdownChanged(bool value)
    {
        settingsManager.Transaction(s => s.RemoveFolderLinksOnShutdown = value);
    }

    [RelayCommand]
    private async Task CheckPythonVersion()
    {
        var isInstalled = prerequisiteHelper.IsPythonInstalled;
        Logger.Debug($"Check python installed: {isInstalled}");
        // Ensure python installed
        if (!prerequisiteHelper.IsPythonInstalled)
        {
            // Need 7z as well for site packages repack
            Logger.Debug("Python not installed, unpacking resources...");
            await prerequisiteHelper.UnpackResourcesIfNecessary();
            Logger.Debug("Unpacked resources, installing python...");
            await prerequisiteHelper.InstallPythonIfNecessary();
        }

        // Get python version
        await pyRunner.Initialize();
        var result = await pyRunner.GetVersionInfo();
        // Show dialog box
        var dialog = new ContentDialog
        {
            Title = "Python version info",
            Content = result,
            PrimaryButtonText = "Ok",
            IsPrimaryButtonEnabled = true
        };
        dialog.Title = "Python version info";
        dialog.Content = result;
        dialog.PrimaryButtonText = "Ok";
        await dialog.ShowAsync();
    }

    #region Debug Section
    public void LoadDebugInfo()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        DebugPaths = $"""
                      Current Working Directory [Environment.CurrentDirectory]
                        "{Environment.CurrentDirectory}"
                      App Directory [Assembly.GetExecutingAssembly().Location]
                        "{assembly.Location}"
                      App Directory [AppContext.BaseDirectory]
                        "{AppContext.BaseDirectory}"
                      AppData Directory [SpecialFolder.ApplicationData]
                        "{appData}"
                      """;
        
        // 1. Check portable mode
        var appDir = Compat.AppCurrentDir;
        var expectedPortableFile = Path.Combine(appDir, "Data", ".sm-portable");
        var isPortableMode = File.Exists(expectedPortableFile);
        
        DebugCompatInfo = $"""
                            Platform: {Compat.Platform}
                            AppData: {Compat.AppData}
                            AppDataHome: {Compat.AppDataHome}
                            AppCurrentDir: {Compat.AppCurrentDir}
                            ExecutableName: {Compat.GetExecutableName()}
                            -- Settings --
                            Expected Portable Marker file: {expectedPortableFile}
                            Portable Marker file exists: {isPortableMode}
                            IsLibraryDirSet = {settingsManager.IsLibraryDirSet}
                            IsPortableMode = {settingsManager.IsPortableMode}
                            """;
        
        // Get Gpu info
        var gpuInfo = "";
        foreach (var (i, gpu) in HardwareHelper.IterGpuInfo().Enumerate())
        {
            gpuInfo += $"[{i+1}] {gpu}\n";
        }
        DebugGpuInfo = gpuInfo;
    }
    
    // Debug buttons
    [RelayCommand]
    private void DebugNotification()
    {
        notificationService.Show(new Notification(
            title: "Test Notification",
            message: "Here is some message",
            type: NotificationType.Information));
    }

    [RelayCommand]
    private async Task DebugContentDialog()
    {
        var dialog = new ContentDialog
        {
            DefaultButton = ContentDialogButton.Primary,
            Title = "Test title",
            PrimaryButtonText = "OK",
            CloseButtonText = "Close"
        };

        var result = await dialog.ShowAsync();
        notificationService.Show(new Notification("Content dialog closed",
            $"Result: {result}"));
    }

    [RelayCommand]
    private void DebugThrowException()
    {
        // Use try-catch to generate traceback information
        throw new OperationCanceledException("Example Message");
    }
    #endregion

    #region Info Section

    public void OnVersionClick()
    {
        // Ignore if already enabled
        if (SharedState.IsDebugMode) return;
        
        VersionTapCount++;
        
        switch (VersionTapCount)
        {
            // Reached required threshold
            case >= VersionTapCountThreshold:
            {
                IsVersionTapTeachingTipOpen = false;
                // Enable debug options
                SharedState.IsDebugMode = true;
                notificationService.Show(
                    "Debug options enabled", "Warning: Improper use may corrupt application state or cause loss of data.");
                VersionTapCount = 0;
                break;
            }
            // Open teaching tip above 3rd click
            case >= 3:
                IsVersionTapTeachingTipOpen = true;
                break;
        }
    }

    [RelayCommand]
    private async Task ShowLicensesDialog()
    {
        try
        {
            var markdown = GetLicensesMarkdown();

            var dialog = DialogHelper.CreateMarkdownDialog(markdown, "Licenses");
            dialog.MaxDialogHeight = 600;
            await dialog.ShowAsync();
        }
        catch (Exception e)
        {
            notificationService.Show("Failed to read licenses information", 
                $"{e}", NotificationType.Error);
        }
    }

    private static string GetLicensesMarkdown()
    {
        // Read licenses.json
        using var reader = new StreamReader(Assets.LicensesJson.Open());
        var licenses = JsonSerializer
            .Deserialize<IReadOnlyList<LicenseInfo>>(reader.ReadToEnd()) ??
                       throw new InvalidOperationException("Failed to read licenses.json");
        
        // Generate markdown
        var builder = new StringBuilder();
        foreach (var license in licenses)
        {
            builder.AppendLine($"## [{license.PackageName}]({license.PackageUrl}) by {string.Join(", ", license.Authors)}");
            builder.AppendLine();
            builder.AppendLine(license.Description);
            builder.AppendLine();
            builder.AppendLine($"[{license.LicenseUrl}]({license.LicenseUrl})");
            builder.AppendLine();
        }

        return builder.ToString();
    }

    #endregion

}
