using System;

namespace BloodPressureListFormatter
{
    static class CSVDelimiterExtensions
    {
        public static string GetValue(this CSVDelimiter delimiter)
        {
            switch (delimiter)
            {
                case CSVDelimiter.COMMA:
                    return ",";
                case CSVDelimiter.TAB:
                    return "\t";
                default:
                    throw new Exception();
            }
        }
    }
}
