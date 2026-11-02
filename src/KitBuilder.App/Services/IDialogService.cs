namespace KitBuilder.App.Services;

/// <summary>Pickers and confirmations, abstracted so view models stay unit-testable.</summary>
public interface IDialogService
{
    /// <summary>Prompt for a folder. Returns its path, or null if cancelled.</summary>
    Task<string?> PickFolderAsync(string title);

    /// <summary>Prompt for one existing file. Returns its path, or null if cancelled.</summary>
    Task<string?> PickFileAsync(string title, string typeName, IReadOnlyList<string> patterns);

    /// <summary>Prompt for where to save a file. Returns the path, or null if cancelled.</summary>
    Task<string?> SaveFileAsync(string title, string suggestedFileName, string extension);

    /// <summary>Ask a yes/no question. True when the person chose <paramref name="confirmText"/>.</summary>
    Task<bool> ConfirmAsync(string title, string message, string confirmText);
}

/// <summary>Hands files and folders to the operating system, and the clipboard.</summary>
public interface IDesktopService
{
    /// <summary>Opens a file in its default app, or a folder in the file manager.</summary>
    void Open(string path);

    /// <summary>Shows the file manager with <paramref name="path"/> selected.</summary>
    void Reveal(string path);

    Task CopyTextAsync(string text);
}
