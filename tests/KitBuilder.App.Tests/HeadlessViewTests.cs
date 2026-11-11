using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using KitBuilder.App.Views;

namespace KitBuilder.App.Tests;

/// <summary>
/// Renders the real views over a fixture kit, catching broken bindings and templates that
/// view-model tests cannot see.
/// </summary>
public class HeadlessViewTests
{
    [AvaloniaFact]
    public async Task Start_screen_shows_until_a_kit_is_open()
    {
        using var h = new Harness();
        var window = new MainWindow { DataContext = h.Main };
        window.Show();

        Assert.False(window.GetVisualDescendants().OfType<TabControl>().Single().IsVisible);
        Assert.Contains(window.GetVisualDescendants().OfType<Button>(), b => b.Content as string == "Open kit folder...");

        await h.OpenAsync();
        Assert.True(window.GetVisualDescendants().OfType<TabControl>().Single().IsVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Every_tab_renders_its_view()
    {
        using var h = new Harness();
        await h.OpenAsync();
        var window = new MainWindow { DataContext = h.Main };
        window.Show();
        var tabs = window.GetVisualDescendants().OfType<TabControl>().Single();

        tabs.SelectedIndex = 1;
        window.UpdateLayout();
        Assert.Contains(window.GetVisualDescendants().OfType<TextBox>(), t => t.Text == "fixture-admin");

        tabs.SelectedIndex = 2;
        window.UpdateLayout();
        Assert.NotNull(window.GetVisualDescendants().OfType<ScriptsView>().SingleOrDefault());
        // The specialize phase is selected first: a switch per runnable or disabled file; the
        // supporting file's switch is hidden. Each item shows its plain title and description.
        var switches = window.GetVisualDescendants().OfType<ToggleButton>().Where(t => t is not CheckBox and not RadioButton).ToList();
        Assert.Equal(5, switches.Count);
        Assert.Equal(4, switches.Count(t => t.IsVisible));
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Setting up the computer");
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Says gamma.");

        tabs.SelectedIndex = 3;
        window.UpdateLayout();
        Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Dell Latitude 5450");

        tabs.SelectedIndex = 4;
        window.UpdateLayout();
        Assert.NotNull(window.GetVisualDescendants().OfType<BuildView>().SingleOrDefault());

        window.Close();
    }
}
