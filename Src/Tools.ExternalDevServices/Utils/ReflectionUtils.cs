using System.ComponentModel;
using System.Reflection;

namespace Tools.ExternalDevServices.Utils;

public static class ReflectionUtils
{
    public static IReadOnlyList<(string Name, string? Description)>
        GetNamesAndDescriptionsOrdered<TEnum>() where TEnum : struct, Enum
    {
        return GetNamesAndDescriptionsOrderedExcept<TEnum>();
    }

    public static IReadOnlyList<(string Name, string? Description)>
        GetNamesAndDescriptionsOrderedExcept<TEnum>(params TEnum[] excludedValues) where TEnum : struct, Enum
    {
        return Enum.GetValues(typeof(TEnum))
            .Cast<TEnum>()
            .Except(excludedValues)
            .OrderBy(v => Convert.ToInt64(v)) // works for any underlying enum type
            .Select(v =>
            {
                var fi = typeof(TEnum).GetField(v.ToString())!;
                var desc = fi.GetCustomAttribute<DescriptionAttribute>()?.Description;
                return (v.ToString(), desc);
            })
            .ToArray();
    }
}