// AdvancedOptionsDrawer.cs - Draws collapsible advanced/optional settings
using UnityEditor;
using UnityEngine;
using Dennoko.UVTools.Data;
using Dennoko.UVTools.Services;

namespace Dennoko.UVTools.UI
{
    /// <summary>
    /// Draws collapsible advanced options sections.
    /// These are hidden by default to reduce UI noise.
    /// </summary>
    public class AdvancedOptionsDrawer
    {
        private readonly LocalizationService _localization;

        // Foldout states
        private bool _overlayExpanded = false;
        private bool _channelWriteExpanded = false;
        private bool _vertexColorExpanded = false;

        public AdvancedOptionsDrawer(LocalizationService localization)
        {
            _localization = localization;
        }

        // Scene overlay events
        public event System.Action<int> OnUVChannelChanged;
        public event System.Action<bool> OnOverlayOnTopChanged;
        public event System.Action<bool> OnDisableAAChanged;
        public event System.Action<bool> OnBackfaceCullChanged;
        public event System.Action<float> OnThicknessChanged;
        public event System.Action<float> OnDepthOffsetChanged;
        public event System.Action<Color> OnSeamColorChanged;
        public event System.Action<Color> OnSelectedColorChanged;
        public event System.Action<Color> OnPreviewFillColorChanged;
        public event System.Action<float> OnOverlayAlphaChanged;
        public event System.Action<bool> OnShowIslandPreviewChanged;
        public event System.Action<bool> OnPreviewOverlayBaseChanged;

        // Channel write events
        public event System.Action<bool> OnChannelWriteEnabledChanged;
        public event System.Action<Texture2D> OnBasePNGChanged;
        public event System.Action<bool, bool, bool, bool> OnChannelsChanged;

        // Vertex color events
        public event System.Action OnBakeVertexColorClicked;
        public event System.Action<Mesh> OnBaseVCMeshChanged;
        public event System.Action<bool> OnOverwriteExistingChanged;

        /// <summary>
        /// Draws scene overlay options (collapsible).
        /// </summary>
        public void DrawOverlaySection(MaskSettings settings, Texture baseTexture)
        {
            _overlayExpanded = EditorUIStyles.DrawCollapsibleHeader(
                "🎨 " + _localization["scene_overlay"],
                _overlayExpanded,
                _localization["advanced_options_tooltip"]);

            if (!_overlayExpanded) return;

            using (new EditorGUI.IndentLevelScope())
            using (new EditorGUILayout.VerticalScope(EditorUIStyles.CardStyle))
            {
                // UV Channel
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        new GUIContent(_localization["uv_channel"], _localization["uv_channel_tooltip"]),
                        GUILayout.Width(80));

                    int newUv = EditorGUILayout.Popup(settings.UVChannel,
                        new[] { "UV0", "UV1", "UV2", "UV3", "UV4", "UV5", "UV6", "UV7" },
                        GUILayout.Width(60));

                    if (newUv != settings.UVChannel)
                    {
                        OnUVChannelChanged?.Invoke(newUv);
                    }

                    GUILayout.FlexibleSpace();
                }

                EditorGUILayout.Space(4);

                // Display options
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool onTop = EditorUIStyles.DrawToggle(settings.OverlayOnTop,
                        _localization["overlay_on_top"], _localization["overlay_on_top_tooltip"]);
                    if (onTop != settings.OverlayOnTop)
                    {
                        OnOverlayOnTopChanged?.Invoke(onTop);
                    }

                    bool disableAA = EditorUIStyles.DrawToggle(settings.DisableAA,
                        _localization["disable_aa"], _localization["disable_aa_tooltip"]);
                    if (disableAA != settings.DisableAA)
                    {
                        OnDisableAAChanged?.Invoke(disableAA);
                    }
                }

                bool bfc = EditorUIStyles.DrawToggle(settings.BackfaceCull,
                    _localization["backface_cull"], _localization["backface_cull_tooltip"]);
                if (bfc != settings.BackfaceCull)
                {
                    OnBackfaceCullChanged?.Invoke(bfc);
                }

                EditorGUILayout.Space(4);

                // Thickness and depth
                float th = EditorGUILayout.Slider(
                    new GUIContent(_localization["thickness"], _localization["thickness_tooltip"]),
                    settings.OverlaySeamThickness, 1f, 8f);
                if (!Mathf.Approximately(th, settings.OverlaySeamThickness))
                {
                    OnThicknessChanged?.Invoke(th);
                }

                float depthMm = settings.OverlayDepthOffset * 1000f;
                float newDepthMm = EditorGUILayout.Slider(
                    new GUIContent(_localization["depth_offset"], _localization["depth_offset_tooltip"]),
                    depthMm, 0f, 20f);
                if (!Mathf.Approximately(newDepthMm, depthMm))
                {
                    OnDepthOffsetChanged?.Invoke(newDepthMm / 1000f);
                }

                EditorUIStyles.DrawSeparator();

                // Colors
                EditorGUILayout.LabelField(_localization["color_options"], EditorStyles.boldLabel);

                var selCol = EditorGUILayout.ColorField(
                    new GUIContent(_localization["selected_islands_color"], _localization["selected_islands_color_tooltip"]),
                    settings.SelectedSceneColor);
                if (selCol != settings.SelectedSceneColor)
                {
                    OnSelectedColorChanged?.Invoke(selCol);
                }

                var seamCol = EditorGUILayout.ColorField(
                    new GUIContent(_localization["seam_color"], _localization["seam_color_tooltip"]),
                    settings.SeamColor);
                if (seamCol != settings.SeamColor)
                {
                    OnSeamColorChanged?.Invoke(seamCol);
                }

                var pfill = EditorGUILayout.ColorField(
                    new GUIContent(_localization["preview_fill_color"], _localization["preview_fill_color_tooltip"]),
                    settings.PreviewFillSelectedColor);
                if (pfill != settings.PreviewFillSelectedColor)
                {
                    OnPreviewFillColorChanged?.Invoke(pfill);
                }

                float alpha = EditorGUILayout.Slider(
                    new GUIContent(_localization["overlay_alpha"], _localization["overlay_alpha_tooltip"]),
                    settings.PreviewOverlayAlpha, 0f, 1f);
                if (!Mathf.Approximately(alpha, settings.PreviewOverlayAlpha))
                {
                    OnOverlayAlphaChanged?.Invoke(alpha);
                }

                EditorUIStyles.DrawSeparator();

                // Preview options
                bool showIsland = EditorUIStyles.DrawToggle(settings.ShowIslandPreview,
                    _localization["show_island_preview"], _localization["show_island_preview_tooltip"]);
                if (showIsland != settings.ShowIslandPreview)
                {
                    OnShowIslandPreviewChanged?.Invoke(showIsland);
                }

                using (new EditorGUI.DisabledScope(baseTexture == null))
                {
                    bool overlay = EditorUIStyles.DrawToggle(settings.PreviewOverlayBaseTex,
                        _localization["preview_overlay_base"], _localization["preview_overlay_base_tooltip"]);
                    if (overlay != settings.PreviewOverlayBaseTex)
                    {
                        OnPreviewOverlayBaseChanged?.Invoke(overlay);
                    }
                }
            }
        }

        /// <summary>
        /// Draws channel-wise write options (collapsible).
        /// </summary>
        public void DrawChannelWriteSection(MaskSettings settings, Texture2D basePNG)
        {
            _channelWriteExpanded = EditorUIStyles.DrawCollapsibleHeader(
                "📝 " + _localization["channel_write"],
                _channelWriteExpanded,
                _localization["channel_write_tooltip"]);

            if (!_channelWriteExpanded) return;

            using (new EditorGUI.IndentLevelScope())
            using (new EditorGUILayout.VerticalScope(EditorUIStyles.CardStyle))
            {
                bool ch = EditorUIStyles.DrawToggle(settings.ChannelWriteEnabled,
                    _localization["channel_write_enabled"], _localization["channel_write_enabled_tooltip"]);
                if (ch != settings.ChannelWriteEnabled)
                {
                    OnChannelWriteEnabledChanged?.Invoke(ch);
                }

                using (new EditorGUI.DisabledScope(!settings.ChannelWriteEnabled))
                {
                    EditorGUIUtility.labelWidth = 80;
                    var newBase = EditorGUILayout.ObjectField(
                        new GUIContent(_localization["base_png"], _localization["base_png_tooltip"]),
                        basePNG, typeof(Texture2D), false, GUILayout.MaxWidth(250)) as Texture2D;
                    EditorGUIUtility.labelWidth = 0;
                    if (newBase != basePNG)
                    {
                        OnBasePNGChanged?.Invoke(newBase);
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(_localization["write_channels"], GUILayout.Width(100));
                        bool r = GUILayout.Toggle(settings.WriteR, "R", GUILayout.Width(30));
                        bool g = GUILayout.Toggle(settings.WriteG, "G", GUILayout.Width(30));
                        bool b = GUILayout.Toggle(settings.WriteB, "B", GUILayout.Width(30));
                        bool a = GUILayout.Toggle(settings.WriteA, "A", GUILayout.Width(30));
                        GUILayout.FlexibleSpace();

                        if (r != settings.WriteR || g != settings.WriteG || b != settings.WriteB || a != settings.WriteA)
                        {
                            OnChannelsChanged?.Invoke(r, g, b, a);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Draws vertex color bake options (collapsible).
        /// </summary>
        public void DrawVertexColorSection(MaskSettings settings, Mesh baseVCMesh, bool hasAnalysis)
        {
            _vertexColorExpanded = EditorUIStyles.DrawCollapsibleHeader(
                "🎯 " + _localization["vertex_color_bake"],
                _vertexColorExpanded,
                _localization["bake_to_vertex_colors_tooltip"]);

            if (!_vertexColorExpanded) return;

            using (new EditorGUI.IndentLevelScope())
            using (new EditorGUILayout.VerticalScope(EditorUIStyles.CardStyle))
            {
                var newBaseMesh = EditorGUILayout.ObjectField(
                    new GUIContent(_localization["base_vc_mesh"], _localization["base_vc_mesh_tooltip"]),
                    baseVCMesh, typeof(Mesh), false) as Mesh;
                if (newBaseMesh != baseVCMesh)
                {
                    OnBaseVCMeshChanged?.Invoke(newBaseMesh);
                }

                bool overwrite = EditorUIStyles.DrawToggle(settings.OverwriteExistingVC,
                    _localization["overwrite_existing"], _localization["overwrite_existing_tooltip"]);
                if (overwrite != settings.OverwriteExistingVC)
                {
                    OnOverwriteExistingChanged?.Invoke(overwrite);
                }

                EditorGUILayout.Space(4);

                GUI.enabled = hasAnalysis;
                if (GUILayout.Button(
                    new GUIContent(_localization["bake_to_vertex_colors"], _localization["bake_to_vertex_colors_tooltip"]),
                    EditorUIStyles.SmallButtonStyle, GUILayout.Height(24)))
                {
                    OnBakeVertexColorClicked?.Invoke();
                }
                GUI.enabled = true;
            }
        }
    }
}
