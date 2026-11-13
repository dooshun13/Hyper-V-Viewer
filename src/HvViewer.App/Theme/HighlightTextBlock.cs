using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace HvViewer.App.Theme;

/// <summary>A TextBlock that marks the parts of its text that matched the search.</summary>
public sealed class HighlightTextBlock : TextBlock
{
    public static readonly DependencyProperty SourceTextProperty = DependencyProperty.Register(
        nameof(SourceText), typeof(string), typeof(HighlightTextBlock), new PropertyMetadata(null, (d, _) => ((HighlightTextBlock)d).Rebuild()));

    public static readonly DependencyProperty MatchesProperty = DependencyProperty.Register(
        nameof(Matches), typeof(IReadOnlyList<(int Start, int Length)>), typeof(HighlightTextBlock),
        new PropertyMetadata(null, (d, _) => ((HighlightTextBlock)d).Rebuild()));

    public string? SourceText
    {
        get => (string?)GetValue(SourceTextProperty);
        set => SetValue(SourceTextProperty, value);
    }

    public IReadOnlyList<(int Start, int Length)>? Matches
    {
        get => (IReadOnlyList<(int Start, int Length)>?)GetValue(MatchesProperty);
        set => SetValue(MatchesProperty, value);
    }

    private void Rebuild()
    {
        Inlines.Clear();
        var text = SourceText;
        if (string.IsNullOrEmpty(text)) return;

        var position = 0;
        foreach (var (start, length) in (Matches ?? []).OrderBy(m => m.Start))
        {
            if (start < position || start + length > text.Length) continue; // overlapping or out of range
            if (start > position) Inlines.Add(new Run(text[position..start]));
            Inlines.Add(new Run(text.Substring(start, length)) { Background = HighlightBrush(), FontWeight = FontWeights.SemiBold });
            position = start + length;
        }
        if (position < text.Length) Inlines.Add(new Run(text[position..]));
    }

    private Brush HighlightBrush()
    {
        var color = TryFindResource("Warning") is SolidColorBrush warning ? warning.Color : Colors.Gold;
        return new SolidColorBrush(Color.FromArgb(90, color.R, color.G, color.B));
    }
}
