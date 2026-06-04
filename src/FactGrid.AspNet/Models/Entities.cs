using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FactGrid.AspNet.Models;

[Table("ResourceHours")]
public class Worklog
{
    public int Id { get; set; }

    [MaxLength(200)]
    [Description("The name of the resource or person who performed the work")]
    public string ResourceName { get; set; } = string.Empty;

    [MaxLength(300)]
    [Description("The project, cost center, or activity code")]
    public string? Project { get; set; } = string.Empty;

    [Description("Description of the work performed")]
    public string? Description { get; set; }

    [Column(TypeName = "date")]
    [Description("Date the work was performed")]
    public DateOnly WorkDate { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    [Description("Number of hours worked")]
    public decimal Hours { get; set; }

    [MaxLength(50)]
    [Description("Approval status (e.g. Approved, Pending, Rejected)")]
    public string ApprovalStatus { get; set; } = string.Empty;
}

[Table("Expenses")]
public class Expense
{
    public int Id { get; set; }

    [MaxLength(200)]
    [Description("The name of the resource or person who incurred the expense")]
    public string ResourceName { get; set; } = string.Empty;

    [MaxLength(100)]
    [Description("Expense category (e.g. Travel, Office Supplies, Meals)")]
    public string? Category { get; set; }

    [Description("Description of the expense")]
    public string? Description { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    [Description("Expense amount in dollars")]
    public decimal Amount { get; set; }

    [Column(TypeName = "date")]
    [Description("Date the expense was incurred")]
    public DateOnly ExpenseDate { get; set; }

    [MaxLength(50)]
    [Description("Approval status (e.g. Approved, Pending, Rejected)")]
    public string ApprovalStatus { get; set; } = string.Empty;
}
