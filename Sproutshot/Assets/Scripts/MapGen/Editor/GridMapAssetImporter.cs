using System;
using UnityEditor;
using UnityEngine;
using YooAsset.Editor;

namespace Sproutshot.MapGen.Editor
{
    /// <summary>Import contract for continuous ground, grayscale data and transparent map art.</summary>
    public sealed class GridMapAssetImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            string normalized = assetPath.Replace('\\', '/');
            bool isNewMap = normalized.StartsWith("Assets/Arts/Sprites/Maps/", StringComparison.Ordinal);
            bool isBackground = normalized.StartsWith("Assets/Arts/Sprites/Environment/", StringComparison.Ordinal);
            bool isLegacyTile = normalized.StartsWith("Assets/Arts/Sprites/Tiles/", StringComparison.Ordinal);
            if ((!isNewMap && !isBackground && !isLegacyTile) ||
                !normalized.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("/Previews/")) return;

            bool isGround = isNewMap && normalized.Contains("/Ground/");
            bool isMask = isNewMap && normalized.Contains("/Shared/Mask/");
            bool isNoise = isNewMap && normalized.Contains("/Shared/Noise/");
            bool isData = isMask || isNoise;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = GridMapArtLibrary.PixelsPerUnit;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = !isGround && !isData;
            importer.sRGBTexture = !isData;
            importer.isReadable = isGround || isData;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = isGround || isNoise ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.maxTextureSize = 2048;
            importer.textureCompression = isGround || isData
                ? TextureImporterCompression.Uncompressed : TextureImporterCompression.CompressedHQ;

            if (isNewMap || isBackground)
            {
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                settings.spriteAlignment = (int)SpriteAlignment.Center;
                settings.spritePivot = Vector2.one * .5f;
                settings.spriteBorder = Vector4.zero;
                settings.spriteGenerateFallbackPhysicsShape = false;
                importer.SetTextureSettings(settings);
            }
        }
    }

    /// <summary>Never bundle composition previews or non-PNG files as runtime map art.</summary>
    [DisplayName("地图素材 PNG（不含预览图）")]
    public sealed class CollectMapPng : IAssetFilterRule
    {
        public string FindAssetType => EAssetFilterType.All.ToString();

        public bool IsCollectAsset(AssetFilterRuleData data)
        {
            string path = data.AssetPath.Replace('\\', '/');
            return path.StartsWith("Assets/Arts/Sprites/Maps/", StringComparison.Ordinal) &&
                   !path.Contains("/Previews/") &&
                   path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }
    }
}
