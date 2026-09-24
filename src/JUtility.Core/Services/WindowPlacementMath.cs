namespace JUtility.Core.Services;

public readonly record struct WindowBounds(int Left, int Top, int Width, int Height);

public static class WindowPlacementMath
{
    public static WindowBounds ClampToWorkArea(
        int left,
        int top,
        int width,
        int height,
        int workLeft,
        int workTop,
        int workRight,
        int workBottom)
    {
        int workWidth = Math.Max(1, workRight - workLeft);
        int workHeight = Math.Max(1, workBottom - workTop);
        int safeWidth = Math.Min(Math.Max(1, width), workWidth);
        int safeHeight = Math.Min(Math.Max(1, height), workHeight);
        int safeLeft = Math.Clamp(left, workLeft, Math.Max(workLeft, workRight - safeWidth));
        int safeTop = Math.Clamp(top, workTop, Math.Max(workTop, workBottom - safeHeight));

        return new WindowBounds(safeLeft, safeTop, safeWidth, safeHeight);
    }

    public static WindowBounds PlaceNearCursor(
        int cursorX,
        int cursorY,
        int windowWidth,
        int windowHeight,
        int workLeft,
        int workTop,
        int workRight,
        int workBottom,
        int gap = 14)
    {
        int workWidth = Math.Max(1, workRight - workLeft);
        int workHeight = Math.Max(1, workBottom - workTop);
        int safeWidth = Math.Min(Math.Max(1, windowWidth), workWidth);
        int safeHeight = Math.Min(Math.Max(1, windowHeight), workHeight);
        int safeGap = Math.Max(0, gap);

        int left = cursorX + safeGap;
        int top = cursorY + safeGap;

        if (left + safeWidth > workRight)
        {
            left = cursorX - safeGap - safeWidth;
        }

        if (top + safeHeight > workBottom)
        {
            top = cursorY - safeGap - safeHeight;
        }

        left = Math.Clamp(left, workLeft, Math.Max(workLeft, workRight - safeWidth));
        top = Math.Clamp(top, workTop, Math.Max(workTop, workBottom - safeHeight));

        return new WindowBounds(left, top, safeWidth, safeHeight);
    }
}
