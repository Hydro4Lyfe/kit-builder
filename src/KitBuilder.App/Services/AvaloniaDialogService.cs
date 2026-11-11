using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using KitBuilder.App.Views;

namespace KitBuilder.App.Services;

/// <summary>Dialogs backed by Avalonia's <see cref="IStorageProvider"/> and <see cref="ConfirmWindow"/>.</summary>
public sealed class AvaloniaDialogService(Window owner) : IDialogService
{
    public async Task<string?> PickFolderAsync(string title)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    public async Task<string?> PickFileAsync(string title, string typeName, IReadOnlyList<string> patterns)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType(typeName) { Patterns = patterns }],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public async Task<string?> SaveFileAsync(string title, string suggestedFileName, string extension)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = extension,
            FileTypeChoices = [new FilePickerFileType(extension.ToUpperInvariant()) { Patterns = [$"*.{extension}"] }],
        });
        return file?.TryGetLocalPath();
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmText) =>
        new ConfirmWindow(title, message, confirmText).ShowDialog<bool>(owner);
}

/// <summary>Opens paths with the shell: Explorer on Windows, xdg-open elsewhere.</summary>
public sealed class DesktopService(TopLevel owner) : IDesktopService
{
    public void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

    public void Edit(string path)
    {
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { path } });
        else
            Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { path } });
    }

    public void Reveal(string path)
    {
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { $"/select,{path}" } });
        else
            Open(Directory.Exists(path) ? path : Path.GetDirectoryName(path)!);
    }

    public Task CopyTextAsync(string text) => owner.Clipboard?.SetTextAsync(text) ?? Task.CompletedTask;
}
