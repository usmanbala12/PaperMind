namespace PaperMind.Services.Implementations
{
    public class StorageRequestConfig
    {
        public string? DropboxAccessToken { get; set; }

        public string? GoogleDriveClientId { get; set; }
        public string? GoogleDriveClientSecret { get; set; }

        public string? OneDriveClientId { get; set; }
        public string? OneDriveTenantId { get; set; }
    }
}
