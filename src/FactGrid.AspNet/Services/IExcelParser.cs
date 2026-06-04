using System.Collections;

namespace FactGrid.AspNet.Services;

public interface IExcelParser
{
    (IList Records, List<string> Errors) Parse(Stream excelStream);
}

public interface IExcelParser<T> : IExcelParser where T : class
{
    new (List<T> Records, List<string> Errors) Parse(Stream excelStream);
}
