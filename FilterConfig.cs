using System.Collections.Generic;

namespace KeyboardRepeatFilter
{
    public sealed class FilterConfig
    {
        public double MinRepeatIntervalMs { get; set; } = 28.0;
        public int[] ExcludedVkCodes { get; set; } = new[] { 8, 13 };
        public Dictionary<int, double> PerKeyMinRepeatIntervalMs { get; set; } = new Dictionary<int, double>();
    }
}
