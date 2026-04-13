// MaskImportDrawer.cs - Draws the mask image import card
// Allows drag-and-drop of an existing mask image whose black regions
// are loaded as hand-painted areas in the MaskPainter.
using UnityEditor;
using UnityEngine;
using Dennoko.UVTools.Services;

namespace Dennoko.UVTools.UI
{
    /// <summary>
    /// Draws a card for importing an existing mask image.
    /// Black pixels (below the configured threshold) are treated as
    /// hand-painted regions and merged into the active paint mask.
    /// </summary>
    public class MaskImportDrawer
    {
        private readonly LocalizationService _localization;

        private Texture2D _importTex;
        private int _blackThreshold = 128; // 0-255: pixels darker than this are treated as "black" (default middle value)

        public MaskImportDrawer(LocalizationService localization)
        {
            _localization = localization;
        }

        /// <summary>
        /// Fired when the user clicks the Load button.
        /// Arguments: the texture to import and the black threshold (0-255).
        /// </summary>
        public event System.Action<Texture2D, int> OnLoadClicked;

        /// <summary>
        /// Draws the mask import card.
        /// </summary>
        public void Draw()
        {
            EditorUIStyles.BeginCard(_localization.Get("mask_import_title", "マスク画像の読み込み"));

            // Drag-and-drop area
            var dropRect = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, _localization.Get("mask_import_drop_hint", "マスク画像をここへドラッグ"), EditorStyles.helpBox);
            HandleDragAndDrop(dropRect);

            EditorGUILayout.Space(4);

            // Texture object field + Clear button
            using (new EditorGUILayout.HorizontalScope())
            {
                var newTex = EditorGUILayout.ObjectField(
                    new GUIContent(
                        _localization.Get("mask_import_image", "画像"),
                        _localization.Get("mask_import_image_tooltip", "読み込む既存のマスク画像を指定します。")),
                    _importTex,
                    typeof(Texture2D),
                    false) as Texture2D;

                if (newTex != _importTex)
                {
                    _importTex = newTex;
                }

                GUI.enabled = _importTex != null;
                if (GUILayout.Button(
                    new GUIContent(
                        _localization.Get("clear_button", "Clear"),
                        _localization.Get("mask_import_clear_tooltip", "読み込み画像をクリアします。")),
                    EditorUIStyles.SmallButtonStyle,
                    GUILayout.Width(60)))
                {
                    _importTex = null;
                }
                GUI.enabled = true;
            }

            EditorGUILayout.Space(4);

            // Threshold slider
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(
                    new GUIContent(
                        _localization.Get("mask_import_threshold", "黒の閾値"),
                        _localization.Get("mask_import_threshold_tooltip", "この値より暗いピクセルを手塗り領域として扱います（0-255）。")),
                    GUILayout.Width(90));
                _blackThreshold = EditorGUILayout.IntSlider(_blackThreshold, 0, 255);
            }

            EditorGUILayout.Space(4);

            // Load button
            GUI.enabled = _importTex != null;
            if (EditorUIStyles.DrawSecondaryButton(
                _localization.Get("mask_import_load", "マスクを読み込む"),
                _localization.Get("mask_import_load_tooltip", "指定した画像の黒い部分を手塗り領域として読み込みます。")))
            {
                OnLoadClicked?.Invoke(_importTex, _blackThreshold);
            }
            GUI.enabled = true;

            EditorUIStyles.EndCard();
        }

        private void HandleDragAndDrop(Rect dropRect)
        {
            var evt = Event.current;
            if (!dropRect.Contains(evt.mousePosition)) return;

            if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
            {
                // Accept only if at least one dragged object is a Texture2D
                bool hasTexture = false;
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj is Texture2D)
                    {
                        hasTexture = true;
                        break;
                    }
                }

                if (!hasTexture) return;

                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (var obj in DragAndDrop.objectReferences)
                    {
                        if (obj is Texture2D tex)
                        {
                            _importTex = tex;
                            break;
                        }
                    }
                }
                evt.Use();
            }
        }
    }
}
