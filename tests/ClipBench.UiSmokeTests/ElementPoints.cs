using FlaUI.Core.AutomationElements;

namespace ClipBench.UiSmokeTests;

internal static class ElementPoints
{
    /// <summary>
    /// The middle of the element, which is on the element itself. The point that UI Automation calls clickable can be at
    /// its edge, on padding that belongs to the list item and not to the row inside it.
    /// </summary>
    /// <param name="element">The element.</param>
    /// <returns>The point in the middle of its rectangle.</returns>
    public static System.Drawing.Point Center(this AutomationElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var rectangle = element.BoundingRectangle;
        return new System.Drawing.Point(rectangle.X + (rectangle.Width / 2), rectangle.Y + (rectangle.Height / 2));
    }
}
