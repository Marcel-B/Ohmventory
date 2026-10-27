namespace Ohmventory.Api.Domain;

public class PartImage
{
    public Guid Id { get; set; }
    public Guid? PartId { get; set; }
    public Part? Part { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    /// <summary>Rohantwort der Bauteilerkennung als JSON, zur Nachvollziehbarkeit.</summary>
    public string? RecognitionJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
