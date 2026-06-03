namespace EfMcp.AspNet.Services;

public interface IExcelParser<T> where T : class
{
    (List<T> Records, List<string> Errors) Parse(Stream excelStream);
}
