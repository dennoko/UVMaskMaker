// IMaskExporter.cs - Interface for mask export implementations
using System.Collections.Generic;
using UnityEngine;

namespace Dennoko.UVTools.Services
{
    /// <summary>
    /// Defines the contract for mask export implementations.
    /// Follows Open/Closed Principle - new export formats can be added without modifying existing code.
    /// </summary>
    public interface IMaskExporter
    {
        /// <summary>
        /// Gets the file extension for this exporter (without dot).
        /// </summary>
        string FileExtension { get; }

        /// <summary>
        /// Gets the display name for this export format.
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Exports the mask to a file.
        /// </summary>
        /// <param name="analysis">UV analysis result</param>
        /// <param name="selectedIslands">Set of selected island indices</param>
        /// <param name="settings">Export settings</param>
        /// <param name="path">Output file path</param>
        /// <returns>True if export succeeded</returns>
        bool Export(
            UVAnalysis analysis,
            HashSet<int> selectedIslands,
            ExportSettings settings,
            string path);
    }

    /// <summary>
    /// Settings for mask export operations.
    /// </summary>
    public class ExportSettings
    {
        public int TextureSize = 512;
        public int PixelMargin = 2;
        public bool InvertMask = false;

        // Channel-wise writing
        public bool ChannelWriteEnabled = false;
        public bool WriteR = true;
        public bool WriteG = false;
        public bool WriteB = false;
        public bool WriteA = false;

        // Base texture for channel-wise writing
        public Texture2D BasePNG = null;
    }
}
