// EditorUIStyles.cs - Shared styles for UV Mask Maker editor UI
using UnityEditor;
using UnityEngine;

namespace Dennoko.UVTools.UI
{
    /// <summary>
    /// Provides shared styles and colors for the UV Mask Maker editor window.
    /// Creates a modern, clean UI appearance with cards and consistent styling.
    /// </summary>
    public static class EditorUIStyles
    {
        // Colors
        private static readonly Color CardBackgroundLight = new Color(0.22f, 0.22f, 0.22f, 1f);
        private static readonly Color CardBackgroundDark = new Color(0.18f, 0.18f, 0.18f, 1f);
        private static readonly Color HeaderColor = new Color(0.3f, 0.6f, 0.9f, 1f);
        private static readonly Color AccentColor = new Color(0.4f, 0.8f, 0.4f, 1f);
        private static readonly Color BorderColor = new Color(0.35f, 0.35f, 0.35f, 1f);

        // Standard spacing constants
        /// <summary>Space between cards/sections</summary>
        public const float CardSpacing = 8f;
        /// <summary>Space within cards between elements</summary>
        public const float InnerSpacing = 4f;
        /// <summary>Larger space between major sections</summary>
        public const float SectionSpacing = 12f;
        /// <summary>Space between buttons in a row</summary>
        public const float ButtonSpacing = 4f;

        // Cached styles
        private static GUIStyle _cardStyle;
        private static GUIStyle _sectionHeaderStyle;
        private static GUIStyle _collapsibleHeaderStyle;
        private static GUIStyle _primaryButtonStyle;
        private static GUIStyle _smallButtonStyle;
        private static GUIStyle _centeredLabelStyle;
        private static GUIStyle _helpBoxStyle;

        /// <summary>
        /// Card-style box for grouping related content.
        /// </summary>
        public static GUIStyle CardStyle
        {
            get
            {
                if (_cardStyle == null)
                {
                    _cardStyle = new GUIStyle(EditorStyles.helpBox)
                    {
                        margin = new RectOffset(4, 4, 4, 4),
                        padding = new RectOffset(10, 10, 8, 8),
                    };
                }
                return _cardStyle;
            }
        }

        /// <summary>
        /// Bold header style for section titles.
        /// </summary>
        public static GUIStyle SectionHeaderStyle
        {
            get
            {
                if (_sectionHeaderStyle == null)
                {
                    _sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 12,
                        margin = new RectOffset(0, 0, 2, 6),
                    };
                }
                return _sectionHeaderStyle;
            }
        }

        /// <summary>
        /// Foldout-style header for collapsible sections.
        /// </summary>
        public static GUIStyle CollapsibleHeaderStyle
        {
            get
            {
                if (_collapsibleHeaderStyle == null)
                {
                    _collapsibleHeaderStyle = new GUIStyle(EditorStyles.foldout)
                    {
                        fontStyle = FontStyle.Bold,
                        margin = new RectOffset(0, 0, 4, 4),
                    };
                }
                return _collapsibleHeaderStyle;
            }
        }

        /// <summary>
        /// Primary action button style (e.g., Save button).
        /// </summary>
        public static GUIStyle PrimaryButtonStyle
        {
            get
            {
                if (_primaryButtonStyle == null)
                {
                    _primaryButtonStyle = new GUIStyle(GUI.skin.button)
                    {
                        fontStyle = FontStyle.Bold,
                        fontSize = 12,
                        fixedHeight = 28,
                    };
                }
                return _primaryButtonStyle;
            }
        }

        /// <summary>
        /// Small inline button style.
        /// </summary>
        public static GUIStyle SmallButtonStyle
        {
            get
            {
                if (_smallButtonStyle == null)
                {
                    _smallButtonStyle = new GUIStyle(EditorStyles.miniButton)
                    {
                        fixedHeight = 22,
                        padding = new RectOffset(8, 8, 2, 2),
                    };
                }
                return _smallButtonStyle;
            }
        }

        /// <summary>
        /// Centered label for hints and messages.
        /// </summary>
        public static GUIStyle CenteredLabelStyle
        {
            get
            {
                if (_centeredLabelStyle == null)
                {
                    _centeredLabelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        wordWrap = true,
                    };
                }
                return _centeredLabelStyle;
            }
        }

        /// <summary>
        /// Draws a card-style section with header.
        /// Returns the content rect inside the card.
        /// </summary>
        public static void BeginCard(string title = null)
        {
            EditorGUILayout.BeginVertical(CardStyle);
            if (!string.IsNullOrEmpty(title))
            {
                EditorGUILayout.LabelField(title, SectionHeaderStyle);
            }
        }

        /// <summary>
        /// Ends a card-style section.
        /// </summary>
        public static void EndCard()
        {
            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// Draws a collapsible section header. Returns true if expanded.
        /// </summary>
        public static bool DrawCollapsibleHeader(string title, bool isExpanded, string tooltip = null)
        {
            var content = tooltip != null ? new GUIContent(title, tooltip) : new GUIContent(title);
            return EditorGUILayout.Foldout(isExpanded, content, true, CollapsibleHeaderStyle);
        }

        /// <summary>
        /// Draws a horizontal separator line.
        /// </summary>
        public static void DrawSeparator()
        {
            EditorGUILayout.Space(4);
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, BorderColor);
            EditorGUILayout.Space(4);
        }

        /// <summary>
        /// Draws a button bar with evenly spaced buttons.
        /// </summary>
        public static int DrawButtonBar(params GUIContent[] buttons)
        {
            int clicked = -1;
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < buttons.Length; i++)
                {
                    if (GUILayout.Button(buttons[i], SmallButtonStyle))
                    {
                        clicked = i;
                    }
                }
            }
            return clicked;
        }

        /// <summary>
        /// Draws a primary action button centered.
        /// </summary>
        public static bool DrawPrimaryButton(string text, string tooltip = null, float width = 140)
        {
            bool clicked = false;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                var content = tooltip != null ? new GUIContent(text, tooltip) : new GUIContent(text);
                if (GUILayout.Button(content, PrimaryButtonStyle, GUILayout.Width(width)))
                {
                    clicked = true;
                }
                GUILayout.FlexibleSpace();
            }
            return clicked;
        }

        /// <summary>
        /// Draws a secondary action button centered.
        /// </summary>
        public static bool DrawSecondaryButton(string text, string tooltip = null, float width = 180)
        {
            bool clicked = false;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                var content = tooltip != null ? new GUIContent(text, tooltip) : new GUIContent(text);
                // Use standard button style (not bold)
                if (GUILayout.Button(content, GUILayout.Width(width), GUILayout.Height(24)))
                {
                    clicked = true;
                }
                GUILayout.FlexibleSpace();
            }
            return clicked;
        }

        /// <summary>
        /// Draws a toggle with improved styling.
        /// </summary>
        public static bool DrawToggle(bool value, string label, string tooltip = null)
        {
            var content = tooltip != null ? new GUIContent(label, tooltip) : new GUIContent(label);
            return EditorGUILayout.ToggleLeft(content, value);
        }

        /// <summary>
        /// Draws a compact two-column layout for label + field.
        /// </summary>
        public static void BeginCompactRow(string label, string tooltip = null, float labelWidth = 100)
        {
            EditorGUILayout.BeginHorizontal();
            var content = tooltip != null ? new GUIContent(label, tooltip) : new GUIContent(label);
            EditorGUILayout.LabelField(content, GUILayout.Width(labelWidth));
        }

        /// <summary>
        /// Ends a compact row.
        /// </summary>
        public static void EndCompactRow()
        {
            EditorGUILayout.EndHorizontal();
        }
    }
}
