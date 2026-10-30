namespace CipherVault.UI.Views.Controls;

using System.Collections;
using System.Windows;
using System.Windows.Controls;

public partial class EntryHistoryPanel : UserControl
{
    public static readonly DependencyProperty HistoryEntriesProperty =
        DependencyProperty.Register(
            nameof(HistoryEntries),
            typeof(IEnumerable),
            typeof(EntryHistoryPanel),
            new PropertyMetadata(null));

    public IEnumerable HistoryEntries
    {
        get => (IEnumerable)GetValue(HistoryEntriesProperty);
        set => SetValue(HistoryEntriesProperty, value);
    }

    public EntryHistoryPanel()
    {
        InitializeComponent();
    }
}
