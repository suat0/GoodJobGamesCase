#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace BlastGame.Game.EditorTools
{
    // How the HUD's font asset was produced. The asset itself is committed, so nothing needs this to
    // open the project - it is here because the settings behind a generated asset are not readable
    // from the asset, and a font rebuilt with different ones renders visibly differently.
    public static class PolishSetup
    {
        // Builds the HUD's font asset from the TTF. Done in code so the atlas settings are recorded
        // rather than remembered: a font asset rebuilt by hand with a different padding or sampling
        // size renders visibly differently.
        public static void CreateFontAsset()
        {
            const string Source = "Assets/Fonts/Baloo2-ExtraBold.ttf";
            const string Output = "Assets/Fonts/Baloo2-ExtraBold SDF.asset";

            var font = AssetDatabase.LoadAssetAtPath<Font>(Source);
            if (font == null) throw new System.IO.FileNotFoundException(Source);

            // 90pt into a 1024 atlas leaves the HUD's largest label sharp without a second atlas page.
            TMPro.TMP_FontAsset asset = TMPro.TMP_FontAsset.CreateFontAsset(
                font, 90, 9,
                UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                1024, 1024,
                TMPro.AtlasPopulationMode.Dynamic,
                enableMultiAtlasSupport: false);

            AssetDatabase.CreateAsset(asset, Output);

            // Printable ASCII, baked now. A dynamic font asset renders missing glyphs during play,
            // which allocates - the one thing this project measures itself on.
            var characters = new System.Text.StringBuilder(95);
            for (int c = 32; c < 127; c++) characters.Append((char)c);

            if (!asset.TryAddCharacters(characters.ToString(), out string missing))
                Debug.LogWarning($"Font is missing: {missing}");

            // Switched last, and only after the glyphs are in: adding is a dynamic-mode operation, so
            // flipping first leaves an empty atlas and a font that renders nothing.
            asset.atlasPopulationMode = TMPro.AtlasPopulationMode.Static;

            foreach (Texture2D atlas in asset.atlasTextures)
            {
                atlas.name = asset.name + " Atlas";
                AssetDatabase.AddObjectToAsset(atlas, asset);
            }

            asset.material.name = asset.name + " Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Font asset written to {Output} with {asset.characterTable.Count} characters.");
        }
    }
}
#endif
