using System;

namespace BloodPressureListFormatter
{
    interface ICSVRow
    {
        int size { get; }
        string getString(int index);
        int? getInt(int index);
        long? getLong(int index);
        double? getDoule(int index);
        DateTime? getDate(int index);
    }

}
