namespace TeliLandOverlay;

public enum DrawingToolKind
{
    None,
    Rectangle,
    Line,
    Arrow,
    Ellipse,
    Polygon,
    Star,
    Pen,
    Pencil,
    Text
}

public static class DrawingToolKindExtensions
{
    public static bool UsesDrawingOverlay(this DrawingToolKind toolKind)
    {
        return toolKind == DrawingToolKind.Pencil;
    }

    public static bool UsesDragDrawing(this DrawingToolKind toolKind)
    {
        return toolKind == DrawingToolKind.Pencil;
    }

    public static bool UsesTextPlacement(this DrawingToolKind toolKind)
    {
        return false;
    }
}
