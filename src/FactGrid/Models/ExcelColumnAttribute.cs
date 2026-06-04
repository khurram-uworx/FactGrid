namespace FactGrid.Models;

[AttributeUsage(AttributeTargets.Property)]
public class ExcelColumnAttribute : Attribute
{
    public int Position { get; set; }
    public string Title { get; set; }
    public bool Required { get; set; }
    public object? Example { get; set; }

    public ExcelColumnAttribute(int position, string title)
    {
        Position = position;
        Title = title;
    }
}
