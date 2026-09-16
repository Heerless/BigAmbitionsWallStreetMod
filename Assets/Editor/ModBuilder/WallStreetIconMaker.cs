#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BAModTemplate.Editor
{
    /// <summary>
    /// Draws the Brokerage's business icon.
    /// <para>
    /// Vanilla business icons are small flat glyphs on a transparent ground. Generating
    /// one keeps it consistent with that set and avoids an AI image that would read as
    /// pasted on. The shape is a rising candlestick chart - the one symbol that says
    /// "trading floor" at 64 pixels.
    /// </para>
    /// </summary>
    public static class WallStreetIconMaker
    {
        private const int Size = 128;
        private const string AssetPath = "Assets/Mods/WallStreet/BusinessIcon-Brokerage.png";

        public static void Create()
        {
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var clear = new Color(1f, 1f, 1f, 0f);

            var pixels = new Color[Size * Size];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = clear;
            texture.SetPixels(pixels);

            var ink = Color.white;

            // Four candles stepping upward, each with a wick.
            DrawCandle(texture, ink, x: 16, bodyLow: 30, bodyHigh: 58, wickLow: 20, wickHigh: 68);
            DrawCandle(texture, ink, x: 44, bodyLow: 44, bodyHigh: 76, wickLow: 34, wickHigh: 86);
            DrawCandle(texture, ink, x: 72, bodyLow: 58, bodyHigh: 92, wickLow: 48, wickHigh: 102);
            DrawCandle(texture, ink, x: 100, bodyLow: 74, bodyHigh: 112, wickLow: 64, wickHigh: 120);

            // Baseline, so the glyph reads as a chart rather than floating bars.
            FillRect(texture, ink, 10, 14, Size - 20, 4);

            texture.Apply();

            var full = Path.Combine(Directory.GetCurrentDirectory(), AssetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(AssetPath) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.assetBundleName = "wallstreet";
                importer.assetBundleVariant = "unity3d";
                importer.SaveAndReimport();
            }

            AssetDatabase.Refresh();
            Debug.Log("[CI] Brokerage icon written to " + AssetPath);
        }

        /// <summary>
        /// Draws the Broker skill icon: a climbing line with an arrowhead.
        /// <para>
        /// Skill icons appear on every employee row, so it has to read at a glance and stay
        /// distinct from the Brokerage's candlestick. Without one, Brokers wear the Lawyer
        /// icon they were cloned from.
        /// </para>
        /// </summary>
        public static void CreateSkillIcon()
        {
            const string skillPath = "Assets/Mods/WallStreet/SkillIcon-Broker.png";

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var pixels = new Color[Size * Size];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color(1f, 1f, 1f, 0f);
            texture.SetPixels(pixels);

            var ink = Color.white;

            DrawLine(texture, ink, 18, 34, 58, 58, 8);
            DrawLine(texture, ink, 58, 58, 98, 92, 8);

            // Arrowhead, drawn as a triangle of narrowing rows.
            for (var i = 0; i < 24; i++)
                FillRect(texture, ink, 98 - i, 92 - i, (i * 2) + 2, 5);

            texture.Apply();

            var full = Path.Combine(Directory.GetCurrentDirectory(), skillPath);
            File.WriteAllBytes(full, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(skillPath, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(skillPath) is TextureImporter skillImporter)
            {
                skillImporter.textureType = TextureImporterType.Sprite;
                skillImporter.spriteImportMode = SpriteImportMode.Single;
                skillImporter.alphaIsTransparency = true;
                skillImporter.mipmapEnabled = false;
                skillImporter.filterMode = FilterMode.Bilinear;
                skillImporter.textureCompression = TextureImporterCompression.Uncompressed;
                skillImporter.assetBundleName = "wallstreet";
                skillImporter.assetBundleVariant = "unity3d";
                skillImporter.SaveAndReimport();
            }

            AssetDatabase.Refresh();
            Debug.Log("[CI] Broker skill icon written to " + skillPath);
        }

        private static void DrawLine(Texture2D t, Color c, int x0, int y0, int x1, int y1, int thickness)
        {
            var steps = Mathf.Max(Mathf.Abs(x1 - x0), Mathf.Abs(y1 - y0));

            for (var i = 0; i <= steps; i++)
            {
                var progress = (float)i / steps;
                var x = Mathf.RoundToInt(Mathf.Lerp(x0, x1, progress));
                var y = Mathf.RoundToInt(Mathf.Lerp(y0, y1, progress));
                FillRect(t, c, x - thickness / 2, y - thickness / 2, thickness, thickness);
            }
        }

        private static void DrawCandle(Texture2D t, Color c, int x, int bodyLow, int bodyHigh, int wickLow, int wickHigh)
        {
            const int BodyWidth = 14;
            const int WickWidth = 4;

            FillRect(t, c, x + (BodyWidth - WickWidth) / 2, wickLow, WickWidth, wickHigh - wickLow);
            FillRect(t, c, x, bodyLow, BodyWidth, bodyHigh - bodyLow);
        }

        private static void FillRect(Texture2D t, Color c, int x, int y, int w, int h)
        {
            for (var px = x; px < x + w; px++)
            for (var py = y; py < y + h; py++)
                if (px >= 0 && px < Size && py >= 0 && py < Size)
                    t.SetPixel(px, py, c);
        }
    }
}
#endif
