using Spectre.Console;
using Spectre.Console.Rendering;

namespace Dvtui.Views;

internal sealed class ScrollableContent(IRenderable content) : IRenderable
{
    public int Offset { get; set; }
    public int Height { get; set; } = 1;
    public int MaximumOffset { get; private set; }

    public Measurement Measure(RenderOptions options, int maxWidth)
    {
        return content.Measure(options, maxWidth);
    }

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        var lines = Segment.SplitLines(content.Render(options, maxWidth))
            .ToList();
        MaximumOffset = Math.Max(0, lines.Count - Height);
        Offset = Math.Clamp(Offset, 0, MaximumOffset);
        var visibleLines = lines.Skip(Offset).Take(Height).ToList();

        for(var index = 0; index < visibleLines.Count; index++)
        {
            if(index > 0)
            {
                yield return Segment.LineBreak;
            }

            foreach(var segment in visibleLines[index])
            {
                yield return segment;
            }
        }
    }
}
