using System.Linq;

namespace BloodPressureListFormatter
{
    static class LinqExtensions
    {
        public static bool IsAnyOf<ELEMENT_T>(this ELEMENT_T value, params ELEMENT_T[] list)
        //where ELEMENT_T: IEquatable<ELEMENT_T>
        {
            return list.Contains(value);
        }
    }
}
