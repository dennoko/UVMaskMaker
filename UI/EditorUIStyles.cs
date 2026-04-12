// EditorUIStyles.cs
// dennoko.dev color schema に基づく Unity IMGUI スタイル / カラー定義。
// Initialize() を OnGUI の先頭で必ず呼ぶこと（ドメインリロード後のテクスチャ再生成に対応）。
using UnityEditor;
using UnityEngine;

namespace Dennoko.UVTools.UI
{
    public static class EditorUIStyles
    {
        // ════════════════════════════════════════════════════════════════════════
        // Colors  (dennoko.dev color schema)
        // ════════════════════════════════════════════════════════════════════════
        public static readonly Color Surface0        = Hex(0x121212); // アプリ背景
        public static readonly Color Surface1        = Hex(0x1e1e1e); // カード・入力欄
        public static readonly Color Surface2        = Hex(0x2c2c2c); // ツールバー・ホバー
        public static readonly Color Outline         = Hex(0x3a3a3a); // 境界線・セパレーター
        public static readonly Color TextPrimary     = Hex(0xffffff); // タイトル
        public static readonly Color TextSecondary   = Hex(0xcccccc); // 本文・ラベル
        public static readonly Color TextTertiary    = Hex(0xaaaaaa); // 補足・見出し
        public static readonly Color TextDisabled    = Hex(0x555555); // 無効状態
        public static readonly Color SemanticError   = Hex(0x9b1b30);
        public static readonly Color SemanticWarning = Hex(0xffb74d);
        public static readonly Color SemanticSuccess = Hex(0x4caf50);
        public static readonly Color SemanticInfo    = Hex(0x64b5f6);
        public static readonly Color AccentBlue      = new Color(0.2f, 0.6f, 1.0f, 1f);

        // ════════════════════════════════════════════════════════════════════════
        // Spacing constants
        // ════════════════════════════════════════════════════════════════════════
        public const float CardSpacing    = 8f;
        public const float InnerSpacing   = 4f;
        public const float SectionSpacing = 12f;
        public const float ButtonSpacing  = 4f;

        // ════════════════════════════════════════════════════════════════════════
        // Texture cache (HideAndDontSave — must be re-created after domain reload)
        // ════════════════════════════════════════════════════════════════════════
        private static bool      _initialized;
        private static Texture2D _texSurface1;
        private static Texture2D _texSurface2;
        private static Texture2D _texCardBorder;     // 3×3 bordered: fill=Surface1 border=Outline
        private static Texture2D _texToolbar;        // solid Surface2
        private static Texture2D _texStatusInfo;
        private static Texture2D _texStatusSuccess;
        private static Texture2D _texStatusError;
        private static Texture2D _texButtonNormal;
        private static Texture2D _texButtonHover;
        private static Texture2D _texButtonActive;
        private static Texture2D _texPrimaryNormal;
        private static Texture2D _texPrimaryHover;

        // ════════════════════════════════════════════════════════════════════════
        // Style cache (rebuilt after domain reload / texture invalidation)
        // ════════════════════════════════════════════════════════════════════════
        private static GUIStyle _cardStyle;
        private static GUIStyle _cardOuterStyle;
        private static GUIStyle _toolbarStyle;
        private static GUIStyle _sectionHeaderStyle;
        private static GUIStyle _sectionHeaderSmallStyle;
        private static GUIStyle _collapsibleHeaderStyle;
        private static GUIStyle _primaryButtonStyle;
        private static GUIStyle _smallButtonStyle;
        private static GUIStyle _centeredLabelStyle;
        private static GUIStyle _statusInfoStyle;
        private static GUIStyle _statusSuccessStyle;
        private static GUIStyle _statusErrorStyle;
        private static GUIStyle _titleStyle;
        private static GUIStyle _captionStyle;

        // ════════════════════════════════════════════════════════════════════════
        // Lifecycle
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// OnGUI の先頭で呼ぶ。ドメインリロード後にテクスチャ・スタイルを再構築する。
        /// </summary>
        public static void Initialize()
        {
            // textures become null after domain reload even if _initialized==true
            if (_initialized && _texCardBorder) return;

            // Invalidate all style caches (they hold texture refs that are now stale)
            _cardStyle               = null;
            _cardOuterStyle          = null;
            _toolbarStyle            = null;
            _sectionHeaderStyle      = null;
            _sectionHeaderSmallStyle = null;
            _collapsibleHeaderStyle  = null;
            _primaryButtonStyle      = null;
            _smallButtonStyle        = null;
            _centeredLabelStyle      = null;
            _statusInfoStyle         = null;
            _statusSuccessStyle      = null;
            _statusErrorStyle        = null;
            _titleStyle              = null;
            _captionStyle            = null;

            EnsureTextures();
            _initialized = true;
        }

        private static void EnsureTextures()
        {
            if (!_texSurface1)      _texSurface1      = MakeTex(Surface1);
            if (!_texSurface2)      _texSurface2      = MakeTex(Surface2);
            if (!_texCardBorder)    _texCardBorder    = MakeBorderedTex(Surface1, Outline);
            if (!_texToolbar)       _texToolbar       = MakeTex(Surface2);
            if (!_texStatusInfo)    _texStatusInfo    = MakeTex(Surface1);
            if (!_texStatusSuccess) _texStatusSuccess = MakeTex(Color.Lerp(Surface1, SemanticSuccess, 0.25f));
            if (!_texStatusError)   _texStatusError   = MakeTex(Color.Lerp(Surface1, SemanticError,   0.45f));
            if (!_texButtonNormal)  _texButtonNormal  = MakeBorderedTex(Surface1, Outline);
            if (!_texButtonHover)   _texButtonHover   = MakeBorderedTex(Surface2, Outline);
            if (!_texButtonActive)  _texButtonActive  = MakeTex(Color.Lerp(Surface2, Color.white, 0.12f));
            if (!_texPrimaryNormal) _texPrimaryNormal = MakeBorderedTex(Surface2, new Color(0.35f, 0.6f, 1f, 1f));
            if (!_texPrimaryHover)  _texPrimaryHover  = MakeBorderedTex(
                Color.Lerp(Surface2, new Color(0.2f, 0.5f, 0.9f, 1f), 0.25f),
                new Color(0.4f, 0.7f, 1f, 1f));
        }

        // ════════════════════════════════════════════════════════════════════════
        // Style properties (lazy-initialized, auto-rebuild on null)
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>Surface1 背景 + Outline ボーダーのカードスタイル。</summary>
        public static GUIStyle CardStyle
        {
            get
            {
                if (_cardStyle == null)
                {
                    _cardStyle = new GUIStyle
                    {
                        normal  = { background = _texCardBorder },
                        border  = new RectOffset(1, 1, 1, 1),
                        padding = new RectOffset(10, 10, 8, 8),
                        margin  = new RectOffset(8, 8, 4, 4),
                    };
                }
                return _cardStyle;
            }
        }

        /// <summary>padding=0 の外枠カード（ツールバー付きカード用）。</summary>
        public static GUIStyle CardOuterStyle
        {
            get
            {
                if (_cardOuterStyle == null)
                {
                    _cardOuterStyle = new GUIStyle
                    {
                        normal  = { background = _texCardBorder },
                        border  = new RectOffset(1, 1, 1, 1),
                        padding = new RectOffset(0, 0, 0, 0),
                        margin  = new RectOffset(8, 8, 4, 4),
                    };
                }
                return _cardOuterStyle;
            }
        }

        /// <summary>プレビューエリア上部などで使うツールバー背景スタイル。</summary>
        public static GUIStyle ToolbarStyle
        {
            get
            {
                if (_toolbarStyle == null)
                {
                    _toolbarStyle = new GUIStyle
                    {
                        normal  = { background = _texToolbar },
                        padding = new RectOffset(8, 8, 4, 4),
                        margin  = new RectOffset(0, 0, 0, 0),
                    };
                }
                return _toolbarStyle;
            }
        }

        /// <summary>セクション見出し（太字、TextPrimary）。</summary>
        public static GUIStyle SectionHeaderStyle
        {
            get
            {
                if (_sectionHeaderStyle == null)
                {
                    _sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 12,
                        margin   = new RectOffset(0, 0, 2, 6),
                    };
                    _sectionHeaderStyle.normal.textColor = TextPrimary;
                }
                return _sectionHeaderStyle;
            }
        }

        /// <summary>小さいセクション見出し（ツールバー内などに使用、TextTertiary）。</summary>
        public static GUIStyle SectionHeaderSmallStyle
        {
            get
            {
                if (_sectionHeaderSmallStyle == null)
                {
                    _sectionHeaderSmallStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize = 10,
                    };
                    _sectionHeaderSmallStyle.normal.textColor = TextTertiary;
                }
                return _sectionHeaderSmallStyle;
            }
        }

        /// <summary>折りたたみヘッダー用 Foldout スタイル。</summary>
        public static GUIStyle CollapsibleHeaderStyle
        {
            get
            {
                if (_collapsibleHeaderStyle == null)
                {
                    _collapsibleHeaderStyle = new GUIStyle(EditorStyles.foldout)
                    {
                        fontStyle = FontStyle.Bold,
                        margin    = new RectOffset(0, 0, 4, 4),
                    };
                    _collapsibleHeaderStyle.normal.textColor    = TextSecondary;
                    _collapsibleHeaderStyle.onNormal.textColor  = TextPrimary;
                    _collapsibleHeaderStyle.focused.textColor   = TextPrimary;
                    _collapsibleHeaderStyle.onFocused.textColor = TextPrimary;
                }
                return _collapsibleHeaderStyle;
            }
        }

        /// <summary>プライマリアクションボタン（Save など）。</summary>
        public static GUIStyle PrimaryButtonStyle
        {
            get
            {
                if (_primaryButtonStyle == null)
                {
                    _primaryButtonStyle = new GUIStyle(GUI.skin.button)
                    {
                        fontSize    = 13,
                        fontStyle   = FontStyle.Bold,
                        fixedHeight = 34,
                        alignment   = TextAnchor.MiddleCenter,
                        border      = new RectOffset(1, 1, 1, 1),
                    };
                    _primaryButtonStyle.normal.background  = _texPrimaryNormal;
                    _primaryButtonStyle.normal.textColor   = TextPrimary;
                    _primaryButtonStyle.hover.background   = _texPrimaryHover;
                    _primaryButtonStyle.hover.textColor    = TextPrimary;
                    _primaryButtonStyle.active.background  = _texButtonActive;
                    _primaryButtonStyle.active.textColor   = TextPrimary;
                }
                return _primaryButtonStyle;
            }
        }

        /// <summary>小さいインラインボタン（Invert / Select All などに使用）。</summary>
        public static GUIStyle SmallButtonStyle
        {
            get
            {
                if (_smallButtonStyle == null)
                {
                    _smallButtonStyle = new GUIStyle(EditorStyles.miniButton)
                    {
                        fixedHeight = 22,
                        padding     = new RectOffset(8, 8, 2, 2),
                    };
                    _smallButtonStyle.normal.textColor  = TextSecondary;
                    _smallButtonStyle.hover.textColor   = TextPrimary;
                    _smallButtonStyle.active.textColor  = TextPrimary;
                }
                return _smallButtonStyle;
            }
        }

        /// <summary>ヒントや空状態のラベル（センタリング・グレー）。</summary>
        public static GUIStyle CenteredLabelStyle
        {
            get
            {
                if (_centeredLabelStyle == null)
                {
                    _centeredLabelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        wordWrap  = true,
                    };
                    _centeredLabelStyle.normal.textColor = TextTertiary;
                }
                return _centeredLabelStyle;
            }
        }

        /// <summary>ウィンドウタイトルスタイル。</summary>
        public static GUIStyle TitleStyle
        {
            get
            {
                if (_titleStyle == null)
                {
                    _titleStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        fontSize  = 14,
                        alignment = TextAnchor.MiddleLeft,
                    };
                    _titleStyle.normal.textColor = TextPrimary;
                }
                return _titleStyle;
            }
        }

        /// <summary>キャプション・補足テキスト用。</summary>
        public static GUIStyle CaptionStyle
        {
            get
            {
                if (_captionStyle == null)
                {
                    _captionStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleLeft,
                    };
                    _captionStyle.normal.textColor = TextTertiary;
                }
                return _captionStyle;
            }
        }

        // --- Status bar styles ---

        public static GUIStyle StatusInfoStyle
        {
            get
            {
                if (_statusInfoStyle == null)
                    _statusInfoStyle = MakeStatusStyle(_texStatusInfo, TextSecondary);
                return _statusInfoStyle;
            }
        }

        public static GUIStyle StatusSuccessStyle
        {
            get
            {
                if (_statusSuccessStyle == null)
                    _statusSuccessStyle = MakeStatusStyle(_texStatusSuccess, SemanticSuccess);
                return _statusSuccessStyle;
            }
        }

        public static GUIStyle StatusErrorStyle
        {
            get
            {
                if (_statusErrorStyle == null)
                    _statusErrorStyle = MakeStatusStyle(_texStatusError, new Color(1f, 0.65f, 0.65f));
                return _statusErrorStyle;
            }
        }

        // ════════════════════════════════════════════════════════════════════════
        // Section helpers
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>カードスタイルの縦グループを開始する（任意タイトル付き）。</summary>
        public static void BeginCard(string title = null)
        {
            EditorGUILayout.BeginVertical(CardStyle);
            if (!string.IsNullOrEmpty(title))
                EditorGUILayout.LabelField(title, SectionHeaderStyle);
        }

        /// <summary>カードスタイルの縦グループを終了する。</summary>
        public static void EndCard()
        {
            EditorGUILayout.EndVertical();
        }

        /// <summary>折りたたみヘッダーを描画する。展開中なら true を返す。</summary>
        public static bool DrawCollapsibleHeader(string title, bool isExpanded, string tooltip = null)
        {
            var content = tooltip != null ? new GUIContent(title, tooltip) : new GUIContent(title);
            return EditorGUILayout.Foldout(isExpanded, content, true, CollapsibleHeaderStyle);
        }

        /// <summary>1px の横区切り線を描画する。</summary>
        public static void DrawSeparator()
        {
            EditorGUILayout.Space(4);
            var rect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(rect, Outline);
            EditorGUILayout.Space(4);
        }

        /// <summary>等幅ボタンバーを描画する。クリックされたボタンのインデックスを返す（なければ -1）。</summary>
        public static int DrawButtonBar(params GUIContent[] buttons)
        {
            int clicked = -1;
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < buttons.Length; i++)
                    if (GUILayout.Button(buttons[i], SmallButtonStyle))
                        clicked = i;
            }
            return clicked;
        }

        /// <summary>センタリングされたプライマリボタンを描画する。</summary>
        public static bool DrawPrimaryButton(string text, string tooltip = null, float width = 140)
        {
            bool clicked = false;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                var content = tooltip != null ? new GUIContent(text, tooltip) : new GUIContent(text);
                if (GUILayout.Button(content, PrimaryButtonStyle, GUILayout.Width(width)))
                    clicked = true;
                GUILayout.FlexibleSpace();
            }
            return clicked;
        }

        /// <summary>センタリングされたセカンダリボタンを描画する。</summary>
        public static bool DrawSecondaryButton(string text, string tooltip = null, float width = 180)
        {
            bool clicked = false;
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                var content = tooltip != null ? new GUIContent(text, tooltip) : new GUIContent(text);
                if (GUILayout.Button(content, GUILayout.Width(width), GUILayout.Height(24)))
                    clicked = true;
                GUILayout.FlexibleSpace();
            }
            return clicked;
        }

        /// <summary>ToggleLeft をスタイルに沿って描画する。</summary>
        public static bool DrawToggle(bool value, string label, string tooltip = null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8);
                var content = tooltip != null ? new GUIContent(label, tooltip) : new GUIContent(label);
                return EditorGUILayout.ToggleLeft(content, value);
            }
        }

        /// <summary>2カラム（ラベル＋フィールド）の行を開始する。</summary>
        public static void BeginCompactRow(string label, string tooltip = null, float labelWidth = 100)
        {
            EditorGUILayout.BeginHorizontal();
            var content = tooltip != null ? new GUIContent(label, tooltip) : new GUIContent(label);
            EditorGUILayout.LabelField(content, GUILayout.Width(labelWidth));
        }

        /// <summary>BeginCompactRow の終了。</summary>
        public static void EndCompactRow() => EditorGUILayout.EndHorizontal();

        // ════════════════════════════════════════════════════════════════════════
        // Texture utilities
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>単色 1×1 テクスチャを生成する。</summary>
        public static Texture2D MakeTex(Color color)
        {
            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            tex.hideFlags  = HideFlags.HideAndDontSave;
            return tex;
        }

        /// <summary>3×3 ボーダーテクスチャを生成する（fill + 1px border）。</summary>
        public static Texture2D MakeBorderedTex(Color fill, Color border)
        {
            const int s = 3;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                    tex.SetPixel(x, y, (x == 0 || x == s-1 || y == 0 || y == s-1) ? border : fill);
            tex.Apply();
            tex.filterMode = FilterMode.Point;
            tex.hideFlags  = HideFlags.HideAndDontSave;
            return tex;
        }

        // ════════════════════════════════════════════════════════════════════════
        // Private helpers
        // ════════════════════════════════════════════════════════════════════════

        private static GUIStyle MakeStatusStyle(Texture2D bg, Color textColor)
        {
            var s = new GUIStyle(EditorStyles.helpBox)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize  = 11,
                padding   = new RectOffset(10, 10, 4, 4),
                margin    = new RectOffset(0, 0, 0, 0),
                border    = new RectOffset(1, 1, 1, 1),
            };
            s.normal.background = bg;
            s.normal.textColor  = textColor;
            return s;
        }

        /// <summary>0xRRGGBB → Color (alpha=1)。</summary>
        private static Color Hex(int rgb) => new Color(
            ((rgb >> 16) & 0xFF) / 255f,
            ((rgb >>  8) & 0xFF) / 255f,
            ( rgb        & 0xFF) / 255f);
    }
}
