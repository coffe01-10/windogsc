namespace DogSC;

static class ControlAppearance
{
    public static void SelectChip(Button button, bool selected)
    {
        button.SetResourceReference(Control.BackgroundProperty, selected ? "SelectionBrush" : "ControlBrush");
        button.SetResourceReference(Control.ForegroundProperty, "InkBrush");
        button.SetResourceReference(Control.BorderBrushProperty, selected ? "InkSecondaryBrush" : "ControlBorderBrush");
    }
}
