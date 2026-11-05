using System.Collections.Immutable;
using ClipBench.Core.Hotkeys;
using ClipBench.Presentation.Popup;
using ClipBench.Presentation.Preferences;
using ClipBench.Presentation.Snippets;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;

namespace ClipBench.App;

/// <summary>
/// The Settings window. It holds no logic of its own: clicks and keys are handed to the view models, which the tests
/// cover. The number boxes and the separator list are kept in step by hand because their controls do not bind to whole
/// numbers and choices. Closing it only hides it, so the next open is quick.
/// </summary>
public sealed partial class SettingsWindow : Window
{
    private const double WidthDip = 940;
    private const double HeightDip = 700;
    private const string CustomLabel = "Custom";

    private readonly Dictionary<SettingsSection, FrameworkElement> _panels;
    private bool _closing;
    private bool _sized;
    private bool _syncing;

    /// <summary>Creates the window, hidden.</summary>
    /// <param name="viewModel">What the window shows.</param>
    public SettingsWindow(SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();

        _panels = new Dictionary<SettingsSection, FrameworkElement>
        {
            [SettingsSection.General] = GeneralPanel,
            [SettingsSection.Hotkeys] = HotkeysPanel,
            [SettingsSection.History] = HistoryPanel,
            [SettingsSection.Privacy] = PrivacyPanel,
            [SettingsSection.Paste] = PastePanel,
            [SettingsSection.Snippets] = SnippetsPanel,
            [SettingsSection.About] = AboutPanel,
        };

        ConfigureNumberBox(MaxCountBox, HistorySettingsViewModel.MaxCountRange);
        ConfigureNumberBox(MaxAgeBox, HistorySettingsViewModel.MaxAgeDaysRange);
        ConfigureNumberBox(MaxMegabytesBox, HistorySettingsViewModel.MaxMegabytesRange);
        ConfigureNumberBox(SensitiveExpiryBox, PrivacySettingsViewModel.SensitiveExpirySecondsRange);

        foreach (var option in PasteSettingsViewModel.SeparatorOptions)
        {
            SeparatorBox.Items.Add(option.Label);
        }

        SeparatorBox.Items.Add(CustomLabel);

        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.SelectedSection))
            {
                ApplySelection();
            }
        };
        ViewModel.History.PropertyChanged += (_, _) => SyncControls();
        ViewModel.Privacy.PropertyChanged += (_, _) => SyncControls();
        ViewModel.Paste.PropertyChanged += (_, _) => SyncControls();
        ViewModel.About.OpenRepositoryRequested += OnOpenRepositoryRequested;

        AppWindow.Closing += OnClosing;
        ApplySelection();
        SyncControls();
    }

    /// <summary>What the window shows.</summary>
    public SettingsViewModel ViewModel { get; }

    /// <summary>The list of sections.</summary>
    public static ImmutableArray<SettingsSectionItem> SectionItems => SettingsViewModel.Sections;

    /// <summary>The short explanation of the snippet variables, shown under the text.</summary>
    public static string SnippetHelp => SnippetEditorViewModel.HelpText;

    /// <summary>Shows the window with a section open.</summary>
    /// <param name="section">The section to open.</param>
    public void ShowSection(SettingsSection section)
    {
        ViewModel.Show(section);
        ResetSnippetsUnlessDirty(section);

        AppWindow.Show();
        Activate();
        if (!_sized)
        {
            // The scale of the screen is known once the window has content on screen.
            _sized = true;
            var scale = Content.XamlRoot?.RasterizationScale ?? 1.0;
            AppWindow.Resize(new SizeInt32((int)Math.Round(WidthDip * scale), (int)Math.Round(HeightDip * scale)));
        }

        SyncControls();
        _ = DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => FocusSection(section));
    }

    /// <summary>Closes the window for good, as the app exits.</summary>
    public void Shutdown()
    {
        _closing = true;
        Close();
    }

    private static void ConfigureNumberBox(NumberBox box, Core.Settings.SettingRange range)
    {
        box.Minimum = range.Minimum;
        box.Maximum = range.Maximum;
        box.ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten;
    }

    private static string? NameOf(VirtualKey key)
    {
        var code = (int)key;
        if (code is >= 'A' and <= 'Z' or >= '0' and <= '9')
        {
            return ((char)code).ToString();
        }

        if (key is >= VirtualKey.F1 and <= VirtualKey.F24)
        {
            return $"F{code - (int)VirtualKey.F1 + 1}";
        }

        return key switch
        {
            VirtualKey.Back => "Backspace",
            VirtualKey.Space => "Space",
            VirtualKey.Enter => "Enter",
            VirtualKey.Tab => "Tab",
            VirtualKey.Insert => "Insert",
            VirtualKey.Delete => "Delete",
            VirtualKey.Home => "Home",
            VirtualKey.End => "End",
            VirtualKey.PageUp => "PageUp",
            VirtualKey.PageDown => "PageDown",
            VirtualKey.Left => "Left",
            VirtualKey.Up => "Up",
            VirtualKey.Right => "Right",
            VirtualKey.Down => "Down",
            _ => null,
        };
    }

    private static bool IsDown(VirtualKey key) => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private static bool IsModifier(VirtualKey key) =>
        key is VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl
            or VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift
            or VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu
            or VirtualKey.LeftWindows or VirtualKey.RightWindows;

    private void ApplySelection()
    {
        foreach (var (section, panel) in _panels)
        {
            panel.Visibility = ViewModel.IsShown(section) ? Visibility.Visible : Visibility.Collapsed;
        }

        var index = SettingsViewModel.Sections.ToList().FindIndex(item => item.Section == ViewModel.SelectedSection);
        if (SectionList.SelectedIndex != index)
        {
            SectionList.SelectedIndex = index;
        }
    }

    private void FocusSection(SettingsSection section)
    {
        Control? target = section switch
        {
            SettingsSection.Snippets => ViewModel.Snippets.IsNew ? (Control)NameBox : SnippetNames,
            _ => SectionList,
        };
        _ = target.Focus(FocusState.Programmatic);
    }

    // Puts the number boxes, the separator list and the custom box where the view models say they are.
    private void SyncControls()
    {
        _syncing = true;
        try
        {
            SetNumber(MaxCountBox, ViewModel.History.MaxCount);
            SetNumber(MaxAgeBox, ViewModel.History.MaxAgeDays);
            SetNumber(MaxMegabytesBox, ViewModel.History.MaxMegabytes);
            SetNumber(SensitiveExpiryBox, ViewModel.Privacy.SensitiveExpirySeconds);

            var presets = PasteSettingsViewModel.SeparatorOptions;
            var selected = ViewModel.Paste.IsCustomSelected
                ? presets.Count
                : presets.ToList().FindIndex(option => option.Choice == ViewModel.Paste.SelectedSeparator);
            if (SeparatorBox.SelectedIndex != selected)
            {
                SeparatorBox.SelectedIndex = selected;
            }

            if (CustomSeparatorBox.Text != ViewModel.Paste.CustomSeparator && CustomSeparatorBox.FocusState == FocusState.Unfocused)
            {
                CustomSeparatorBox.Text = ViewModel.Paste.CustomSeparator;
            }
        }
        finally
        {
            _syncing = false;
        }
    }

    private static void SetNumber(NumberBox box, int value)
    {
        if (!double.IsNaN(box.Value) && (int)box.Value == value)
        {
            return;
        }

        box.Value = value;
    }

    private void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_closing)
        {
            // Hidden, not closed: unsaved snippet changes are dropped the next time the snippets are shown.
            args.Cancel = true;
            sender.Hide();
        }
    }

    private void OnOpenRepositoryRequested(object? sender, OpenAddressEventArgs e)
    {
        // The address is one we wrote ourselves. The user's own browser opens it; ClipBench makes no connection.
        if (Uri.TryCreate(e.Url, UriKind.Absolute, out var address) && address.Scheme == Uri.UriSchemeHttps)
        {
            _ = Launcher.LaunchUriAsync(address);
        }
    }

    // Opening the snippets starts from the first one, but never throws away text the user has not saved.
    private void ResetSnippetsUnlessDirty(SettingsSection section)
    {
        if (section == SettingsSection.Snippets && !ViewModel.Snippets.IsDirty)
        {
            ViewModel.Snippets.Reset();
        }
    }

    private void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SectionList.SelectedItem is SettingsSectionItem item && ViewModel.SelectedSection != item.Section)
        {
            ViewModel.Show(item.Section);
            ResetSnippetsUnlessDirty(item.Section);
        }
    }

    private void MaxCountBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        Apply(args, value => ViewModel.History.MaxCount = value);

    private void MaxAgeBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        Apply(args, value => ViewModel.History.MaxAgeDays = value);

    private void MaxMegabytesBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        Apply(args, value => ViewModel.History.MaxMegabytes = value);

    private void SensitiveExpiryBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
        Apply(args, value => ViewModel.Privacy.SensitiveExpirySeconds = value);

    private void Apply(NumberBoxValueChangedEventArgs args, Action<int> set)
    {
        if (_syncing || double.IsNaN(args.NewValue))
        {
            return;
        }

        set((int)Math.Round(args.NewValue));
    }

    private void SeparatorBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var presets = PasteSettingsViewModel.SeparatorOptions;
        var index = SeparatorBox.SelectedIndex;
        if (_syncing || index < 0 || index >= presets.Count)
        {
            // The last entry only shows that a custom separator is in use; it is chosen with the box below.
            return;
        }

        ViewModel.Paste.SelectedSeparator = presets[index].Choice;
    }

    private void ApplyCustomSeparator_Click(object sender, RoutedEventArgs e) =>
        ViewModel.Paste.ApplyCustomSeparatorCommand.Execute(CustomSeparatorBox.Text);

    private void CustomSeparatorBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ViewModel.Paste.ApplyCustomSeparatorCommand.Execute(CustomSeparatorBox.Text);
            e.Handled = true;
        }
    }

    private void RemoveProgram_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string program })
        {
            ViewModel.Privacy.RemoveProgramCommand.Execute(program);
        }
    }

    private void NewProgramBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ViewModel.Privacy.AddProgramCommand.Execute(null);
            e.Handled = true;
        }
    }

    private async void PopupHotkeyBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e) =>
        await CaptureAsync(HotkeyAction.OpenPopup, e);

    private async void CollectHotkeyBox_PreviewKeyDown(object sender, KeyRoutedEventArgs e) =>
        await CaptureAsync(HotkeyAction.ToggleCollect, e);

    // Turns the keys pressed in a shortcut box into the text a person would type, and lets the view model judge it.
    private async Task CaptureAsync(HotkeyAction action, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape || e.Key == VirtualKey.Tab)
        {
            // These keep their jobs: closing the window and moving on.
            return;
        }

        e.Handled = true;
        if (IsModifier(e.Key))
        {
            return;
        }

        var parts = new List<string>();
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows))
        {
            parts.Add("Win");
        }

        if (IsDown(VirtualKey.Control))
        {
            parts.Add("Ctrl");
        }

        if (IsDown(VirtualKey.Menu))
        {
            parts.Add("Alt");
        }

        if (IsDown(VirtualKey.Shift))
        {
            parts.Add("Shift");
        }

        // A key the shortcuts cannot use is passed on as it is, so the view model says what is wrong.
        parts.Add(NameOf(e.Key) ?? e.Key.ToString());
        _ = await ViewModel.Hotkeys.SetAsync(action, string.Join('+', parts), CancellationToken.None);
    }

    private void Frame_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var control = IsDown(VirtualKey.Control);
        var snippets = ViewModel.IsShown(SettingsSection.Snippets);
        switch (e.Key)
        {
            case VirtualKey.S when control && snippets:
                ViewModel.Snippets.SaveCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.N when control && snippets:
                ViewModel.Snippets.NewCommand.Execute(null);
                _ = NameBox.Focus(FocusState.Programmatic);
                e.Handled = true;
                break;
            case VirtualKey.Delete when control && snippets:
                ViewModel.Snippets.DeleteCommand.Execute(null);
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                // Backs out of one thing at a time: a question, then unsaved changes, then the window.
                if (ViewModel.Privacy.IsConfirmingWipe)
                {
                    ViewModel.Privacy.CancelWipeCommand.Execute(null);
                }
                else if (snippets && ViewModel.Snippets.IsConfirmingDelete)
                {
                    ViewModel.Snippets.CancelDeleteCommand.Execute(null);
                }
                else if (snippets && ViewModel.Snippets.IsDirty)
                {
                    ViewModel.Snippets.DiscardCommand.Execute(null);
                }
                else
                {
                    AppWindow.Hide();
                }

                e.Handled = true;
                break;
            default:
                break;
        }
    }
}
