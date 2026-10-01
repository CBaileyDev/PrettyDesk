using System.Security.Cryptography;
using SkiaSharp;

namespace PrettyDesk.Core.Content;

public sealed record UserImage(string Id, string Path, int Width, int Height);

public enum ImportFailure
{
    None,
    NotFound,
    UnsupportedFormat,
    Unreadable,
    TooSmall,
}

public sealed record ImportResult(UserImage? Image, ImportFailure Failure, string? Message)
{
    public bool Ok => Image is not null;
}

/// <summary>
/// The user's own wallpapers (FR-CON-7): JPEG/PNG/WebP/BMP with a long edge of at least 1280 px, copied into
/// <c>user/</c> under a content-hash name so duplicates collapse. Ids look like <c>user:3fa9c1d2e4b5.jpg</c>.
/// </summary>
public sealed class UserImageStore
{
    public const string IdPrefix = "user:";
    public const int MinLongEdge = 1280;

    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".bmp" };

    private readonly string _directory;

    public UserImageStore(string directory) => _directory = directory;

    public static bool IsUserId(string wallpaperId) => wallpaperId.StartsWith(IdPrefix, StringComparison.Ordinal);

    public ImportResult Import(string sourcePath)
    {
        if (!File.Exists(sourcePath))
        {
            return new ImportResult(null, ImportFailure.NotFound, "That file no longer exists.");
        }

        var extension = System.IO.Path.GetExtension(sourcePath);
        if (!Extensions.Contains(extension))
        {
            return new ImportResult(null, ImportFailure.UnsupportedFormat, "PrettyDesk supports JPEG, PNG, WebP and BMP images.");
        }

        SKImageInfo info;
        try
        {
            using var codec = SKCodec.Create(sourcePath);
            if (codec is null)
            {
                return new ImportResult(null, ImportFailure.Unreadable, "That file doesn't look like a valid image.");
            }

            info = codec.Info;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ImportResult(null, ImportFailure.Unreadable, "PrettyDesk couldn't read that file.");
        }

        if (Math.Max(info.Width, info.Height) < MinLongEdge)
        {
            return new ImportResult(null, ImportFailure.TooSmall, $"That image is {info.Width}×{info.Height}. Wallpapers need at least {MinLongEdge} px on the long edge to look sharp.");
        }

        string hash;
        using (var stream = File.OpenRead(sourcePath))
        {
            hash = Convert.ToHexStringLower(SHA256.HashData(stream))[..12];
        }

        var fileName = hash + extension.ToLowerInvariant().Replace(".jpeg", ".jpg", StringComparison.Ordinal);
        Directory.CreateDirectory(_directory);
        var destination = System.IO.Path.Combine(_directory, fileName);
        if (!File.Exists(destination))
        {
            var temp = destination + ".tmp";
            File.Copy(sourcePath, temp, overwrite: true);
            File.Move(temp, destination, overwrite: true);
        }

        return new ImportResult(new UserImage(IdPrefix + fileName, destination, info.Width, info.Height), ImportFailure.None, null);
    }

    public UserImage? TryGet(string id)
    {
        if (!IsUserId(id))
        {
            return null;
        }

        var name = id[IdPrefix.Length..];

        // The id comes from settings.json, which the user can edit: never let it escape the user folder.
        if (name.Length == 0 || name != System.IO.Path.GetFileName(name))
        {
            return null;
        }

        var path = System.IO.Path.Combine(_directory, name);
        if (!File.Exists(path))
        {
            return null;
        }

        using var codec = SKCodec.Create(path);
        return codec is null ? null : new UserImage(id, path, codec.Info.Width, codec.Info.Height);
    }

    public IReadOnlyList<UserImage> List() =>
        !Directory.Exists(_directory)
            ? []
            : Directory.GetFiles(_directory)
                .Where(f => Extensions.Contains(System.IO.Path.GetExtension(f)))
                .OrderBy(f => File.GetCreationTimeUtc(f))
                .Select(f => TryGet(IdPrefix + System.IO.Path.GetFileName(f)))
                .Where(i => i is not null)
                .Select(i => i!)
                .ToList();

    public bool Remove(string id)
    {
        var image = TryGet(id);
        if (image is null)
        {
            return false;
        }

        File.Delete(image.Path);
        return true;
    }

    /// <summary>Warns when an image has fewer pixels than a monitor, so it would be upscaled (FR-CON-7).</summary>
    public static string? ResolutionWarning(UserImage image, IEnumerable<Abstractions.MonitorInfo> monitors)
    {
        var tooSmall = monitors.FirstOrDefault(m => image.Width < m.PixelWidth || image.Height < m.PixelHeight);
        return tooSmall is null
            ? null
            : $"This image ({image.Width}×{image.Height}) is smaller than your {tooSmall.PixelWidth}×{tooSmall.PixelHeight} display and may look soft.";
    }
}
