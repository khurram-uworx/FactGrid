namespace FactGrid.AspNet.Services;

public interface IEntityServiceFactory
{
    IExcelParser CreateExcelParser(Type modelType);
    IEntityTableService CreateTableService(Type modelType);
}
