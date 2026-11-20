using System;
using System.Collections.Generic;
using Kitchen;
using KitchenData;
using TMPro;
using UnityEngine;

namespace CopyPaste
{
    /// <summary>
    /// Host's screen only: plain Unity objects, never sent over the network, so online players
    /// without the mod are unaffected (and don't see them). Fed by CopyPasteSystem every frame.
    ///
    ///   Popups   a short label ("Copied", "Pasted", ...) that rises over the appliance and fades.
    ///   Preview  while a local player's crane is over an appliance of a type they copied, and a
    ///            paste would change it: a flat arrow for the direction, and/or a small spinning
    ///            copy of the filter / provided item, the same way vanilla's ConveyItemsView
    ///            shows a smart grabber's filter.
    ///
    /// Sprites are generated at runtime and drawn with SpriteRenderer, and text uses the game's
    /// own TMP font with Overflow (the body font draws nothing when it doesn't fit), the same
    /// approach Blueprint Shop's sale labels use, which renders correctly in this game.
    /// </summary>
    internal class CopyPasteVisuals : MonoBehaviour
    {
        internal struct Preview
        {
            public int PlayerId;
            public Unity.Entities.Entity Target;
            public Vector3 Position;
            public Quaternion Rotation;
            public bool HasDirection;
            public Orientation Direction;
            public bool ShowItem;
            public int Item;
            public ItemList Components;

            public bool SameLookAs(Preview other)
            {
                return Target == other.Target && Position == other.Position && Rotation == other.Rotation
                    && HasDirection == other.HasDirection && Direction == other.Direction
                    && ShowItem == other.ShowItem && Item == other.Item && Components.IsEquivalent(other.Components);
            }
        }

        private class PopupObject
        {
            public Transform Root;
            public TextMeshPro Text;
            public SpriteRenderer Backing;
            public Vector3 Anchor;
            public float Born;
            public Color TextColour;
            public Color BackColour;
        }

        private class PreviewObject
        {
            public Preview Data;
            public GameObject Root;
            public Transform Spinner;
        }

        private const float PopupHeight = 1.9f;
        private const float PopupRise = 0.35f;
        private const float PopupSeconds = 1.4f;
        private const float PopupFadeSeconds = 0.4f;
        private const float PopupTextHeight = 0.32f;

        private const float ArrowHeight = 1.0f;
        private const float ArrowSize = 0.75f;
        private const float ItemHeight = 1.35f;
        private const float ItemScale = 0.55f;
        private const float SpinDegreesPerSecond = 60f;

        private static readonly Color Good = new Color(1f, 1f, 1f, 1f);
        private static readonly Color Bad = new Color(1f, 0.62f, 0.55f, 1f);
        private static readonly Color Back = new Color(0.1f, 0.086f, 0.075f, 0.85f);
        private static readonly Color ArrowColour = new Color(0.35f, 0.85f, 1f, 0.85f);

        private static CopyPasteVisuals _instance;
        private static Sprite _rounded;
        private static Sprite _arrow;
        private static TMP_FontAsset _font;
        private static bool _loggedError;

        /// <summary>
        /// When CopyPasteSystem last reported. If it stops running (scene change, back to the
        /// main menu), nothing would clear its previews, so they're dropped after StaleSeconds.
        /// </summary>
        private static float _lastFeed;
        private const float StaleSeconds = 0.5f;

        private readonly List<PopupObject> _popups = new List<PopupObject>();
        private readonly Dictionary<int, PreviewObject> _previews = new Dictionary<int, PreviewObject>();
        private readonly HashSet<int> _wanted = new HashSet<int>();
        private readonly List<int> _drop = new List<int>();
        private Camera _camera;

        private static CopyPasteVisuals Instance
        {
            get
            {
                if (_instance == null)
                {
                    GameObject go = new GameObject("CopyPaste.Visuals");
                    DontDestroyOnLoad(go);
                    go.hideFlags = HideFlags.HideAndDontSave;
                    _instance = go.AddComponent<CopyPasteVisuals>();
                }
                return _instance;
            }
        }

        public static void Popup(Vector3 position, string text, bool good)
        {
            Guard(() => Instance.AddPopup(position, text, good));
        }

        /// <summary>The previews to show this frame (at most one per player); others are removed.</summary>
        public static void SetPreviews(List<Preview> previews)
        {
            _lastFeed = Time.unscaledTime;
            if (previews.Count == 0 && (_instance == null || _instance._previews.Count == 0))
            {
                return; // the common case: nothing shown, nothing to show
            }
            Guard(() => Instance.UpdatePreviews(previews));
        }

        private static void Guard(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                if (!_loggedError)
                {
                    _loggedError = true;
                    ModMain.LogError($"Feedback failed (logged once): {e}");
                }
            }
        }

        // ---- Popups ----

        private void AddPopup(Vector3 position, string text, bool good)
        {
            PopupObject p = new PopupObject
            {
                Anchor = position + Vector3.up * PopupHeight,
                Born = Time.unscaledTime,
                TextColour = good ? Good : Bad,
                BackColour = Back,
            };
            p.Root = new GameObject("CopyPaste Popup").transform;
            p.Root.SetParent(transform, false);
            p.Root.position = p.Anchor;

            GameObject textGo = new GameObject("Text");
            textGo.transform.SetParent(p.Root, false);
            p.Text = textGo.AddComponent<TextMeshPro>();
            TMP_FontAsset font = Font;
            if (font != null)
            {
                p.Text.font = font;
            }
            p.Text.text = text;
            p.Text.fontSize = 2.2f;
            p.Text.alignment = TextAlignmentOptions.Center;
            p.Text.enableWordWrapping = false;
            p.Text.overflowMode = TextOverflowModes.Overflow;
            p.Text.richText = false;
            p.Text.color = p.TextColour;
            p.Text.rectTransform.sizeDelta = new Vector2(4f, 1f); // generous: see the class comment
            p.Text.sortingOrder = 101;

            GameObject backGo = new GameObject("Backing");
            backGo.transform.SetParent(p.Root, false);
            backGo.transform.localPosition = new Vector3(0f, 0f, 0.01f); // TMP faces -Z: +Z is behind
            p.Backing = backGo.AddComponent<SpriteRenderer>();
            p.Backing.sprite = Rounded;
            p.Backing.drawMode = SpriteDrawMode.Sliced;
            p.Backing.color = p.BackColour;
            p.Backing.sortingOrder = 100;

            // Fit the backing to the text actually drawn.
            p.Text.ForceMeshUpdate();
            Vector2 drawn = p.Text.GetRenderedValues(false);
            if (float.IsNaN(drawn.x) || drawn.x <= 0f)
            {
                drawn = new Vector2(text.Length * 0.12f, PopupTextHeight);
            }
            p.Backing.size = new Vector2(drawn.x + 0.2f, drawn.y + 0.1f);

            // Scale so the text is PopupTextHeight world units tall whatever the font's metrics.
            float scale = drawn.y > 0.01f ? PopupTextHeight / drawn.y : 1f;
            p.Root.localScale = Vector3.one * Mathf.Clamp(scale, 0.05f, 5f);

            // A new popup at the same place replaces the old one instead of stacking on it.
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                if ((_popups[i].Anchor - p.Anchor).sqrMagnitude < 0.01f)
                {
                    DestroyPopup(i);
                }
            }
            _popups.Add(p);
            Face(p.Root);
        }

        private void DestroyPopup(int index)
        {
            if (_popups[index].Root != null)
            {
                Destroy(_popups[index].Root.gameObject);
            }
            _popups.RemoveAt(index);
        }

        // ---- Previews ----

        private void UpdatePreviews(List<Preview> previews)
        {
            _wanted.Clear();
            foreach (Preview preview in previews)
            {
                _wanted.Add(preview.PlayerId);
                if (_previews.TryGetValue(preview.PlayerId, out PreviewObject existing))
                {
                    if (existing.Data.SameLookAs(preview))
                    {
                        continue;
                    }
                    DestroyPreview(existing);
                }
                _previews[preview.PlayerId] = BuildPreview(preview);
            }
            _drop.Clear();
            foreach (KeyValuePair<int, PreviewObject> kv in _previews)
            {
                if (!_wanted.Contains(kv.Key))
                {
                    _drop.Add(kv.Key);
                }
            }
            foreach (int id in _drop)
            {
                DestroyPreview(_previews[id]);
                _previews.Remove(id);
            }
        }

        private PreviewObject BuildPreview(Preview data)
        {
            PreviewObject p = new PreviewObject { Data = data };
            p.Root = new GameObject("CopyPaste Preview");
            p.Root.transform.SetParent(transform, false);
            p.Root.transform.position = data.Position;

            if (data.HasDirection)
            {
                // Direction is relative to the appliance, as in vanilla (ConveyItemsView moves its
                // pusher by PushDirection.ToOffset() in the appliance's local space).
                Vector3 world = data.Rotation * data.Direction.ToOffset();
                world.y = 0f;
                if (world.sqrMagnitude > 0.0001f)
                {
                    GameObject arrow = new GameObject("Arrow");
                    arrow.transform.SetParent(p.Root.transform, false);
                    arrow.transform.localPosition = new Vector3(0f, ArrowHeight, 0f) + world.normalized * 0.15f;
                    // Lie flat: the sprite's up (+Y) along the push direction, its face pointing up.
                    arrow.transform.rotation = Quaternion.LookRotation(Vector3.down, world.normalized);
                    arrow.transform.localScale = Vector3.one * ArrowSize;
                    SpriteRenderer sr = arrow.AddComponent<SpriteRenderer>();
                    sr.sprite = Arrow;
                    sr.color = ArrowColour;
                    sr.sortingOrder = 99;
                }
            }

            if (data.ShowItem)
            {
                GameObject spinner = new GameObject("Spinner");
                spinner.transform.SetParent(p.Root.transform, false);
                spinner.transform.localPosition = new Vector3(0f, ItemHeight, 0f);
                p.Spinner = spinner.transform;
                if (data.Item != 0 && GameData.Main != null && GameData.Main.GetPrefab(data.Item) is GameObject prefab)
                {
                    GameObject item = Instantiate(prefab, spinner.transform, false);
                    item.transform.localPosition = Vector3.zero;
                    item.transform.localRotation = Quaternion.identity;
                    item.transform.localScale = Vector3.one * ItemScale;
                    if (item.TryGetComponent(out ItemGroupView group))
                    {
                        group.PerformUpdate(data.Item, data.Components);
                    }
                    if (item.TryGetComponent(out PassthroughItemView passthrough))
                    {
                        passthrough.PerformUpdate(data.Item, data.Components);
                    }
                    // A display copy only: no physics.
                    foreach (Collider c in item.GetComponentsInChildren<Collider>(true))
                    {
                        c.enabled = false;
                    }
                    item.SetActive(true);
                }
                else
                {
                    // Pasting an empty filter clears it: say so instead of showing nothing.
                    AddPreviewLabel(spinner.transform, "No filter");
                }
            }
            return p;
        }

        private void AddPreviewLabel(Transform parent, string text)
        {
            GameObject go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            TextMeshPro tmp = go.AddComponent<TextMeshPro>();
            TMP_FontAsset font = Font;
            if (font != null)
            {
                tmp.font = font;
            }
            tmp.text = text;
            tmp.fontSize = 2f;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.color = ArrowColour;
            tmp.rectTransform.sizeDelta = new Vector2(4f, 1f);
            tmp.ForceMeshUpdate();
            Vector2 drawn = tmp.GetRenderedValues(false);
            if (!float.IsNaN(drawn.y) && drawn.y > 0.01f)
            {
                go.transform.localScale = Vector3.one * Mathf.Clamp(0.25f / drawn.y, 0.05f, 5f);
            }
        }

        private static void DestroyPreview(PreviewObject p)
        {
            if (p.Root != null)
            {
                Destroy(p.Root);
            }
        }

        // ---- Per frame ----

        private void LateUpdate()
        {
            float now = Time.unscaledTime;
            for (int i = _popups.Count - 1; i >= 0; i--)
            {
                PopupObject p = _popups[i];
                float age = now - p.Born;
                if (age >= PopupSeconds || p.Root == null)
                {
                    DestroyPopup(i);
                    continue;
                }
                p.Root.position = p.Anchor + Vector3.up * (PopupRise * age / PopupSeconds);
                float alpha = Mathf.Clamp01((PopupSeconds - age) / PopupFadeSeconds);
                p.Text.color = new Color(p.TextColour.r, p.TextColour.g, p.TextColour.b, p.TextColour.a * alpha);
                p.Backing.color = new Color(p.BackColour.r, p.BackColour.g, p.BackColour.b, p.BackColour.a * alpha);
                Face(p.Root);
            }

            if (_previews.Count > 0 && now - _lastFeed > StaleSeconds)
            {
                foreach (PreviewObject stale in _previews.Values)
                {
                    DestroyPreview(stale);
                }
                _previews.Clear();
            }

            foreach (PreviewObject p in _previews.Values)
            {
                if (p.Spinner != null)
                {
                    p.Spinner.Rotate(0f, SpinDegreesPerSecond * Time.unscaledDeltaTime, 0f, Space.World);
                    // Text labels face the camera instead of spinning.
                    Transform label = p.Spinner.Find("Label");
                    if (label != null)
                    {
                        Face(label);
                    }
                }
            }
        }

        /// <summary>Billboard: face the same way as the camera so text reads flat on screen.</summary>
        private void Face(Transform t)
        {
            if (_camera == null)
            {
                _camera = Camera.main;
            }
            if (_camera != null)
            {
                t.rotation = _camera.transform.rotation;
            }
        }

        // ---- Assets made at runtime ----

        /// <summary>The game's body text font, or TMP's default if it can't be found.</summary>
        private static TMP_FontAsset Font
        {
            get
            {
                if (_font != null)
                {
                    return _font;
                }
                foreach (TMP_FontAsset f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                {
                    if (f != null && f.name.IndexOf("Primary Text", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _font = f;
                        break;
                    }
                }
                if (_font == null)
                {
                    _font = TMP_Settings.defaultFontAsset;
                }
                return _font;
            }
        }

        /// <summary>A 9-sliced rounded rectangle.</summary>
        private static Sprite Rounded
        {
            get
            {
                if (_rounded != null)
                {
                    return _rounded;
                }
                const int size = 32;
                const int radius = 10;
                Texture2D tex = NewTexture(size, "CopyPaste Rounded");
                Color32[] pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = Mathf.Max(0f, Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius)));
                        float dy = Mathf.Max(0f, Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius)));
                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(radius - dist + 0.5f) * 255f));
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                _rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                    SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
                _rounded.name = "CopyPaste Rounded";
                return _rounded;
            }
        }

        /// <summary>A white arrow pointing up (+Y), one world unit across.</summary>
        private static Sprite Arrow
        {
            get
            {
                if (_arrow != null)
                {
                    return _arrow;
                }
                const int size = 64;
                Texture2D tex = NewTexture(size, "CopyPaste Arrow");
                Color32[] pixels = new Color32[size * size];
                const float shaftHalf = 7f, shaftBottom = 6f, headBottom = 30f, headTop = 60f, headHalf = 26f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float px = x + 0.5f - size / 2f, py = y + 0.5f;
                        // Signed distance-ish coverage, 1 px soft edge.
                        float shaft = Mathf.Min(shaftHalf - Mathf.Abs(px), Mathf.Min(py - shaftBottom, headBottom + 1f - py));
                        float halfAt = headHalf * (headTop - py) / (headTop - headBottom);
                        float head = Mathf.Min(halfAt - Mathf.Abs(px), Mathf.Min(py - headBottom, headTop - py));
                        float a = Mathf.Clamp01(Mathf.Max(shaft, head) + 0.5f);
                        pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                _arrow = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
                _arrow.name = "CopyPaste Arrow";
                return _arrow;
            }
        }

        private static Texture2D NewTexture(int size, string name)
        {
            return new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = name,
            };
        }
    }
}
