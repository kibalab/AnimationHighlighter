#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace K13A.AnimationEditor.PropertyHighlighter
{
    static class GeometryUtil
    {
        static Func<EditorWindow, Rect> _getHierarchyTotalRect;
        static float _bottomScrollbarHeight = 13f;

        public static void Configure(Func<EditorWindow, Rect> getHierarchyTotalRect, float bottomScrollbarHeight)
        {
            _getHierarchyTotalRect = getHierarchyTotalRect;
            _bottomScrollbarHeight = Mathf.Max(1f, bottomScrollbarHeight);
        }

        public static Rect ClipRect(Rect src, Rect clip)
        {
            float xMin = Mathf.Max(src.xMin, clip.xMin);
            float yMin = Mathf.Max(src.yMin, clip.yMin);
            float xMax = Mathf.Min(src.xMax, clip.xMax);
            float yMax = Mathf.Min(src.yMax, clip.yMax);

            if (xMax <= xMin || yMax <= yMin)
                return new Rect();

            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        public static Rect NormalizeTimelineRect(EditorWindow window, Rect rect)
        {
            if (window == null)
                return rect;

            Rect windowRect = new Rect(0f, 0f, window.position.width, window.position.height);
            if (IsUsableTimelineRect(rect, windowRect))
                return rect;

            Rect hierarchyRect = _getHierarchyTotalRect != null ? _getHierarchyTotalRect(window) : new Rect();
            if (hierarchyRect.width > 1f && hierarchyRect.height > 1f)
            {
                float x = hierarchyRect.xMax + 1f;
                float y = hierarchyRect.yMin;
                float w = Mathf.Max(1f, windowRect.width - x - _bottomScrollbarHeight);
                float h = Mathf.Max(1f, hierarchyRect.height);
                Rect fallback = new Rect(x, y, w, h);
                if (IsUsableTimelineRect(fallback, windowRect))
                    return fallback;
            }

            float defaultX = windowRect.width * 0.30f;
            float defaultW = Mathf.Max(1f, windowRect.width - defaultX - _bottomScrollbarHeight);
            float defaultH = Mathf.Max(1f, windowRect.height - _bottomScrollbarHeight);
            return new Rect(defaultX, 0f, defaultW, defaultH);
        }

        public static Rect GetOverlayBounds(EditorWindow window, VisualElement overlay)
        {
            if (overlay != null)
            {
                float w = overlay.resolvedStyle.width;
                float h = overlay.resolvedStyle.height;
                if (w > 1f && h > 1f)
                    return new Rect(0f, 0f, w, h);

                Rect layout = overlay.layout;
                if (layout.width > 1f && layout.height > 1f)
                    return new Rect(0f, 0f, layout.width, layout.height);
            }

            if (window != null && window.rootVisualElement != null)
            {
                float rw = window.rootVisualElement.resolvedStyle.width;
                float rh = window.rootVisualElement.resolvedStyle.height;
                if (rw > 1f && rh > 1f)
                    return new Rect(0f, 0f, rw, rh);

                Rect layout = window.rootVisualElement.layout;
                if (layout.width > 1f && layout.height > 1f)
                    return new Rect(0f, 0f, layout.width, layout.height);
            }

            if (window != null)
                return new Rect(0f, 0f, Mathf.Max(1f, window.position.width), Mathf.Max(1f, window.position.height));

            return new Rect();
        }

        public static Rect ConstrainToKeyframeSide(EditorWindow window, VisualElement overlay, Rect timelineRect)
        {
            if (window == null)
                return timelineRect;

            Rect hierarchyRect = _getHierarchyTotalRect != null ? _getHierarchyTotalRect(window) : new Rect();
            if (hierarchyRect.width <= 1f || hierarchyRect.height <= 1f)
                return timelineRect;

            Rect h = hierarchyRect;
            if (overlay != null)
            {
                Rect overlayBounds = GetOverlayBounds(window, overlay);
                if (overlayBounds.width > 1f && overlayBounds.height > 1f)
                    h = ResolveRectInOverlaySpace(overlay, hierarchyRect, overlayBounds);
            }

            float keyX = h.xMax + 1f;
            float right = timelineRect.xMax;
            if (right <= keyX + 1f)
            {
                Rect overlayBounds = GetOverlayBounds(window, overlay);
                float fallbackRight = overlayBounds.width > 1f ? overlayBounds.width : window.position.width;
                right = Mathf.Max(right, fallbackRight - _bottomScrollbarHeight);
            }

            Rect result = timelineRect;
            result.xMin = Mathf.Max(result.xMin, keyX);
            result.xMax = Mathf.Max(result.xMin + 1f, right);
            return result;
        }

        public static Rect BuildKeyframeRectFromHierarchy(EditorWindow window, VisualElement overlay)
        {
            if (window == null)
                return new Rect();

            Rect hierarchyRect = _getHierarchyTotalRect != null ? _getHierarchyTotalRect(window) : new Rect();
            if (hierarchyRect.width <= 1f || hierarchyRect.height <= 1f)
            {
                Rect overlayBounds = GetOverlayBounds(window, overlay);
                float width = overlayBounds.width > 1f ? overlayBounds.width : window.position.width;
                float height = overlayBounds.height > 1f ? overlayBounds.height : window.position.height;
                float x = width * 0.30f;
                return new Rect(
                    x,
                    0f,
                    Mathf.Max(1f, width - x - _bottomScrollbarHeight),
                    Mathf.Max(1f, height - _bottomScrollbarHeight));
            }

            if (overlay != null)
            {
                Rect overlayBounds = GetOverlayBounds(window, overlay);
                Rect h = ResolveRectInOverlaySpace(overlay, hierarchyRect, overlayBounds);
                float x = h.xMax + 1f;
                float y = Mathf.Clamp(h.yMin, 0f, overlayBounds.height - 1f);
                float w = Mathf.Max(1f, overlayBounds.width - x - _bottomScrollbarHeight);
                float hgt = Mathf.Max(1f, Mathf.Min(h.height, overlayBounds.height - y));
                return new Rect(x, y, w, hgt);
            }

            Rect windowRect = new Rect(0f, 0f, window.position.width, window.position.height);
            float wx = hierarchyRect.xMax + 1f;
            float wy = Mathf.Clamp(hierarchyRect.yMin, 0f, windowRect.height - 1f);
            float ww = Mathf.Max(1f, windowRect.width - wx - _bottomScrollbarHeight);
            float wh = Mathf.Max(1f, Mathf.Min(hierarchyRect.height, windowRect.height - wy));
            return new Rect(wx, wy, ww, wh);
        }

        public static bool IsUsableTimelineRect(Rect rect, Rect windowRect)
        {
            if (rect.width <= 1f || rect.height <= 1f)
                return false;

            Rect overlap = ClipRect(rect, windowRect);
            return overlap.width * overlap.height > 50f;
        }

        public static Rect ResolveRectInOverlaySpace(VisualElement overlay, Rect rawRect, Rect overlayBounds)
        {
            if (overlay == null)
                return rawRect;

            Rect localAssumed = rawRect;
            Rect worldToLocal = ConvertWorldRectToLocal(overlay, rawRect);

            float areaLocal = OverlapArea(localAssumed, overlayBounds);
            float areaWorld = OverlapArea(worldToLocal, overlayBounds);

            return areaWorld > areaLocal ? worldToLocal : localAssumed;
        }

        public static Rect ConvertWorldRectToLocal(VisualElement overlay, Rect worldRect)
        {
            Vector2 p0 = overlay.WorldToLocal(new Vector2(worldRect.xMin, worldRect.yMin));
            Vector2 p1 = overlay.WorldToLocal(new Vector2(worldRect.xMax, worldRect.yMax));
            return Rect.MinMaxRect(p0.x, p0.y, p1.x, p1.y);
        }

        public static float OverlapArea(Rect a, Rect b)
        {
            Rect c = ClipRect(a, b);
            if (c.width <= 0f || c.height <= 0f)
                return 0f;
            return c.width * c.height;
        }

        public static Rect ConvertRootLocalRectToContainerLocal(EditorWindow window, IMGUIContainer container, Rect rootLocalRect)
        {
            if (window == null || window.rootVisualElement == null || container == null)
                return rootLocalRect;

            Vector2 w0 = window.rootVisualElement.LocalToWorld(new Vector2(rootLocalRect.xMin, rootLocalRect.yMin));
            Vector2 w1 = window.rootVisualElement.LocalToWorld(new Vector2(rootLocalRect.xMax, rootLocalRect.yMax));
            Vector2 l0 = container.WorldToLocal(w0);
            Vector2 l1 = container.WorldToLocal(w1);
            return Rect.MinMaxRect(l0.x, l0.y, l1.x, l1.y);
        }
    }
}
#endif