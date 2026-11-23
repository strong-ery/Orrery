using Avalonia;
using Avalonia.Controls;

namespace Orrery.App.Views;

public partial class DesignerView : UserControl
{
    private bool _narrow;

    public DesignerView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => Fit(e.NewSize.Width);
    }

    /// <summary>
    /// Wide: questions on the left, the plain-words summary beside them. Narrow: the summary drops under the questions
    /// so the tiles keep the whole width and nothing has to be squeezed.
    /// </summary>
    private void Fit(double width)
    {
        var narrow = width < 880;
        if (narrow == _narrow)
            return;
        _narrow = narrow;
        if (narrow)
        {
            Root.Children.Remove(Summary);
            Summary.Margin = new Thickness(0, 4, 0, 24);
            Questions.Children.Add(Summary);
            Root.ColumnDefinitions[1].Width = new GridLength(0);
            Root.ColumnSpacing = 0;
        }
        else
        {
            Questions.Children.Remove(Summary);
            Summary.Margin = new Thickness(0, 40, 0, 0);
            Root.Children.Add(Summary);
            Grid.SetColumn(Summary, 1);
            Root.ColumnDefinitions[1].Width = new GridLength(320);
            Root.ColumnSpacing = 32;
        }
    }
}
