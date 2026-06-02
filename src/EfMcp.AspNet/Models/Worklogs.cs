using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EfMcp.AspNet.Models;

[Table("ResourceHours")]
public class Worklogs
{
    public int Id { get; set; }

    [MaxLength(200)]
    [Description("The name of the resource or person who performed the work")]
    public string ResourceName { get; set; } = string.Empty;

    [MaxLength(300)]
    [Description("The project, cost center, or activity code")]
    public string Project { get; set; } = string.Empty;

    [Description("Description of the work performed")]
    public string? Description { get; set; }

    [Description("Date the work was performed")]
    public DateTime WorkDate { get; set; }

    [Column(TypeName = "decimal(10,2)")]
    [Description("Number of hours worked")]
    public decimal Hours { get; set; }

    [MaxLength(50)]
    [Description("Approval status (e.g. Approved, Pending, Rejected)")]
    public string ApprovalStatus { get; set; } = string.Empty;
}
