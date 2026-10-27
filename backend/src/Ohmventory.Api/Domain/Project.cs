namespace Ohmventory.Api.Domain;

public class Project
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? Question { get; set; }
    public List<string> Sources { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ProjectItem> Items { get; set; } = [];
}

public class ProjectItem
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;
    public required string Name { get; set; }
    public string? PartNumber { get; set; }
    public int QuantityRequired { get; set; } = 1;
    public Guid? PartId { get; set; }
    public Part? Part { get; set; }
}
