using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;

namespace FactGrid.AspNet.Services;

public record ColumnMetadata(
    string Name,
    string Type,
    string Description,
    int? MaxLength,
    bool IsNullable
);

public static class EntitySchemaHelper
{
    public static IEnumerable<ColumnMetadata> GetColumns(Type modelType)
    {
        return modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name != "Id")
            .Select(p => new ColumnMetadata(
                Name: p.Name,
                Type: GetDisplayType(p),
                Description: p.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "",
                MaxLength: p.GetCustomAttribute<MaxLengthAttribute>()?.Length,
                IsNullable: IsNullableProperty(p)
            ));
    }

    static string GetDisplayType(PropertyInfo property)
        => property.GetCustomAttribute<ColumnAttribute>()?.TypeName
            ?? MapToDisplayType(property.PropertyType);

    static string MapToDisplayType(Type type)
    {
        return type switch
        {
            Type t when t == typeof(string) => "NVARCHAR",
            Type t when t == typeof(int) => "INT",
            Type t when t == typeof(decimal) => "DECIMAL(10,2)",
            Type t when t == typeof(DateOnly) => "DATE",
            Type t when t == typeof(DateTime) => "DATE",
            Type t when t == typeof(bool) => "BIT",
            _ => type.Name
        };
    }

    static bool IsNullableProperty(PropertyInfo prop)
    {
        if (Nullable.GetUnderlyingType(prop.PropertyType) is not null)
            return true;

        if (!prop.PropertyType.IsValueType)
        {
            var nullableContext = new NullabilityInfoContext();
            var info = nullableContext.Create(prop);
            return info.WriteState is NullabilityState.Nullable;
        }

        return false;
    }
}
