using System;
using System.Collections.Generic;
using Kitchen;
using KitchenData;
using TMPro;
using UnityEngine;

namespace CopyPaste
{
    /// <summary>
    /// Draws the feedback on this machine (host or guest) for this machine's own players: plain
    /// Unity objects, never sent anywhere. Fed by FeedbackChannel with the host's snapshots.
    ///
    ///   Popups   a short label ("Copied", "Pasted", ...) that rises over the appliance and fades.
    ///   Preview  while one of this machine's players has the crane over an appliance of a type
    ///            they copied, and a paste would change it: a flat arrow for the direction,
    ///            and/or a small spinning copy of the filter / provided item, the same way
    ///            vanilla's ConveyItemsView shows a smart grabber's filter.
    ///
    /// Which players are this machine's: CPlayer.InputSource equals this machine's
    /// InputSourceIdentifier.Identifier (vanilla's PlayerView.IsMyPlayer test).
    ///
    /// Sprites are generated at runtime and drawn with SpriteRenderer, and text uses the game's
    /// own TMP font with Overflow (the body font draws nothing when it doesn't fit), the same
    /// approach Blueprint Shop's sale labels use, which renders correctly in this game.
    /// </summary>
    internal class CopyPasteVisuals : MonoBehaviour
    {
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
            public PreviewState Data;
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
        /// When the last snapshot arrived. The host resends every 0.5 s while a preview is up;
        /// if they stop (disconnect, scene change, main menu), previews are dropped after this.
        /// </summary>
        private static float _lastFeed;
        private const float StaleSeconds = 1.5f;

        // Popups already shown, by sequence number, for the host session that numbered them.
        private static int _nonce;
        private static int _lastSeq;

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

        /// <summary>A snapshot from the host: every player's previews, and recent popups.</summary>
        public static void Apply(int nonce, List<PopupEvent> popups, List<PreviewState> previews)
        {
            _lastFeed = Time.unscaledTime;
            int local = Controllers.InputSourceIdentifier.Identifier.Value;
            if (nonce != _nonce)
            {
                _nonce = nonce; // a different host (or a restarted one) numbers popups afresh
                _lastSeq = 0;
            }
            foreach (PopupEvent p in popups)
            {
                if (p.Seq <= _lastSeq)
                {
                    continue; // shown already (snapshots repeat recent popups)
                }
                _lastSeq = p.Seq;
                // The host only includes popups from the last second, so a guest joining mid-way
                // sees nothing older than that.
                if (p.Source == local && CopyPasteSettings.ShowPopups)
                {
                    PopupEvent popup = p;
                    Guard(() => Instance.AddPopup(popup.Position, popup.Text, popup.Good, popup.AgeMs / 1000f));
                }
            }

            List<PreviewState> mine = new List<PreviewState>();
            if (CopyPasteSettings.ShowPreview)
            {
                foreach (PreviewState v in previews)
                {
                    if (v.Source == local)
                    {
                        mine.Add(v);
                    }
                }
            }
            if (mine.Count == 0 && (_instance == null || _instance._previews.Count == 0))
            {
                return; // the common case: nothing shown, nothing to show
            }
            Guard(() => Instance.UpdatePreviews(mine));
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

        private void AddPopup(Vector3 position, string text, bool good, float ageSeconds)
        {
            PopupObject p = new PopupObject
            {
                Anchor = position + Vector3.up * PopupHeight,
                // Started on the host ageSeconds ago (network and batching delay).
                Born = Time.unscaledTime - Mathf.Clamp(ageSeconds, 0f, PopupSeconds),
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

        private void UpdatePreviews(List<PreviewState> previews)
        {
            _wanted.Clear();
            foreach (PreviewState preview in previews)
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

        private PreviewObject BuildPreview(PreviewState data)
        {
            PreviewObject p = new PreviewObject { Data = data };
            p.Root = new GameObject("CopyPaste Preview");
            p.Root.transform.SetParent(transform, false);
            p.Root.transform.position = data.Position;

            // The host already turned the grabber's relative direction into a world one.
            Vector3 world = new Vector3(data.Arrow.x, 0f, data.Arrow.z);
            if (world.sqrMagnitude > 0.0001f)
            {
                world.Normalize();
                GameObject arrow = new GameObject("Arrow");
                arrow.transform.SetParent(p.Root.transform, false);
                arrow.transform.localPosition = new Vector3(0f, ArrowHeight, 0f) + world * 0.15f;
                // Lie flat: the sprite's up (+Y) along the push direction, its face pointing up.
                arrow.transform.rotation = Quaternion.LookRotation(Vector3.down, world);
                arrow.transform.localScale = Vector3.one * ArrowSize;
                SpriteRenderer sr = arrow.AddComponent<SpriteRenderer>();
                sr.sprite = Arrow;
                sr.color = ArrowColour;
                sr.sortingOrder = 99;
            }

            if (data.ShowItem)
            {
                GameObject spinner = new GameObject("Spinner");
                spinner.transform.SetParent(p.Root.transform, false);
                spinner.transform.localPosition = new Vector3(0f, ItemHeight, 0f);
                p.Spinner = spinner.transform;
                if (data.Item == 0)
                {
                    // Pasting an empty filter clears it: say so instead of showing nothing.
                    AddPreviewLabel(spinner.transform, "No filter");
                }
                // Not GetPrefab, which throws for an item this machine doesn't have (from a mod
                // only the host runs, say); then only the arrow, if any, is shown.
                else if (GameData.Main != null && GameData.Main.Prefabs.TryGetValue(data.Item, out GameObject prefab) && prefab != null)
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
