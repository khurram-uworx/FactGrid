using FactGrid.Models;
using System.Collections.Concurrent;
using System.Reflection;

namespace FactGrid.Services;

public static class ExcelColumnMetadata
{
    public record ColumnInfo(int Position, PropertyInfo Property, ExcelColumnAttribute Attr);

    static readonly ConcurrentDictionary<Type, IReadOnlyList<ColumnInfo>> ColumnsCache = new();
    static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, int>> IndexCache = new();

    public static IReadOnlyList<ColumnInfo> GetColumns(Type modelType)
    {
        return ColumnsCache.GetOrAdd(modelType, BuildColumns);
    }

    public static IReadOnlyDictionary<string, int> GetColumnIndex(Type modelType)
    {
        return IndexCache.GetOrAdd(modelType, t => GetColumns(t).ToDictionary(c => c.Property.Name, c => c.Position));
    }

    public static void Validate(Type modelType)
    {
        var columns = GetColumns(modelType);

        var invalid = columns.FirstOrDefault(c => c.Position <= 0);
        if (invalid is not null)
            throw new InvalidOperationException(
                $"ExcelColumn position {invalid.Position} on {modelType.Name}.{invalid.Property.Name} must be positive.");

        var dupes = columns.GroupBy(c => c.Position).FirstOrDefault(g => g.Count() > 1);
        if (dupes is not null)
            throw new InvalidOperationException(
                $"Duplicate ExcelColumn position {dupes.Key} on {modelType.Name}.");
    }

    public static List<string> ValidateRequired(Type modelType, IReadOnlyDictionary<string, string> cellTexts, int rowNumber)
    {
        var errors = new List<string>();
        var index = GetColumnIndex(modelType);
        foreach (var col in GetColumns(modelType).Where(c => c.Attr.Required))
        {
            if (string.IsNullOrWhiteSpace(cellTexts[col.Property.Name]))
                errors.Add($"Row {rowNumber}: {col.Property.Name} is required");
        }
        return errors;
    }

    static IReadOnlyList<ColumnInfo> BuildColumns(Type modelType)
    {
        return modelType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => (Prop: p, Attr: p.GetCustomAttribute<ExcelColumnAttribute>()))
            .Where(x => x.Attr is not null)
            .Select(x => new ColumnInfo(x.Attr!.Position, x.Prop, x.Attr))
            .OrderBy(x => x.Position)
            .ToList();
    }
}
