namespace Ohmventory.Api.Storage;

public class ImageStorageOptions
{
    public const string Section = "Storage";
    public string ImageDirectory { get; set; } = "data/images";
}

public class ImageStorage(Microsoft.Extensions.Options.IOptions<ImageStorageOptions> options)
{
    private readonly string _root = Path.GetFullPath(options.Value.ImageDirectory);

    public async Task<string> SaveAsync(Guid id, string extension, Stream content, CancellationToken ct)
    {
        Directory.CreateDirectory(_root);
        var fileName = $"{id:N}{extension}";
        await using var file = File.Create(Path.Combine(_root, fileName));
        await content.CopyToAsync(file, ct);
        return fileName;
    }

    public string GetPath(string fileName) => Path.Combine(_root, Path.GetFileName(fileName));
}
