using Microsoft.UI.Xaml.Controls;
using PictureSorter.App.Views;

namespace PictureSorter.App.Services;

/// <summary>
/// Führt die Navigation auf der Navigationsleiste und dem Rahmen des Hauptfensters
/// aus. Beides gehört zusammen: Der Rahmen zeigt die Seite, die Leiste markiert den
/// Eintrag. Lagen beide Schritte an verschiedenen Stellen, liefen sie irgendwann
/// auseinander – deshalb kennt nur diese Klasse die Zuordnung Bereich → Seite.
/// </summary>
internal sealed class NavigationService : INavigationService
{
    // Die Kennungen stehen so auch als Tag an den Einträgen der Navigationsleiste.
    private static readonly (AppSection Section, string Tag, Type Page)[] Sections =
    [
        (AppSection.Dashboard, "dashboard", typeof(DashboardPage)),
        (AppSection.Sort, "sort", typeof(SortPage)),
        (AppSection.Duplicates, "duplicates", typeof(DuplicatesPage)),
        (AppSection.Calendar, "calendar", typeof(CalendarSortPage)),
        (AppSection.Memory, "memory", typeof(MemoryPage)),
        (AppSection.Settings, "about", typeof(AboutPage)),
    ];

    private NavigationView? _navigationView;
    private Frame? _frame;

    /// <summary>
    /// Verbindet den Dienst mit der Navigationsleiste und dem Rahmen des
    /// Hauptfensters und zeigt die Startseite.
    /// </summary>
    /// <param name="navigationView">Die Navigationsleiste.</param>
    /// <param name="frame">Der Rahmen, in dem die Seiten erscheinen.</param>
    public void Initialize(NavigationView navigationView, Frame frame)
    {
        ArgumentNullException.ThrowIfNull(navigationView);
        ArgumentNullException.ThrowIfNull(frame);

        _navigationView = navigationView;
        _frame = frame;
        navigationView.SelectionChanged += OnSelectionChanged;

        ShowPage(AppSection.Dashboard);
    }

    /// <inheritdoc />
    public void NavigateTo(AppSection section)
    {
        if (_navigationView is null)
        {
            return;
        }

        string tag = TagOf(section);
        foreach (object item in _navigationView.MenuItems)
        {
            if (item is not NavigationViewItem entry
                || !string.Equals(entry.Tag as string, tag, StringComparison.Ordinal))
            {
                continue;
            }

            // Die Auswahl zu setzen löst SelectionChanged aus – dort wird die Seite
            // gezeigt. Ist der Eintrag schon ausgewählt, feuert das Ereignis nicht;
            // dann muss der Rahmen direkt folgen.
            if (ReferenceEquals(_navigationView.SelectedItem, entry))
            {
                ShowPage(section);
            }
            else
            {
                _navigationView.SelectedItem = entry;
            }

            return;
        }
    }

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem entry && TryParseTag(entry.Tag as string, out AppSection section))
        {
            ShowPage(section);
        }
    }

    private void ShowPage(AppSection section) => _ = _frame?.Navigate(PageOf(section));

    private static string TagOf(AppSection section) =>
        Sections.First(entry => entry.Section == section).Tag;

    private static Type PageOf(AppSection section) =>
        Sections.First(entry => entry.Section == section).Page;

    private static bool TryParseTag(string? tag, out AppSection section)
    {
        foreach ((AppSection candidate, string candidateTag, _) in Sections)
        {
            if (string.Equals(candidateTag, tag, StringComparison.Ordinal))
            {
                section = candidate;
                return true;
            }
        }

        section = AppSection.Dashboard;
        return false;
    }
}
