using UnityEditor;
using UnityEngine;

namespace Strategy.EditorTools
{
    /// <summary>Music in TempOST is long uncompressed WAV: stream it and compress to Vorbis instead of decoding it all into memory.</summary>
    public class TempOstImport : AssetPostprocessor
    {
        void OnPreprocessAudio()
        {
            if (!assetPath.Contains("/TempOST/")) return;
            var importer = (AudioImporter)assetImporter;
            var s = importer.defaultSampleSettings;
            s.loadType = AudioClipLoadType.Streaming;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.6f;
            importer.defaultSampleSettings = s;
        }
    }
}
