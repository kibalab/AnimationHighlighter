#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.UIElements;
using static K13A.AnimationEditor.PropertyHighlighter.GeometryUtil;
using static K13A.AnimationEditor.PropertyHighlighter.SelectionResolver;

namespace K13A.AnimationEditor.PropertyHighlighter
{
    [InitializeOnLoad]
    static class Controller
    {
        internal sealed class ImguiHook
        {
            public IMGUIContainer Container;
            public Action Handler;
            public object TreeView;
            public Delegate RowCallbackAssigned;
            public Delegate OriginalRowCallback;
            public readonly Dictionary<int, Rect> RowRectsById = new();
            public int RowRectsFrame = -1;
        }

        public sealed class RuntimeState
        {
            public readonly Dictionary<int, VisualElement> OverlayByWindowId = new();
            public readonly Dictionary<int, ImguiHook> ImguiHookByWindowId = new();
            public readonly HashSet<int> NoImguiWarnedWindowIds = new();
            public bool IsInitialized;
        }

        private const string OverlayName = "K13A.AnimationTimelinePropertyHighlighter";
        private const float BottomScrollbarHeight = 13f;
        private const bool UseTreeViewRowCallbackFallback = false;
        private const bool UseTreeViewDirectRowProjection = false;

        private static readonly BindingFlags InstFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly RuntimeState Runtime = new();
        private static readonly LifecycleHooks LifecycleHooks = new();
        private static readonly InitializationGate ReflectionGate = new(0.35d);

        private static Dictionary<int, VisualElement> OverlayByWindowId => Runtime.OverlayByWindowId;
        private static Dictionary<int, ImguiHook> IMGUIHookByWindowId => Runtime.ImguiHookByWindowId;
        private static HashSet<int> NoImguiWarnedWindowIds => Runtime.NoImguiWarnedWindowIds;

        private static bool IsInitialized
        {
            get => Runtime.IsInitialized;
            set => Runtime.IsInitialized = value;
        }

        private static bool EnableDebugLogs => Settings.DebugLogsEnabled;
        private static Color HighlightFillColor => Settings.HighlightFill;
        private static Color HighlightBorderColor => Settings.HighlightBorder;

        private static Type _animationWindowType;
        private static MethodInfo _getAllAnimationWindowsMethod;
        private static PropertyInfo _animationWindowStateProp;
        private static PropertyInfo _animationWindowAnimEditorProp;
        private static FieldInfo _animationWindowAnimEditorField;

        private static PropertyInfo _stateShowCurveEditorProp;
        private static FieldInfo _stateShowCurveEditorField;
        private static PropertyInfo _stateDopelinesProp;
        private static FieldInfo _stateDopelinesField;
        private static PropertyInfo _stateActiveCurvesProp;
        private static PropertyInfo _stateHierarchyStateProp;
        private static FieldInfo _stateHierarchyStateField;
        private static PropertyInfo _stateSelectedHierarchyNodesProp;

        private static PropertyInfo _hierarchySelectedIdsProp;
        private static FieldInfo _hierarchySelectedIdsField;
        private static FieldInfo _hierarchySelectedIdsBackingField;
        private static PropertyInfo _hierarchyLastClickedIdProp;
        private static FieldInfo _hierarchyLastClickedIdField;
        private static FieldInfo _hierarchyLastClickedIdBackingField;
        private static PropertyInfo _hierarchyScrollPosProp;
        private static FieldInfo _hierarchyScrollPosField;

        private static FieldInfo _animEditorDopeSheetField;
        private static FieldInfo _animEditorHierarchyField;
        private static PropertyInfo _dopeSheetRectProp;
        private static FieldInfo _dopeSheetRectField;

        private static FieldInfo _hierarchyTreeViewField;
        private static MethodInfo _hierarchyGetTotalRectMethod;
        private static PropertyInfo _treeViewStateProp;
        private static FieldInfo _treeViewStateField;
        private static PropertyInfo _treeViewOnGuiRowCallbackProp;
        private static MethodInfo _treeViewGetSelectionMethod;
        private static PropertyInfo _treeViewDataProp;
        private static PropertyInfo _treeViewGuiProp;
        private static MethodInfo _treeViewGetTotalRectMethod;
        private static MethodInfo _treeDataGetRowsMethod;
        private static MethodInfo _treeGuiGetRowRectMethod;

        static Controller()
        {
            Configure(GetHierarchyTotalRect, BottomScrollbarHeight);
            EnsureInitialized("StaticCtor");
        }

        [InitializeOnLoadMethod]
        private static void BootstrapOnLoadMethod()
        {
            EnsureInitialized("InitializeOnLoadMethod");
        }

        [DidReloadScripts]
        private static void BootstrapOnReloadedScripts()
        {
            EnsureInitialized("DidReloadScripts");
        }

        [MenuItem("Tools/K13A/Animation Highlighter/Force Diagnostic Log")]
        private static void ForceDiagnosticMenu()
        {
            EnsureInitialized("Menu");
            ForceDiagnostic("Menu");
        }

        private static void EnsureInitialized(string source)
        {
            if (IsInitialized)
                return;

            IsInitialized = true;
            var reflectionOk = ReflectionGate.ForceRefresh(InitReflection);
            if (EnableDebugLogs)
                Debug.LogWarning($"[AnimationTimelineHighlighter] Initialized via {source}. reflectionOk={reflectionOk}");

            LifecycleHooks.Register(Update, Cleanup);

            EditorApplication.delayCall += () => ForceDiagnostic($"Delay:{source}");
        }

        private static void ForceDiagnostic(string source)
        {
            if (!EnableDebugLogs)
                return;

            var reflectionOk = ReflectionGate.ForceRefresh(InitReflection);
            var windows = GetAnimationWindows();
            var windowCount = windows?.Count ?? 0;
            var asmName = typeof(Controller).Assembly.GetName().Name;

            Debug.LogWarning($"[AnimationTimelineHighlighter] Diagnostic source={source}, asm={asmName}, reflectionOk={reflectionOk}, windows={windowCount}, overlays={OverlayByWindowId.Count}, hooks={IMGUIHookByWindowId.Count}");

            if (!reflectionOk)
            {
                Debug.LogError("[AnimationTimelineHighlighter] Reflection bootstrap failed. AnimationWindow type/state could not be resolved.");
            }
        }

        static void Update()
        {
            if (!ReflectionGate.EnsureReady(InitReflection))
            {
                LogGlobal("Update skipped: reflection not ready.");
                return;
            }

            var aliveWindowIds = new HashSet<int>();
            var windows = GetAnimationWindows();
            var windowCount = windows?.Count ?? 0;
            if (windowCount == 0)
            {
                LogGlobal("Update: no AnimationWindow instances found.");
            }

            if (windows != null)
            {
                foreach (var window in windows.OfType<EditorWindow>())
                {
                    try
                    {
                        var id = window.GetInstanceID();
                        aliveWindowIds.Add(id);
                        EnsureOverlay(window, id);
                        EnsureImGuiHook(window, id);
                        var hasActiveImGuiPath = HasActiveImGuiHook(id);
                        if (UseTreeViewRowCallbackFallback)
                        {
                            EnsureTreeViewRowCallback(window, id);
                        }
                        else
                        {
                            EnsureTreeViewRowCallbackDisabled(window, id);
                        }

                        if (!OverlayByWindowId.TryGetValue(id, out var overlay) || overlay == null) continue;

                        overlay.BringToFront();
                        if (hasActiveImGuiPath)
                        {
                            // Avoid double-drawing when IMGUI hook is active.
                            overlay.style.display = DisplayStyle.None;
                            overlay.Clear();
                        }
                        else
                        {
                            UpdateOverlayVisual(window, overlay);
                        }

                        window.Repaint();
                    }
                    catch (Exception ex)
                    {
                        LogWindow(window, $"Update exception: {ex.GetType().Name}: {ex.Message}", true);
                    }
                }
            }

            RemoveOrphanedOverlays(aliveWindowIds);
            RemoveOrphanedImguiHooks(aliveWindowIds);
        }

        private static bool InitReflection()
        {
            if (_animationWindowType == null)
            {
                var editorAsm = typeof(EditorWindow).Assembly;
                _animationWindowType = editorAsm.GetType("UnityEditor.AnimationWindow");
                if (_animationWindowType == null)
                    return false;

                _getAllAnimationWindowsMethod = _animationWindowType.GetMethod("GetAllAnimationWindows", StaticFlags);
                _animationWindowStateProp = _animationWindowType.GetProperty("state", InstFlags);
                _animationWindowAnimEditorProp = _animationWindowType.GetProperty("animEditor", InstFlags);
                _animationWindowAnimEditorField = _animationWindowType.GetField("m_AnimEditor", InstFlags);
            }

            if (
                (_stateShowCurveEditorProp == null && _stateShowCurveEditorField == null)
                || (_stateDopelinesProp == null && _stateDopelinesField == null)
            )
            {
                var editorAsm = typeof(EditorWindow).Assembly;
                var stateType = editorAsm.GetType("UnityEditorInternal.AnimationWindowState");
                if (stateType == null)
                    return false;

                _stateShowCurveEditorProp = stateType.GetProperty("showCurveEditor", InstFlags);
                _stateShowCurveEditorField = stateType.GetField("showCurveEditor", InstFlags)
                                             ?? stateType.GetField("m_ShowCurveEditor", InstFlags);
                _stateDopelinesProp = stateType.GetProperty("dopelines", InstFlags);
                _stateDopelinesField = stateType.GetField("dopelines", InstFlags)
                                       ?? stateType.GetField("m_Dopelines", InstFlags);
                _stateActiveCurvesProp = stateType.GetProperty("activeCurves", InstFlags);
                _stateHierarchyStateProp = stateType.GetProperty("hierarchyState", InstFlags);
                _stateHierarchyStateField = stateType.GetField("hierarchyState", InstFlags);
                _stateSelectedHierarchyNodesProp = stateType.GetProperty("selectedHierarchyNodes", InstFlags);
            }

            var hasShowCurve = _stateShowCurveEditorProp != null || _stateShowCurveEditorField != null;
            var hasDopelines = _stateDopelinesProp != null || _stateDopelinesField != null;
            return hasShowCurve && hasDopelines;
        }

        private static IList GetAnimationWindows()
        {
            if (_getAllAnimationWindowsMethod != null)
            {
                return _getAllAnimationWindowsMethod.Invoke(null, null) as IList;
            }

            if (_animationWindowType == null)
            {
                return null;
            }

            var objs = Resources.FindObjectsOfTypeAll(_animationWindowType);
            return objs;
        }

        private static void EnsureOverlay(EditorWindow window, int id)
        {
            if (window == null || window.rootVisualElement == null)
            {
                return;
            }

            if (OverlayByWindowId.TryGetValue(id, out var existing))
            {
                if (existing is { panel: not null })
                {
                    return;
                }

                existing?.RemoveFromHierarchy();
                OverlayByWindowId.Remove(id);
            }

            var stale = window.rootVisualElement.Q<VisualElement>(OverlayName);
            stale?.RemoveFromHierarchy();

            var overlay = new VisualElement
            {
                name = OverlayName,
                pickingMode = PickingMode.Ignore,
                style =
                {
                    position = Position.Absolute,
                    left = 0,
                    top = 0,
                    right = 0,
                    bottom = 0,
                    flexGrow = 1f
                }
            };
            overlay.StretchToParentSize();

            window.rootVisualElement.Add(overlay);
            OverlayByWindowId[id] = overlay;
            LogWindow(window, "Overlay created.", true);
        }

        static void RemoveOrphanedOverlays(HashSet<int> aliveWindowIds)
        {
            if (OverlayByWindowId.Count == 0)
                return;

            var staleIds = new List<int>();
            foreach (var kv in OverlayByWindowId)
            {
                var alive = aliveWindowIds.Contains(kv.Key);
                var attached = kv.Value is { panel: not null };
                if (alive && attached) continue;

                kv.Value?.RemoveFromHierarchy();

                staleIds.Add(kv.Key);
            }

            foreach (var id in staleIds)
            {
                OverlayByWindowId.Remove(id);
                HighlighterLog.ForgetWindow(id);
            }
        }

        private static void EnsureImGuiHook(EditorWindow window, int id)
        {
            if (window == null || window.rootVisualElement == null)
                return;

            var target = FindPrimaryImGuiContainer(window);
            if (target == null)
            {
                GetOrCreateHook(id);
                if (NoImguiWarnedWindowIds.Add(id))
                {
                    LogWindow(window, "No IMGUIContainer found. Using UI Toolkit overlay + TreeView row callback path.", true);
                }

                return;
            }

            if (NoImguiWarnedWindowIds.Remove(id))
            {
                LogWindow(window, "IMGUIContainer discovered. Switching to mixed IMGUI/UI Toolkit path.", true);
            }

            var existing = GetOrCreateHook(id);
            if (existing != null)
            {
                var sameContainer = existing.Container == target;
                var alive = existing.Container is { panel: not null };
                if (sameContainer && alive) return;

                if (existing.Container != null && existing.Handler != null)
                {
                    UnhookImgui(id, existing);
                }
            }

            Action handler = () => DrawOverlayImGui(window, target);
            target.onGUIHandler += handler;
            existing.Container = target;
            existing.Handler = handler;

            LogWindow(window, $"Hooked IMGUI container: name='{target.name}', size={target.resolvedStyle.width:0.##}x{target.resolvedStyle.height:0.##}", true);
        }

        private static ImguiHook GetOrCreateHook(int id)
        {
            if (IMGUIHookByWindowId.TryGetValue(id, out var hook) && hook != null) return hook;

            hook = new ImguiHook();
            IMGUIHookByWindowId[id] = hook;
            return hook;
        }

        private static bool HasActiveImGuiHook(int id)
        {
            if (!IMGUIHookByWindowId.TryGetValue(id, out var hook) || hook == null) return false;

            return hook.Container is { panel: not null } && hook.Handler != null;
        }

        private static IMGUIContainer FindPrimaryImGuiContainer(EditorWindow window)
        {
            if (window == null || window.rootVisualElement == null)
                return null;

            var root = window.rootVisualElement;
            if (root == null)
                return null;

            var list = new List<IMGUIContainer>();
            root.Query<IMGUIContainer>().ToList(list);
            if (list.Count == 0)
                return null;

            var timelineRect = GetTimelineRect(window);
            var bestOverlap = -1f;
            var bestArea = -1f;
            IMGUIContainer byOverlap = null;
            IMGUIContainer byArea = null;

            foreach (var c in list)
            {
                if (c == null) continue;

                var w = c.resolvedStyle.width;
                var h = c.resolvedStyle.height;
                var area = Mathf.Max(0f, w) * Mathf.Max(0f, h);
                var cRootRect = ConvertWorldRectToLocal(root, c.worldBound);
                var overlap = OverlapArea(cRootRect, timelineRect);

                if (overlap > bestOverlap)
                {
                    bestOverlap = overlap;
                    byOverlap = c;
                }

                if (area > bestArea)
                {
                    bestArea = area;
                    byArea = c;
                }
            }

            var chosen = byArea ?? byOverlap ?? list[0];
            LogWindow(window, $"FindPrimaryIMGUI: count={list.Count}, bestArea={bestArea:0.#}, bestOverlap={bestOverlap:0.#}, chosen='{chosen?.name}'", true);
            return chosen;
        }

        private static void RemoveOrphanedImguiHooks(HashSet<int> aliveWindowIds)
        {
            if (IMGUIHookByWindowId.Count == 0)
                return;

            var staleIds = new List<int>();
            foreach (var kv in IMGUIHookByWindowId)
            {
                var alive = aliveWindowIds.Contains(kv.Key);
                var attached = kv.Value != null && (kv.Value.Container == null || kv.Value.Container.panel != null);
                if (alive && attached) continue;

                UnhookImgui(kv.Key, kv.Value);
                staleIds.Add(kv.Key);
            }

            foreach (var id in staleIds)
            {
                IMGUIHookByWindowId.Remove(id);
                NoImguiWarnedWindowIds.Remove(id);
                HighlighterLog.ForgetWindow(id);
            }
        }

        private static void UnhookImgui(int id, ImguiHook hook)
        {
            if (hook?.Container == null || hook.Handler == null)
            {
                UnhookTreeViewRowCallback(hook);
                return;
            }

            try
            {
                hook.Container.onGUIHandler -= hook.Handler;
            }
            catch
            {
                // ignored
            }

            hook.Container = null;
            hook.Handler = null;
            UnhookTreeViewRowCallback(hook);
        }

        private static void Cleanup()
        {
            LifecycleHooks.Unregister(Update, Cleanup);
            ReflectionGate.Reset();
            IsInitialized = false;
            HighlighterLog.Reset();

            foreach (var kv in OverlayByWindowId.Where(static kv => kv.Value != null))
            {
                kv.Value.RemoveFromHierarchy();
            }

            OverlayByWindowId.Clear();

            foreach (var kv in IMGUIHookByWindowId)
            {
                UnhookImgui(kv.Key, kv.Value);
            }

            IMGUIHookByWindowId.Clear();
            NoImguiWarnedWindowIds.Clear();
        }

        private static void UpdateOverlayVisual(EditorWindow window, VisualElement overlay)
        {
            if (window == null || overlay == null) return;

            var overlayBounds = GetOverlayBounds(window, overlay);
            if (overlayBounds is { width: > 1f, height: > 1f } &&
                (overlay.resolvedStyle.width <= 1f || overlay.resolvedStyle.height <= 1f))
            {
                overlay.style.left = 0f;
                overlay.style.top = 0f;
                overlay.style.width = overlayBounds.width;
                overlay.style.height = overlayBounds.height;
            }

            var rootW = window.rootVisualElement != null ? window.rootVisualElement.resolvedStyle.width : 0f;
            var rootH = window.rootVisualElement != null ? window.rootVisualElement.resolvedStyle.height : 0f;
            LogWindow(window, $"UpdateOverlayVisual: overlay={overlay.resolvedStyle.width:0.#}x{overlay.resolvedStyle.height:0.#}, root={rootW:0.#}x{rootH:0.#}, fallback={overlayBounds.width:0.#}x{overlayBounds.height:0.#}");

            if (!TryBuildHighlightRects(window, overlay, out var lineRects))
            {
                overlay.style.display = DisplayStyle.None;
                overlay.Clear();
                LogWindow(window, "UpdateOverlayVisual: no highlight rects.");
                return;
            }

            overlay.style.display = DisplayStyle.Flex;
            overlay.Clear();

            foreach (var r in lineRects)
            {
                var line = new VisualElement
                {
                    pickingMode = PickingMode.Ignore,
                    style =
                    {
                        position = Position.Absolute,
                        left = r.xMin,
                        top = r.yMin,
                        width = r.width,
                        height = r.height,
                        backgroundColor = HighlightFillColor,
                        borderTopWidth = 1f,
                        borderBottomWidth = 1f,
                        borderLeftWidth = 1f,
                        borderRightWidth = 1f,
                        borderTopColor = HighlightBorderColor,
                        borderBottomColor = HighlightBorderColor,
                        borderLeftColor = HighlightBorderColor,
                        borderRightColor = HighlightBorderColor
                    }
                };
                overlay.Add(line);
            }
        }

        private static void DrawOverlayImGui(EditorWindow window, IMGUIContainer container)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (!TryBuildHighlightRects(window, null, out var lineRects)) return;
            if (container == null || window == null || window.rootVisualElement == null) return;

            var containerLocalBounds = new Rect(0f, 0f, container.resolvedStyle.width, container.resolvedStyle.height);
            if (containerLocalBounds.width <= 1f || containerLocalBounds.height <= 1f) return;

            var keyframeRootRect = BuildKeyframeRectFromHierarchy(window, null);
            var keyframeContainerRect = ConvertRootLocalRectToContainerLocal(window, container, keyframeRootRect);
            keyframeContainerRect = ClipRect(keyframeContainerRect, containerLocalBounds);
            if (keyframeContainerRect.width <= 1f || keyframeContainerRect.height <= 1f)
            {
                var x = containerLocalBounds.width * 0.30f;
                keyframeContainerRect = new Rect(
                    x,
                    0f,
                    Mathf.Max(1f, containerLocalBounds.width - x),
                    containerLocalBounds.height);
            }

            LogWindow(window, $"IMGUI draw: lines={lineRects.Count}, container={containerLocalBounds.width:0.#}x{containerLocalBounds.height:0.#}");

            foreach (var rRoot in lineRects)
            {
                var converted = ConvertRootLocalRectToContainerLocal(window, container, rRoot);
                var r = ClipRect(converted, keyframeContainerRect);

                if (r.width <= 0f && converted.height > 0f)
                {
                    // Fallback when timeline X was resolved incorrectly: keep row Y/height and fill keyframe track width only.
                    var yOnly = new Rect(keyframeContainerRect.xMin, converted.yMin, keyframeContainerRect.width, converted.height);
                    r = ClipRect(yOnly, keyframeContainerRect);
                }

                if (r.width <= 0f || r.height <= 0f)
                {
                    continue;
                }

                EditorGUI.DrawRect(r, HighlightFillColor);
                EditorGUI.DrawRect(new Rect(r.xMin, r.yMin, r.width, 1f), HighlightBorderColor);
                EditorGUI.DrawRect(new Rect(r.xMin, r.yMax - 1f, r.width, 1f), HighlightBorderColor);
                EditorGUI.DrawRect(new Rect(r.xMin, r.yMin, 1f, r.height), HighlightBorderColor);
                EditorGUI.DrawRect(new Rect(r.xMax - 1f, r.yMin, 1f, r.height), HighlightBorderColor);
            }
        }

        private static bool TryBuildHighlightRects(EditorWindow window, VisualElement overlay, out List<Rect> lineRects)
        {
            lineRects = new List<Rect>();
            if (window == null) return false;

            var state = GetAnimationWindowState(window);
            if (state == null)
            {
                LogWindow(window, "BuildRects: state is null.");
                return false;
            }

            if (IsCurveEditorVisible(state))
            {
                LogWindow(window, "BuildRects: curve mode active.");
                return false;
            }

            var hierarchyState = GetHierarchyState(state);
            if (hierarchyState == null)
            {
                LogWindow(window, "BuildRects: hierarchyState is null.");
                return false;
            }

            var dopelines = GetDopelines(state);
            if (dopelines == null || dopelines.Count == 0)
            {
                LogWindow(window, "BuildRects: dopelines empty.");
                return false;
            }

            var timelineRect = GetTimelineRect(window);
            var overlayBounds = new Rect();
            var hasOverlayBounds = false;
            if (overlay != null)
            {
                overlayBounds = GetOverlayBounds(window, overlay);
                hasOverlayBounds = true;
                if (overlayBounds.width <= 1f || overlayBounds.height <= 1f)
                {
                    LogWindow(window, "BuildRects: overlay bounds invalid.");
                    return false;
                }

                timelineRect = ResolveRectInOverlaySpace(overlay, timelineRect, overlayBounds);
            }

            timelineRect = ConstrainToKeyframeSide(window, overlay, timelineRect);
            if (timelineRect.width <= 1f || timelineRect.height <= 1f)
            {
                timelineRect = BuildKeyframeRectFromHierarchy(window, overlay);
            }

            var rowAreaRect = GetHierarchyTotalRect(window);
            if (hasOverlayBounds)
            {
                rowAreaRect = ResolveRectInOverlaySpace(overlay, rowAreaRect, overlayBounds);
            }

            if (rowAreaRect.width <= 1f || rowAreaRect.height <= 1f)
            {
                rowAreaRect = timelineRect;
            }
            else
            {
                timelineRect.yMin = rowAreaRect.yMin;
            }

            if (timelineRect.width <= 1f || timelineRect.height <= 1f)
            {
                LogWindow(window, "BuildRects: timeline rect invalid.");
                return false;
            }

            var scrollY = GetScrollY(hierarchyState);
            var visibleRect = timelineRect;
            visibleRect.height = Mathf.Max(0f, visibleRect.height - BottomScrollbarHeight);

            var selectedSet = GetSelectedIdSet(window, state, hierarchyState);
            var activeCurves = GetActiveCurves(state);
            var activeCurveSet = ToRefSet(activeCurves);
            var hasDopeSelectionFlag = HasSelectedDopelineFlag(dopelines);
            var hasExplicitSelection = selectedSet.Count > 0;
            var useFallbackSelectionSignals = !hasExplicitSelection;

            if (!hasExplicitSelection && activeCurveSet.Count == 0 && !hasDopeSelectionFlag)
            {
                LogWindow(window, $"BuildRects: no selection. dopelines={dopelines.Count}");
                return false;
            }

            if (UseTreeViewDirectRowProjection &&
                selectedSet.Count > 0 &&
                TryBuildRectsFromTreeRowsDirect(window, selectedSet, timelineRect, visibleRect, rowAreaRect.yMin, scrollY, lineRects))
            {
                DeduplicateHighlightRects(lineRects);
                return lineRects.Count > 0;
            }

            if (UseTreeViewRowCallbackFallback &&
                selectedSet.Count > 0 &&
                TryBuildRectsFromTreeRows(window, selectedSet, timelineRect, visibleRect, rowAreaRect.yMin, scrollY, lineRects))
            {
                DeduplicateHighlightRects(lineRects);
                return lineRects.Count > 0;
            }

            var yCursor = 0f;
            foreach (var dopeLine in dopelines)
            {
                if (dopeLine == null) continue;

                var rowHeight = GetDopelineHeight(dopeLine);
                if (rowHeight <= 0f)
                {
                    rowHeight = EditorGUIUtility.singleLineHeight;
                }

                var hierarchyId = GetDopelineHierarchyId(dopeLine);
                var isSelected = selectedSet.Contains(hierarchyId);
                if (!isSelected && useFallbackSelectionSignals && activeCurveSet.Count > 0)
                {
                    isSelected = IsDopelineActive(dopeLine, activeCurveSet);
                }

                if (!isSelected && useFallbackSelectionSignals)
                {
                    isSelected = IsDopelineSelectedByFlag(dopeLine);
                }

                if (!isSelected)
                {
                    yCursor += rowHeight;
                    continue;
                }

                var lineRect = new Rect(
                    timelineRect.xMin,
                    rowAreaRect.yMin + yCursor - scrollY,
                    timelineRect.width,
                    rowHeight);

                yCursor += rowHeight;

                if (!lineRect.Overlaps(visibleRect))
                    continue;

                var clipped = ClipRect(lineRect, visibleRect);
                if (clipped.width <= 0f || clipped.height <= 0f)
                    continue;

                lineRects.Add(clipped);
            }

            LogWindow(window, $"BuildRects: selected={selectedSet.Count}, active={activeCurveSet.Count}, dopeFlag={hasDopeSelectionFlag}, fallbackSignals={useFallbackSelectionSignals}, lines={lineRects.Count}, rowCache={GetRowCacheCount(window)}, timeline={timelineRect}, rowArea={rowAreaRect}");
            DeduplicateHighlightRects(lineRects);
            return lineRects.Count > 0;
        }

        private static void DeduplicateHighlightRects(List<Rect> rects)
        {
            if (rects == null || rects.Count < 2) return;

            rects.Sort(static (a, b) =>
            {
                var y = a.yMin.CompareTo(b.yMin);
                return y != 0 ? y : a.xMin.CompareTo(b.xMin);
            });

            const float yTolerance = 0.5f;
            const float xTolerance = 1.0f;
            var unique = new List<Rect>(rects.Count);

            var duplicated = false;
            foreach (var current in rects)
            {
                foreach (var existing in unique)
                {
                    var sameY = Mathf.Abs(existing.yMin - current.yMin) <= yTolerance &&
                                Mathf.Abs(existing.yMax - current.yMax) <= yTolerance;
                    var sameX = Mathf.Abs(existing.xMin - current.xMin) <= xTolerance &&
                                Mathf.Abs(existing.xMax - current.xMax) <= xTolerance;
                    if (!sameY || !sameX) continue;

                    duplicated = true;
                    break;
                }

                if (!duplicated)
                    unique.Add(current);
            }

            if (unique.Count == rects.Count)
                return;

            rects.Clear();
            rects.AddRange(unique);
        }

        private static bool TryBuildRectsFromTreeRows(
            EditorWindow window,
            HashSet<int> selectedSet,
            Rect timelineRect,
            Rect visibleRect,
            float rowOriginY,
            float scrollY,
            List<Rect> output)
        {
            if (window == null || selectedSet == null || selectedSet.Count == 0 || output == null) return false;
            if (!IMGUIHookByWindowId.TryGetValue(window.GetInstanceID(), out var hook) || hook == null) return false;

            if (hook.RowRectsById.Count == 0)
            {
                LogWindow(window, "TreeRow cache empty.");
                return false;
            }

            var any = false;
            foreach (var id in selectedSet)
            {
                if (!hook.RowRectsById.TryGetValue(id, out var rowRect)) continue;
                if (!TryBuildLineRectFromRowRect(rowRect, timelineRect, visibleRect, rowOriginY, scrollY, out var clipped)) continue;

                output.Add(clipped);
                any = true;
            }

            if (any) LogWindow(window, $"TreeRow cache rects: selected={selectedSet.Count}, added={output.Count}, cache={hook.RowRectsById.Count}");

            return any;
        }

        static int GetRowCacheCount(EditorWindow window)
        {
            if (window == null) return 0;
            if (IMGUIHookByWindowId.TryGetValue(window.GetInstanceID(), out var hook) && hook != null) return hook.RowRectsById.Count;

            return 0;
        }

        static bool TryBuildRectsFromTreeRowsDirect(
            EditorWindow window,
            HashSet<int> selectedSet,
            Rect timelineRect,
            Rect visibleRect,
            float rowOriginY,
            float scrollY,
            List<Rect> output)
        {
            if (window == null || selectedSet == null || selectedSet.Count == 0 || output == null) return false;

            var treeView = GetTreeView(window);
            if (treeView == null) return false;

            var data = GetTreeViewData(treeView);
            var gui = GetTreeViewGui(treeView);
            if (data == null || gui == null)
                return false;

            var rows = GetTreeRows(data);
            if (rows == null || rows.Count == 0)
                return false;

            var rowWidth = GetTreeViewTotalWidth(treeView);
            if (rowWidth <= 1f)
                rowWidth = 1f;

            var any = false;
            for (var row = 0; row < rows.Count; row++)
            {
                var item = rows[row];
                var id = GetHierarchyNodeId(item);
                if (id == 0 || !selectedSet.Contains(id)) continue;

                var rowRect = GetTreeGuiRowRect(gui, row, rowWidth);
                if (rowRect.height <= 0f) continue;
                if (!TryBuildLineRectFromRowRect(rowRect, timelineRect, visibleRect, rowOriginY, scrollY, out var clipped)) continue;

                output.Add(clipped);
                any = true;
            }

            if (any) LogWindow(window, $"TreeRow direct rects: selected={selectedSet.Count}, added={output.Count}, rows={rows.Count}");

            return any;
        }

        private static bool TryBuildLineRectFromRowRect(Rect rowRect, Rect timelineRect, Rect visibleRect, float rowOriginY, float scrollY, out Rect clippedResult)
        {
            clippedResult = new Rect();

            var rowHeight = rowRect.height;
            if (rowHeight <= 1f)
            {
                rowHeight = Mathf.Max(EditorGUIUtility.singleLineHeight, 16f);
            }

            if (rowHeight <= 1f)
            {
                return false;
            }

            var timelineWidth = timelineRect.width > 1f ? timelineRect.width : visibleRect.width;
            if (timelineWidth <= 1f || visibleRect.width <= 1f || visibleRect.height <= 1f)
            {
                return false;
            }

            float[] yCandidates =
            {
                rowOriginY + rowRect.y - scrollY,
                rowOriginY + rowRect.y,
                timelineRect.yMin + rowRect.y - scrollY,
                timelineRect.yMin + rowRect.y,
                rowRect.y - scrollY,
                rowRect.y
            };

            var bestScore = -1f;
            var bestRect = new Rect();

            foreach (var yCandidate in yCandidates)
            {
                var candidate = new Rect(timelineRect.xMin, yCandidate, timelineWidth, rowHeight);
                var clipped = ClipRect(candidate, visibleRect);
                var score = clipped.width * clipped.height;
                if (!(score > bestScore)) continue;

                bestScore = score;
                bestRect = clipped;
            }

            if (bestScore > 0f)
            {
                clippedResult = bestRect;
                return true;
            }

            // Last fallback: fill visible row width even if timeline X could not be resolved.
            foreach (var yCandidate in yCandidates)
            {
                var candidate = new Rect(visibleRect.xMin, yCandidate, visibleRect.width, rowHeight);
                var clipped = ClipRect(candidate, visibleRect);
                var score = clipped.width * clipped.height;
                if (!(score > bestScore)) continue;

                bestScore = score;
                bestRect = clipped;
            }

            if (bestScore <= 0f) return false;

            clippedResult = bestRect;
            return true;
        }

        private static object GetAnimationWindowState(EditorWindow window)
        {
            if (window == null || _animationWindowStateProp == null) return null;

            try
            {
                return _animationWindowStateProp.GetValue(window);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsCurveEditorVisible(object state)
        {
            if (state == null) return false;

            object value = null;
            if (_stateShowCurveEditorProp != null)
            {
                value = _stateShowCurveEditorProp.GetValue(state);
            }
            else if (_stateShowCurveEditorField != null)
            {
                value = _stateShowCurveEditorField.GetValue(state);
            }

            return value is true;
        }

        static object GetHierarchyState(object state)
        {
            if (state == null) return null;

            if (_stateHierarchyStateProp != null)
            {
                return _stateHierarchyStateProp.GetValue(state);
            }

            if (_stateHierarchyStateField != null)
            {
                return _stateHierarchyStateField.GetValue(state);
            }

            return null;
        }

        static HashSet<int> GetSelectedIdSet(EditorWindow window, object state, object hierarchyState)
        {
            var set = new HashSet<int>();

            var selectedIds = GetSelectedIdsFromHierarchyState(hierarchyState);
            if (selectedIds != null)
            {
                foreach (var id in ToIdSet(selectedIds))
                {
                    set.Add(id);
                }

                if (set.Count > 0) return set;
            }

            var selectedNodes = GetSelectedHierarchyNodes(state);
            if (selectedNodes != null)
            {
                foreach (var node in selectedNodes)
                {
                    var id = GetHierarchyNodeId(node);
                    if (id != 0)
                    {
                        set.Add(id);
                    }
                }

                if (set.Count > 0)
                {
                    return set;
                }
            }

            var tvApiIds = GetSelectedIdsViaTreeViewApi(window);
            if (tvApiIds != null)
            {
                foreach (var id in ToIdSet(tvApiIds))
                {
                    set.Add(id);
                }

                if (set.Count > 0)
                {
                    return set;
                }
            }

            var tvIds = GetSelectedIdsFromTreeView(window);
            if (tvIds != null)
            {
                foreach (var id in ToIdSet(tvIds))
                {
                    set.Add(id);
                }

                if (set.Count > 0)
                {
                    return set;
                }
            }

            var lastClickedId = GetLastClickedId(hierarchyState);
            if (lastClickedId != 0)
            {
                set.Add(lastClickedId);
            }
            else
            {
                var tvLastClicked = GetTreeViewLastClickedId(window);
                if (tvLastClicked != 0)
                    set.Add(tvLastClicked);
            }

            return set;
        }

        private static IList GetSelectedIdsViaTreeViewApi(EditorWindow window)
        {
            var treeView = GetTreeView(window);
            if (treeView == null) return null;

            if (_treeViewGetSelectionMethod == null)
            {
                _treeViewGetSelectionMethod = treeView.GetType().GetMethod("GetSelection", InstFlags, null, Type.EmptyTypes, null);
            }

            if (_treeViewGetSelectionMethod == null) return null;

            var selObj = _treeViewGetSelectionMethod.Invoke(treeView, null);
            return selObj as IList;
        }

        private static IList GetActiveCurves(object state)
        {
            if (state == null || _stateActiveCurvesProp == null) return null;

            return _stateActiveCurvesProp.GetValue(state) as IList;
        }

        private static IList GetDopelines(object state)
        {
            if (state == null) return null;

            if (_stateDopelinesProp != null)
            {
                return _stateDopelinesProp.GetValue(state) as IList;
            }

            if (_stateDopelinesField != null)
            {
                return _stateDopelinesField.GetValue(state) as IList;
            }

            return null;
        }

        private static IList GetSelectedIdsFromHierarchyState(object hierarchyState)
        {
            if (hierarchyState == null) return null;

            if (_hierarchySelectedIdsProp == null && _hierarchySelectedIdsField == null)
            {
                var t = hierarchyState.GetType();
                _hierarchySelectedIdsProp = t.GetProperty("selectedIDs", InstFlags);
                _hierarchySelectedIdsField = t.GetField("selectedIDs", InstFlags) ?? t.GetField("m_SelectedIDs", InstFlags);
                _hierarchySelectedIdsBackingField = t.GetField("m_SelectedIDs", InstFlags);
            }

            if (_hierarchySelectedIdsProp != null)
            {
                return _hierarchySelectedIdsProp.GetValue(hierarchyState) as IList;
            }

            if (_hierarchySelectedIdsField != null)
            {
                return _hierarchySelectedIdsField.GetValue(hierarchyState) as IList;
            }

            if (_hierarchySelectedIdsBackingField != null)
            {
                return _hierarchySelectedIdsBackingField.GetValue(hierarchyState) as IList;
            }

            return null;
        }

        private static int GetLastClickedId(object hierarchyState)
        {
            if (hierarchyState == null) return 0;

            if (_hierarchyLastClickedIdProp == null && _hierarchyLastClickedIdField == null && _hierarchyLastClickedIdBackingField == null)
            {
                var t = hierarchyState.GetType();
                _hierarchyLastClickedIdProp = t.GetProperty("lastClickedID", InstFlags);
                _hierarchyLastClickedIdField = t.GetField("lastClickedID", InstFlags);
                _hierarchyLastClickedIdBackingField = t.GetField("m_LastClickedID", InstFlags);
            }

            object idObj = null;
            if (_hierarchyLastClickedIdProp != null)
            {
                idObj = _hierarchyLastClickedIdProp.GetValue(hierarchyState);
            }
            else if (_hierarchyLastClickedIdField != null)
            {
                idObj = _hierarchyLastClickedIdField.GetValue(hierarchyState);
            }
            else if (_hierarchyLastClickedIdBackingField != null)
            {
                idObj = _hierarchyLastClickedIdBackingField.GetValue(hierarchyState);
            }

            return ToIntId(idObj);
        }

        private static IList GetSelectedHierarchyNodes(object state)
        {
            if (state == null || _stateSelectedHierarchyNodesProp == null) return null;

            return _stateSelectedHierarchyNodesProp.GetValue(state) as IList;
        }

        private static float GetScrollY(object hierarchyState)
        {
            if (hierarchyState == null) return 0f;

            if (_hierarchyScrollPosProp == null && _hierarchyScrollPosField == null)
            {
                var t = hierarchyState.GetType();
                _hierarchyScrollPosProp = t.GetProperty("scrollPos", InstFlags);
                _hierarchyScrollPosField = t.GetField("scrollPos", InstFlags);
            }

            object scrollObj = null;
            if (_hierarchyScrollPosProp != null)
            {
                scrollObj = _hierarchyScrollPosProp.GetValue(hierarchyState);
            }
            else if (_hierarchyScrollPosField != null)
            {
                scrollObj = _hierarchyScrollPosField.GetValue(hierarchyState);
            }

            if (scrollObj is Vector2 v)
            {
                return v.y;
            }

            return 0f;
        }

        private static Rect GetTimelineRect(EditorWindow window)
        {
            var animEditor = GetAnimEditor(window);
            if (animEditor == null)
            {
                return new Rect();
            }

            if (_animEditorDopeSheetField == null)
            {
                _animEditorDopeSheetField = animEditor.GetType().GetField("m_DopeSheet", InstFlags);
            }

            var dopeSheet = _animEditorDopeSheetField != null ? _animEditorDopeSheetField.GetValue(animEditor) : null;
            if (dopeSheet == null)
            {
                return new Rect();
            }

            if (_dopeSheetRectProp == null && _dopeSheetRectField == null)
            {
                var t = dopeSheet.GetType();
                while (t != null && _dopeSheetRectProp == null && _dopeSheetRectField == null)
                {
                    _dopeSheetRectProp = t.GetProperty("rect", InstFlags);
                    _dopeSheetRectField = t.GetField("rect", InstFlags) ?? t.GetField("m_Rect", InstFlags);
                    t = t.BaseType;
                }
            }

            object rectObj = null;
            if (_dopeSheetRectProp != null)
            {
                rectObj = _dopeSheetRectProp.GetValue(dopeSheet);
            }
            else if (_dopeSheetRectField != null)
            {
                rectObj = _dopeSheetRectField.GetValue(dopeSheet);
            }

            var rect = rectObj is Rect r ? r : new Rect();
            return NormalizeTimelineRect(window, rect);
        }

        private static object GetAnimEditor(EditorWindow window)
        {
            if (window == null) return null;

            if (_animationWindowAnimEditorProp != null)
            {
                var val = _animationWindowAnimEditorProp.GetValue(window);
                if (val != null)
                {
                    return val;
                }
            }

            if (_animationWindowAnimEditorField != null)
            {
                return _animationWindowAnimEditorField.GetValue(window);
            }

            return null;
        }

        private static object GetAnimHierarchy(EditorWindow window)
        {
            var animEditor = GetAnimEditor(window);
            if (animEditor == null) return null;

            if (_animEditorHierarchyField == null)
            {
                _animEditorHierarchyField = animEditor.GetType().GetField("m_Hierarchy", InstFlags);
            }

            return _animEditorHierarchyField != null ? _animEditorHierarchyField.GetValue(animEditor) : null;
        }

        private static Rect GetHierarchyTotalRect(EditorWindow window)
        {
            var treeView = GetTreeView(window);
            var tvRect = GetTreeViewTotalRect(treeView);
            if (tvRect is { width: > 1f, height: > 1f })
                return tvRect;

            var hierarchy = GetAnimHierarchy(window);
            if (hierarchy == null)
            {
                return new Rect();
            }

            if (_hierarchyGetTotalRectMethod == null)
            {
                _hierarchyGetTotalRectMethod = hierarchy.GetType().GetMethod("GetTotalRect", InstFlags, null, Type.EmptyTypes, null);
            }

            if (_hierarchyGetTotalRectMethod == null)
            {
                return new Rect();
            }

            var rectObj = _hierarchyGetTotalRectMethod.Invoke(hierarchy, null);
            return rectObj is Rect r ? r : new Rect();
        }

        private static object GetTreeViewState(EditorWindow window)
        {
            var treeView = GetTreeView(window);
            if (treeView == null) return null;

            if (_treeViewStateProp == null && _treeViewStateField == null)
            {
                var t = treeView.GetType();
                _treeViewStateProp = t.GetProperty("state", InstFlags);
                _treeViewStateField = t.GetField("state", InstFlags) ?? t.GetField("m_State", InstFlags);
            }

            if (_treeViewStateProp != null)
            {
                return _treeViewStateProp.GetValue(treeView);
            }

            if (_treeViewStateField != null)
            {
                return _treeViewStateField.GetValue(treeView);
            }

            return null;
        }

        private static object GetTreeViewData(object treeView)
        {
            if (treeView == null) return null;

            if (_treeViewDataProp == null)
            {
                _treeViewDataProp = treeView.GetType().GetProperty("data", InstFlags);
            }

            return _treeViewDataProp != null ? _treeViewDataProp.GetValue(treeView) : null;
        }

        private static object GetTreeViewGui(object treeView)
        {
            if (treeView == null) return null;

            if (_treeViewGuiProp == null)
            {
                _treeViewGuiProp = treeView.GetType().GetProperty("gui", InstFlags);
            }

            return _treeViewGuiProp != null ? _treeViewGuiProp.GetValue(treeView) : null;
        }

        private static IList GetTreeRows(object data)
        {
            if (data == null) return null;

            if (_treeDataGetRowsMethod == null)
            {
                _treeDataGetRowsMethod = data.GetType().GetMethod("GetRows", InstFlags, null, Type.EmptyTypes, null);
            }

            if (_treeDataGetRowsMethod == null) return null;

            return _treeDataGetRowsMethod.Invoke(data, null) as IList;
        }

        private static float GetTreeViewTotalWidth(object treeView)
        {
            var rect = GetTreeViewTotalRect(treeView);
            return rect.width > 0f ? rect.width : 0f;
        }

        private static Rect GetTreeViewTotalRect(object treeView)
        {
            if (treeView == null)
            {
                return new Rect();
            }

            if (_treeViewGetTotalRectMethod == null)
            {
                _treeViewGetTotalRectMethod = treeView.GetType().GetMethod("GetTotalRect", InstFlags, null, Type.EmptyTypes, null);
            }

            if (_treeViewGetTotalRectMethod == null)
            {
                return new Rect();
            }

            var rectObj = _treeViewGetTotalRectMethod.Invoke(treeView, null);
            if (rectObj is Rect rect)
            {
                return rect;
            }

            return new Rect();
        }

        private static Rect GetTreeGuiRowRect(object gui, int row, float rowWidth)
        {
            if (gui == null)
            {
                return new Rect();
            }

            if (_treeGuiGetRowRectMethod == null)
            {
                _treeGuiGetRowRectMethod = gui.GetType().GetMethod("GetRowRect", InstFlags, null, new[] { typeof(int), typeof(float) }, null);
            }

            if (_treeGuiGetRowRectMethod == null)
            {
                return new Rect();
            }

            var rectObj = _treeGuiGetRowRectMethod.Invoke(gui, new object[] { row, rowWidth });
            return rectObj is Rect r ? r : new Rect();
        }

        private static object GetTreeView(EditorWindow window)
        {
            var hierarchy = GetAnimHierarchy(window);
            if (hierarchy == null)
                return null;

            if (_hierarchyTreeViewField == null)
            {
                _hierarchyTreeViewField = hierarchy.GetType().GetField("m_TreeView", InstFlags);
            }

            return _hierarchyTreeViewField != null ? _hierarchyTreeViewField.GetValue(hierarchy) : null;
        }

        private static PropertyInfo GetTreeViewOnGuiRowCallbackProp(object treeView)
        {
            if (treeView == null)
                return null;

            if (_treeViewOnGuiRowCallbackProp == null)
            {
                _treeViewOnGuiRowCallbackProp = treeView.GetType().GetProperty("onGUIRowCallback", InstFlags);
            }

            return _treeViewOnGuiRowCallbackProp;
        }

        private static void EnsureTreeViewRowCallbackDisabled(EditorWindow window, int id)
        {
            var hook = GetOrCreateHook(id);
            if (hook == null) return;

            var treeView = GetTreeView(window);
            if (treeView == null) return;

            var callbackProp = GetTreeViewOnGuiRowCallbackProp(treeView);
            if (callbackProp == null || !callbackProp.CanWrite) return;

            // Clean up callback path left from older versions to avoid Animation window corruption after domain reload.
            if (hook.RowCallbackAssigned != null || hook.TreeView != null)
            {
                UnhookTreeViewRowCallback(hook);
            }

            var current = callbackProp.GetValue(treeView) as Delegate;
            if (current == null) return;

            var invocationList = current.GetInvocationList();
            Delegate rebuilt = null;
            var removed = false;
            foreach (var d in invocationList)
            {
                if (IsOwnedByHighlighter(d))
                {
                    removed = true;
                    continue;
                }

                rebuilt = rebuilt == null ? d : Delegate.Combine(rebuilt, d);
            }

            if (!removed) return;

            try
            {
                callbackProp.SetValue(treeView, rebuilt);
                LogWindow(window, "Removed stale highlighter TreeView row callback after domain reload.", true);
            }
            catch
            {
                // Intentionally ignored. Fallback path remains direct tree row reflection.
            }
        }

        private static bool IsOwnedByHighlighter(Delegate d)
        {
            if (d == null || d.Method == null) return false;
            if (IsOwnedByHighlighterType(d.Method.DeclaringType)) return true;

            var target = d.Target;
            return target != null && IsOwnedByHighlighterType(target.GetType());
        }

        private static bool IsOwnedByHighlighterType(Type type)
        {
            var t = type;
            while (t != null)
            {
                if (t == typeof(Controller)) return true;

                // Accept legacy class name and current namespace, but only in this assembly.
                if (t.Assembly == typeof(Controller).Assembly)
                {
                    var fullName = t.FullName;
                    if (!string.IsNullOrEmpty(fullName))
                    {
                        if (fullName.StartsWith("K13A.AnimationEditor.TimelineHighlighter.", StringComparison.Ordinal)) return true;
                        if (fullName.Contains("AnimationTimelinePropertyHighlighter")) return true;
                    }
                }

                t = t.DeclaringType;
            }

            return false;
        }

        private static void EnsureTreeViewRowCallback(EditorWindow window, int id)
        {
            var hook = GetOrCreateHook(id);
            if (hook == null) return;

            var treeView = GetTreeView(window);
            if (treeView == null)
            {
                LogWindow(window, "TreeView unavailable.");
                return;
            }

            var callbackProp = GetTreeViewOnGuiRowCallbackProp(treeView);
            if (callbackProp == null || !callbackProp.CanWrite)
            {
                LogWindow(window, "TreeView row callback property unavailable.");
                return;
            }

            if (ReferenceEquals(hook.TreeView, treeView) && hook.RowCallbackAssigned != null) return;

            UnhookTreeViewRowCallback(hook);
            hook.TreeView = treeView;
            hook.RowRectsById.Clear();
            hook.RowRectsFrame = -1;

            var current = callbackProp.GetValue(treeView) as Delegate;
            hook.OriginalRowCallback = current;

            Action<int, Rect> capture = (rowId, rowRect) =>
            {
                if (current != null)
                {
                    try
                    {
                        current.DynamicInvoke(rowId, rowRect);
                    }
                    catch
                    {
                        // ignored
                    }
                }

                hook.RowRectsById[rowId] = rowRect;
                hook.RowRectsFrame = Time.frameCount;
            };

            try
            {
                var assigned = Delegate.CreateDelegate(callbackProp.PropertyType, capture.Target, capture.Method);
                callbackProp.SetValue(treeView, assigned);
                hook.RowCallbackAssigned = assigned;
                LogWindow(window, "TreeView row callback attached.", true);
            }
            catch
            {
                hook.RowCallbackAssigned = null;
                LogWindow(window, "TreeView row callback attach failed.");
            }
        }

        private static void UnhookTreeViewRowCallback(ImguiHook hook)
        {
            if (hook?.TreeView == null || hook.RowCallbackAssigned == null)
                return;

            var callbackProp = GetTreeViewOnGuiRowCallbackProp(hook.TreeView);
            if (callbackProp != null && callbackProp.CanWrite)
            {
                var current = callbackProp.GetValue(hook.TreeView) as Delegate;
                if (ReferenceEquals(current, hook.RowCallbackAssigned))
                {
                    try
                    {
                        callbackProp.SetValue(hook.TreeView, hook.OriginalRowCallback);
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }

            hook.TreeView = null;
            hook.RowCallbackAssigned = null;
            hook.OriginalRowCallback = null;
            hook.RowRectsById.Clear();
            hook.RowRectsFrame = -1;
        }

        private static IList GetSelectedIdsFromTreeView(EditorWindow window)
        {
            var tvState = GetTreeViewState(window);
            if (tvState == null) return null;

            return GetSelectedIdsFromHierarchyState(tvState);
        }

        private static int GetTreeViewLastClickedId(EditorWindow window)
        {
            var tvState = GetTreeViewState(window);
            if (tvState == null) return 0;

            return GetLastClickedId(tvState);
        }

        private static void LogGlobal(string message, bool force = false)
        {
            HighlighterLog.LogGlobal(EnableDebugLogs, message, force);
        }

        private static void LogWindow(EditorWindow window, string message, bool force = false)
        {
            HighlighterLog.LogWindow(EnableDebugLogs, window, message, force);
        }
    }
}
#endif