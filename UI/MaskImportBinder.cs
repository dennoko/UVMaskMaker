// MaskImportBinder.cs - Binds the mask image import card (UI Toolkit)
// Allows drag-and-drop / selection of an existing mask image whose black
// regions are loaded as hand-painted areas in the MaskPainter.
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Dennoko.UVTools.Services;

namespace Dennoko.UVTools.UI
{
    /// <summary>
    /// Binds a card for importing an existing mask image.
    /// Black pixels (below the configured threshold) are treated as
    /// hand-painted regions and merged into the active paint mask.
    /// </summary>
    public class MaskImportBinder
    {
        private readonly LocalizationService _localization;

        private Label _importTitle;
        private ObjectField _importTexField;
        private Button _importClearBtn;
        private SliderInt _thresholdSlider;
        private Button _importLoadBtn;

        public MaskImportBinder(LocalizationService localization)
        {
            _localization = localization;
        }

        /// <summary>
        /// Fired when the user clicks the Load button.
        /// Arguments: the texture to import and the black threshold (0-255).
        /// </summary>
        public event System.Action<Texture2D, int> OnLoadClicked;

        public void Bind(VisualElement root)
        {
            _importTitle     = root.Q<Label>("import-title");
            _importTexField  = root.Q<ObjectField>("import-tex-field");
            _importClearBtn  = root.Q<Button>("import-clear-btn");
            _thresholdSlider = root.Q<SliderInt>("threshold-slider");
            _importLoadBtn   = root.Q<Button>("import-load-btn");

            _importTexField.RegisterValueChangedCallback(evt => RefreshButtons());
            _importClearBtn.clicked += () =>
            {
                _importTexField.value = null;
                RefreshButtons();
            };
            _importLoadBtn.clicked += () =>
                OnLoadClicked?.Invoke(_importTexField.value as Texture2D, _thresholdSlider.value);

            RefreshButtons();
        }

        public void ApplyLocalization()
        {
            _importTitle.text = _localization.Get("mask_import_title", "マスク画像の読み込み");

            _importTexField.label = _localization.Get("mask_import_image", "画像");
            _importTexField.tooltip = _localization.Get(
                "mask_import_image_tooltip", "読み込む既存のマスク画像を指定します。");

            _importClearBtn.text = _localization.Get("clear_button", "Clear");
            _importClearBtn.tooltip = _localization.Get(
                "mask_import_clear_tooltip", "読み込み画像をクリアします。");

            _thresholdSlider.label = _localization.Get("mask_import_threshold", "黒の閾値");
            _thresholdSlider.tooltip = _localization.Get(
                "mask_import_threshold_tooltip", "この値より暗いピクセルを手塗り領域として扱います（0-255）。");

            _importLoadBtn.text = _localization.Get("mask_import_load", "マスクを読み込む");
            _importLoadBtn.tooltip = _localization.Get(
                "mask_import_load_tooltip", "指定した画像の黒い部分を手塗り領域として読み込みます。");
        }

        private void RefreshButtons()
        {
            bool hasTexture = _importTexField.value != null;
            _importClearBtn.SetEnabled(hasTexture);
            _importLoadBtn.SetEnabled(hasTexture);
        }
    }
}
