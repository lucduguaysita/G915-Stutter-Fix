using System.Collections.Generic;

namespace KeyboardRepeatFilter
{
    public sealed class FilterConfig
    {
        public string LogLevel { get; set; } = "Info";
        public string LogFilePath { get; set; } = "C:\\Temp\\KeyboardRepeatFilter.log";
        public double MinRepeatIntervalMs { get; set; } = 28.0;
        public int[] ExcludedVkCodes { get; set; } = new[] { 8, 13 };
        public Dictionary<int, double> PerKeyMinRepeatIntervalMs { get; set; } = new Dictionary<int, double>();
    }
}
