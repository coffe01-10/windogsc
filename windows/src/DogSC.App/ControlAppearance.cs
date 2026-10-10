namespace DogSC;

static class ControlAppearance
{
    public static void SelectSegment(Button button, bool selected)
    {
        if (selected) button.SetResourceReference(Control.BackgroundProperty, "SegmentSelectedBrush");
        else button.Background = System.Windows.Media.Brushes.Transparent;
        button.SetResourceReference(Control.ForegroundProperty, selected ? "InkBrush" : "InkSecondaryBrush");
        button.BorderThickness = new System.Windows.Thickness(selected ? .75 : 0);
    }

    public static void SelectChip(Button button, bool selected)
    {
        button.SetResourceReference(Control.BackgroundProperty, selected ? "SelectionBrush" : "ControlBrush");
        button.SetResourceReference(Control.ForegroundProperty, "InkBrush");
        button.SetResourceReference(Control.BorderBrushProperty, selected ? "InkSecondaryBrush" : "ControlBorderBrush");
    }
}
