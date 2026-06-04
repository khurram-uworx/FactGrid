using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FactGrid.Models;

[Table("ResourceHours")]
public class Worklog
{
    public int Id { get; set; }

    [MaxLength(200)]
    [Description("The name of the resource or person who performed the work")]
    [ExcelColumn(1, "Resource Name", Required = true, Example = "John Doe")]
    public string ResourceName { get; set; } = string.Empty;

    [MaxLength(300)]
    [Description("The project, cost center, or activity code")]
    [ExcelColumn(2, "Project", Example = "Project Alpha")]
    public string? Project { get; set; } = string.Empty;

    [Description("Description of the work performed")]
    [ExcelColumn(3, "Description", Example = "Development work on feature X")]
    public string? Description { get; set; }

    [Column(TypeName = "date")]
    [Description("Date the work was performed")]
    [ExcelColumn(4, "Work Date", Required = true, Example = "2024-12-25")]
    public DateOnly WorkDate { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    [Description("Number of hours worked")]
    [ExcelColumn(5, "Hours", Required = true, Example = 8.0)]
    public decimal Hours { get; set; }

    [MaxLength(50)]
    [Description("Approval status (e.g. Approved, Pending, Rejected)")]
    [ExcelColumn(6, "Approval Status", Example = "Approved")]
    public string ApprovalStatus { get; set; } = string.Empty;
}

[Table("Expenses")]
public class Expense
{
    public int Id { get; set; }

    [MaxLength(200)]
    [Description("The name of the resource or person who incurred the expense")]
    [ExcelColumn(1, "Resource Name", Required = true, Example = "Jane Smith")]
    public string ResourceName { get; set; } = string.Empty;

    [MaxLength(100)]
    [Description("Expense category (e.g. Travel, Office Supplies, Meals)")]
    [ExcelColumn(2, "Category", Example = "Travel")]
    public string? Category { get; set; }

    [Description("Description of the expense")]
    [ExcelColumn(3, "Description", Example = "Conference flight ticket")]
    public string? Description { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    [Description("Expense amount in dollars")]
    [ExcelColumn(4, "Amount", Required = true, Example = 450.00)]
    public decimal Amount { get; set; }

    [Column(TypeName = "date")]
    [Description("Date the expense was incurred")]
    [ExcelColumn(5, "Expense Date", Required = true, Example = "2025-03-15")]
    public DateOnly ExpenseDate { get; set; }

    [MaxLength(50)]
    [Description("Approval status (e.g. Approved, Pending, Rejected)")]
    [ExcelColumn(6, "Approval Status", Example = "Approved")]
    public string ApprovalStatus { get; set; } = string.Empty;
}
