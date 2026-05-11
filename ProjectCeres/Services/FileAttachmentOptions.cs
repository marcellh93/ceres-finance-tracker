namespace ProjectCeres.Services;

public sealed class FileAttachmentOptions
{
    /// <summary>
    /// Filesystem root under which uploaded attachments are stored.
    /// When null, defaults to <c>IWebHostEnvironment.ContentRootPath</c>.
    /// Tests set this to a temp directory so uploads never leak into the SUT project root.
    /// </summary>
    public string? RootPath { get; set; }
}
