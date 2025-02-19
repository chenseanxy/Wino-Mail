﻿using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Web.WebView2.Core;
using Windows.UI.ViewManagement.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Wino.Core.Domain;
using Wino.Core.Domain.Entities.Mail;
using Wino.Core.Domain.Interfaces;
using Wino.Core.Domain.Models;
using Wino.Core.Domain.Models.Reader;
using Wino.Core.UWP.Extensions;
using Wino.Views.Settings;

namespace Wino.Dialogs;

public sealed partial class SignatureEditorDialog : ContentDialog
{
    private Func<Task<string>> _getHTMLBodyFunction;
    private readonly TaskCompletionSource<bool> _domLoadedTask = new TaskCompletionSource<bool>();

    private readonly INativeAppService _nativeAppService = App.Current.Services.GetService<INativeAppService>();
    private readonly IFontService _fontService = App.Current.Services.GetService<IFontService>();
    private readonly IPreferencesService _preferencesService = App.Current.Services.GetService<IPreferencesService>();

    public AccountSignature Result;

    public bool IsComposerDarkMode
    {
        get { return (bool)GetValue(IsComposerDarkModeProperty); }
        set { SetValue(IsComposerDarkModeProperty, value); }
    }

    public static readonly DependencyProperty IsComposerDarkModeProperty = DependencyProperty.Register(nameof(IsComposerDarkMode), typeof(bool), typeof(SignatureManagementPage), new PropertyMetadata(false, OnIsComposerDarkModeChanged));

    public SignatureEditorDialog()
    {
        InitializeComponent();

        SignatureNameTextBox.Header = Translator.SignatureEditorDialog_SignatureName_TitleNew;
        Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "00FFFFFF");
        Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "--enable-features=OverlayScrollbar,msOverlayScrollbarWinStyle,msOverlayScrollbarWinStyleAnimation,FontAccess");

        // TODO: Should be added additional logic to enable/disable primary button when webview content changed.
        IsPrimaryButtonEnabled = true;
    }

    public SignatureEditorDialog(AccountSignature signatureModel)
    {
        InitializeComponent();

        SignatureNameTextBox.Text = signatureModel.Name.Trim();
        SignatureNameTextBox.Header = string.Format(Translator.SignatureEditorDialog_SignatureName_TitleEdit, signatureModel.Name);

        Result = new AccountSignature
        {
            Id = signatureModel.Id,
            Name = signatureModel.Name,
            MailAccountId = signatureModel.MailAccountId,
            HtmlBody = signatureModel.HtmlBody
        };
        Environment.SetEnvironmentVariable("WEBVIEW2_DEFAULT_BACKGROUND_COLOR", "00FFFFFF");
        Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "--enable-features=OverlayScrollbar,msOverlayScrollbarWinStyle,msOverlayScrollbarWinStyleAnimation");

        // TODO: Should be added additional logic to enable/disable primary button when webview content changed.
        IsPrimaryButtonEnabled = true;
    }

    private async void SignatureDialogOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        Chromium.CoreWebView2Initialized -= ChromiumInitialized;
        Chromium.CoreWebView2Initialized += ChromiumInitialized;

        await Chromium.EnsureCoreWebView2Async();

        _getHTMLBodyFunction = new Func<Task<string>>(async () =>
        {
            var editorContent = await Chromium.ExecuteScriptFunctionSafeAsync("GetHTMLContent");

            return JsonSerializer.Deserialize(editorContent, BasicTypesJsonContext.Default.String);
        });

        var underlyingThemeService = App.Current.Services.GetService<IUnderlyingThemeService>();

        IsComposerDarkMode = underlyingThemeService.IsUnderlyingThemeDark();

        await RenderInternalAsync(Result?.HtmlBody ?? string.Empty);
    }

    private void DialogClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        Chromium.CoreWebView2Initialized -= ChromiumInitialized;

        if (Chromium.CoreWebView2 != null)
        {
            Chromium.CoreWebView2.DOMContentLoaded -= DOMLoaded;
            Chromium.CoreWebView2.WebMessageReceived -= ScriptMessageReceived;
        }
    }

    private async void SaveClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var newSignature = Regex.Unescape(await _getHTMLBodyFunction());

        if (Result == null)
        {
            Result = new AccountSignature
            {
                Id = Guid.NewGuid(),
                Name = SignatureNameTextBox.Text.Trim(),
                HtmlBody = newSignature
            };
        }
        else
        {
            Result.Name = SignatureNameTextBox.Text.Trim();
            Result.HtmlBody = newSignature;
        }

        Hide();
    }

    private void CancelClicked(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        Hide();
    }

    private async void BoldButtonClicked(object sender, RoutedEventArgs e)
    {
        await InvokeScriptSafeAsync("editor.execCommand('bold')");
    }

    private async void ItalicButtonClicked(object sender, RoutedEventArgs e)
    {
        await InvokeScriptSafeAsync("editor.execCommand('italic')");
    }

    private async void UnderlineButtonClicked(object sender, RoutedEventArgs e)
    {
        await InvokeScriptSafeAsync("editor.execCommand('underline')");
    }

    private async void StrokeButtonClicked(object sender, RoutedEventArgs e)
    {
        await InvokeScriptSafeAsync("editor.execCommand('strikethrough')");
    }

    private async void BulletListButtonClicked(object sender, RoutedEventArgs e)
    {
        await InvokeScriptSafeAsync("editor.execCommand('insertunorderedlist')");
    }

    private async void OrderedListButtonClicked(object sender, RoutedEventArgs e)
    {
        await InvokeScriptSafeAsync("editor.execCommand('insertorderedlist')");
    }

    private async void IncreaseIndentClicked(object sender, RoutedEventArgs e)
    {
        await InvokeScriptSafeAsync("editor.execCommand('indent')");
    }

    private async void DecreaseIndentClicked(object sender, RoutedEventArgs e)
    {
        await InvokeScriptSafeAsync("editor.execCommand('outdent')");
    }

    private async void AlignmentChanged(object sender, SelectionChangedEventArgs e)
    {
        var selectedItem = AlignmentListView.SelectedItem as ComboBoxItem;
        var alignment = selectedItem.Tag.ToString();

        switch (alignment)
        {
            case "left":
                await InvokeScriptSafeAsync("editor.execCommand('justifyleft')");
                break;
            case "center":
                await InvokeScriptSafeAsync("editor.execCommand('justifycenter')");
                break;
            case "right":
                await InvokeScriptSafeAsync("editor.execCommand('justifyright')");
                break;
            case "justify":
                await InvokeScriptSafeAsync("editor.execCommand('justifyfull')");
                break;
        }
    }

    private async Task<string> InvokeScriptSafeAsync(string function)
    {
        if (Chromium == null) return string.Empty;

        try
        {

            return await Chromium.ExecuteScriptAsync(function);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
        }

        return string.Empty;
    }

    private async void AddImageClicked(object sender, RoutedEventArgs e)
    {
        await InvokeScriptSafeAsync("imageInput.click();");
    }

    private async Task FocusEditorAsync()
    {
        await InvokeScriptSafeAsync("editor.selection.focus();");

        Chromium.Focus(FocusState.Keyboard);
        Chromium.Focus(FocusState.Programmatic);
    }

    private async void EmojiButtonClicked(object sender, RoutedEventArgs e)
    {
        CoreInputView.GetForCurrentView().TryShow(CoreInputViewKind.Emoji);

        await FocusEditorAsync();
    }

    private async Task<string> TryGetSelectedTextAsync()
    {
        try
        {
            return await Chromium.ExecuteScriptAsync("getSelectedText();");
        }
        catch { }

        return string.Empty;
    }

    private async void WebViewToggleButtonClicked(object sender, RoutedEventArgs e)
    {
        var enable = WebviewToolBarButton.IsChecked == true ? "true" : "false";
        await InvokeScriptSafeAsync($"toggleToolbar('{enable}');");
    }

    private async Task UpdateEditorThemeAsync()
    {
        await _domLoadedTask.Task;

        if (IsComposerDarkMode)
        {
            Chromium.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark;
            await InvokeScriptSafeAsync("SetDarkEditor();");
        }
        else
        {
            Chromium.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Light;
            await InvokeScriptSafeAsync("SetLightEditor();");
        }
    }

    private async Task RenderInternalAsync(string htmlBody)
    {
        await _domLoadedTask.Task;

        await UpdateEditorThemeAsync();
        await InitializeEditorAsync();

        if (string.IsNullOrEmpty(htmlBody))
        {
            await Chromium.ExecuteScriptFunctionAsync("RenderHTML", parameters: JsonSerializer.Serialize(" ", BasicTypesJsonContext.Default.String));
        }
        else
        {
            await Chromium.ExecuteScriptFunctionAsync("RenderHTML", parameters: JsonSerializer.Serialize(htmlBody, BasicTypesJsonContext.Default.String));

            await FocusEditorAsync();
        }
    }

    private async Task<string> InitializeEditorAsync()
    {
        var fonts = _fontService.GetFonts();
        var composerFont = _preferencesService.ComposerFont;
        int composerFontSize = _preferencesService.ComposerFontSize;
        var readerFont = _preferencesService.ReaderFont;
        int readerFontSize = _preferencesService.ReaderFontSize;
        return await Chromium.ExecuteScriptFunctionAsync("initializeJodit", false,
            JsonSerializer.Serialize(fonts, BasicTypesJsonContext.Default.ListString),
            JsonSerializer.Serialize(composerFont, BasicTypesJsonContext.Default.String),
            JsonSerializer.Serialize(composerFontSize, BasicTypesJsonContext.Default.Int32),
            JsonSerializer.Serialize(readerFont, BasicTypesJsonContext.Default.String),
            JsonSerializer.Serialize(readerFontSize, BasicTypesJsonContext.Default.Int32));
    }

    private async void ChromiumInitialized(Microsoft.UI.Xaml.Controls.WebView2 sender, Microsoft.UI.Xaml.Controls.CoreWebView2InitializedEventArgs args)
    {
        var editorBundlePath = (await _nativeAppService.GetEditorBundlePathAsync()).Replace("editor.html", string.Empty);

        Chromium.CoreWebView2.SetVirtualHostNameToFolderMapping("app.editor", editorBundlePath, CoreWebView2HostResourceAccessKind.Allow);
        Chromium.Source = new Uri("https://app.editor/editor.html");

        Chromium.CoreWebView2.DOMContentLoaded -= DOMLoaded;
        Chromium.CoreWebView2.DOMContentLoaded += DOMLoaded;

        Chromium.CoreWebView2.WebMessageReceived -= ScriptMessageReceived;
        Chromium.CoreWebView2.WebMessageReceived += ScriptMessageReceived;
    }

    private static async void OnIsComposerDarkModeChanged(DependencyObject obj, DependencyPropertyChangedEventArgs args)
    {
        if (obj is SignatureEditorDialog dialog)
        {
            await dialog.UpdateEditorThemeAsync();
        }
    }

    private void ScriptMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        var change = JsonSerializer.Deserialize(args.WebMessageAsJson, DomainModelsJsonContext.Default.WebViewMessage);

        if (change.Type == "bold")
        {
            BoldButton.IsChecked = change.Value == "true";
        }
        else if (change.Type == "italic")
        {
            ItalicButton.IsChecked = change.Value == "true";
        }
        else if (change.Type == "underline")
        {
            UnderlineButton.IsChecked = change.Value == "true";
        }
        else if (change.Type == "strikethrough")
        {
            StrokeButton.IsChecked = change.Value == "true";
        }
        else if (change.Type == "ol")
        {
            OrderedListButton.IsChecked = change.Value == "true";
        }
        else if (change.Type == "ul")
        {
            BulletListButton.IsChecked = change.Value == "true";
        }
        else if (change.Type == "indent")
        {
            IncreaseIndentButton.IsEnabled = change.Value == "disabled" ? false : true;
        }
        else if (change.Type == "outdent")
        {
            DecreaseIndentButton.IsEnabled = change.Value == "disabled" ? false : true;
        }
        else if (change.Type == "alignment")
        {
            var parsedValue = change.Value switch
            {
                "jodit-icon_left" => 0,
                "jodit-icon_center" => 1,
                "jodit-icon_right" => 2,
                "jodit-icon_justify" => 3,
                _ => 0
            };
            AlignmentListView.SelectionChanged -= AlignmentChanged;
            AlignmentListView.SelectedIndex = parsedValue;
            AlignmentListView.SelectionChanged += AlignmentChanged;
        }
    }

    private void DOMLoaded(CoreWebView2 sender, CoreWebView2DOMContentLoadedEventArgs args) => _domLoadedTask.TrySetResult(true);

    private void SignatureNameTextBoxTextChanged(object sender, TextChangedEventArgs e) => IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(SignatureNameTextBox.Text);

    private void InvertComposerThemeClicked(object sender, RoutedEventArgs e) => IsComposerDarkMode = !IsComposerDarkMode;
}
