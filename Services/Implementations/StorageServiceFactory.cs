using System;
using Microsoft.Extensions.DependencyInjection;
using PaperMind.Models.Enums;
using PaperMind.Services.Abstractions;

namespace PaperMind.Services.Implementations
{
    public class StorageServiceFactory
    {
        private readonly IServiceProvider _serviceProvider;

        public StorageServiceFactory(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IStorageService Create(StorageProvider provider)
        {
            return provider switch
            {
                StorageProvider.Local => _serviceProvider.GetRequiredService<LocalStorageService>(),
                StorageProvider.GoogleDrive => _serviceProvider.GetRequiredService<GoogleDriveStorageService>(),
                StorageProvider.Dropbox => _serviceProvider.GetRequiredService<DropboxStorageService>(),
                StorageProvider.OneDrive => _serviceProvider.GetRequiredService<OneDriveStorageService>(),
                _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
            };
        }
    }
}
