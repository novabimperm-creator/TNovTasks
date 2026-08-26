using Autodesk.Revit.DB;

namespace TNovTasks
{
    internal static class RevitApiCompat
    {
        public static FilterRule CreateContainsRule(ElementId parameterId, string value)
        {
#if R2022
            return ParameterFilterRuleFactory.CreateContainsRule(parameterId, value, true);
#else
            return ParameterFilterRuleFactory.CreateContainsRule(parameterId, value);
#endif
        }

        public static int ElementIdIntValue(ElementId elementId)
        {
#if R2022
            return elementId.IntegerValue;
#else
            return checked((int)elementId.Value);
#endif
        }

        public static ElementId CreateElementId(int value)
        {
#if R2022
            return new ElementId(value);
#else
            return new ElementId((long)value);
#endif
        }
    }
}
