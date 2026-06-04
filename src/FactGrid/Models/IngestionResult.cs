namespace FactGrid.Models;

public record IngestionResult
{
    public bool Success { get; init; }
    public int InsertedCount { get; init; }
    public string[] Errors { get; init; } = [];
}
