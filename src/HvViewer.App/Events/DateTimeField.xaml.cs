using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HvViewer.App.Events;

/// <summary>A date-and-time box: type it, or click it to pick the day on a calendar and the time from lists.</summary>
public partial class DateTimeField : UserControl
{
    private const string Format = "yyyy-MM-dd HH:mm:ss";

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(DateTimeField),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private bool _loading;

    public DateTimeField()
    {
        InitializeComponent();
        Hour.ItemsSource = Enumerable.Range(0, 24).Select(i => i.ToString("00", CultureInfo.InvariantCulture)).ToList();
        Minute.ItemsSource = Enumerable.Range(0, 60).Select(i => i.ToString("00", CultureInfo.InvariantCulture)).ToList();
        Second.ItemsSource = Minute.ItemsSource;
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    private void OnToggle(object sender, RoutedEventArgs e) => Pop.IsOpen = !Pop.IsOpen;

    private void OnBoxClicked(object sender, MouseButtonEventArgs e) => Pop.IsOpen = true;

    /// <summary>When the picker opens it shows the date in the box (or now, if the box holds nothing readable).</summary>
    private void OnOpened(object? sender, EventArgs e)
    {
        var value = DateTime.TryParseExact(Text?.Trim(), Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed : DateTime.Now;
        Show(value);
    }

    private void Show(DateTime value)
    {
        _loading = true;
        Cal.SelectedDate = value.Date;
        Cal.DisplayDate = value.Date;
        Hour.SelectedIndex = value.Hour;
        Minute.SelectedIndex = value.Minute;
        Second.SelectedIndex = value.Second;
        _loading = false;
    }

    /// <summary>Any change in the calendar or the time lists is written to the box straight away.</summary>
    private void OnPicked(object sender, EventArgs e)
    {
        if (_loading || Cal.SelectedDate is not { } day || Hour.SelectedIndex < 0 || Minute.SelectedIndex < 0 || Second.SelectedIndex < 0) return;
        var value = day.Date.AddHours(Hour.SelectedIndex).AddMinutes(Minute.SelectedIndex).AddSeconds(Second.SelectedIndex);
        Text = value.ToString(Format, CultureInfo.InvariantCulture);
    }

    /// <summary>Jumps to today's date (in the zone chosen in Settings), keeping the time already picked.</summary>
    private void OnToday(object sender, RoutedEventArgs e)
    {
        var today = (DataContext as EventsViewModel)?.NowInZone().Date ?? DateTime.Today;
        _loading = true;
        Cal.SelectedDate = today;
        Cal.DisplayDate = today;
        _loading = false;
        OnPicked(sender, e);
    }

    private void OnOk(object sender, RoutedEventArgs e) => Pop.IsOpen = false;
}
