using System.Collections.Generic;
using FluentAssertions;
using Xunit;
using GameBot.Domain.Vision;

namespace GameBot.UnitTests
{
    public class NmsTests
    {
        [Fact(DisplayName = "NMS suppresses overlapping boxes")]
        public void ApplySuppressesOverlappingBoxes()
        {
            var a = new TemplateMatch(new BoundingBox(10, 10, 10, 10), 0.95);
            var b = new TemplateMatch(new BoundingBox(12, 12, 10, 10), 0.90); // overlaps with a
            var c = new TemplateMatch(new BoundingBox(40, 40, 10, 10), 0.85); // separate
            var list = new List<TemplateMatch> { a, b, c };

            var result = Nms.Apply(list, overlap: 0.3, maxResults: 10);

            result.Should().Contain(a);
            result.Should().Contain(c);
            result.Should().NotContain(b); // suppressed due to overlap and lower score
        }
    }
}
