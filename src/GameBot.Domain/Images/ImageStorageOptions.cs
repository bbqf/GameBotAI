using System;

namespace GameBot.Domain.Images
{
    public sealed class ImageStorageOptions
    {
        public const string DefaultFolderName = "images";

        public ImageStorageOptions(string root)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(root);
            Root = root;
        }

        public string Root { get; }
    }
}
