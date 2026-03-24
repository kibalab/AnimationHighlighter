#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace K13A.AnimationEditor.PropertyHighlighter
{
    static class SelectionResolver
    {
        static readonly BindingFlags InstFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        static PropertyInfo _dopeLineHierarchyIdProp;
        static FieldInfo _dopeLineHierarchyIdField;
        static PropertyInfo _dopeLineCurvesProp;
        static FieldInfo _dopeLineCurvesField;
        static PropertyInfo _dopeLineTallModeProp;
        static FieldInfo _dopeLineTallModeField;
        static PropertyInfo _dopeLineSelectedProp;
        static FieldInfo _dopeLineSelectedField;
        static PropertyInfo _hierarchyNodeIdProp;
        static FieldInfo _hierarchyNodeIdField;

        public static int GetDopelineHierarchyId(object dopeLine)
        {
            if (dopeLine == null)
                return 0;

            if (_dopeLineHierarchyIdProp == null && _dopeLineHierarchyIdField == null)
            {
                var t = dopeLine.GetType();
                _dopeLineHierarchyIdProp = t.GetProperty("hierarchyNodeID", InstFlags);
                _dopeLineHierarchyIdField = t.GetField("m_HierarchyNodeID", InstFlags);
            }

            object idObj = null;
            if (_dopeLineHierarchyIdProp != null)
                idObj = _dopeLineHierarchyIdProp.GetValue(dopeLine);
            else if (_dopeLineHierarchyIdField != null)
                idObj = _dopeLineHierarchyIdField.GetValue(dopeLine);

            return ToIntId(idObj);
        }

        public static bool IsDopelineActive(object dopeLine, HashSet<object> activeCurveSet)
        {
            if (dopeLine == null || activeCurveSet == null || activeCurveSet.Count == 0)
                return false;

            IList curves = GetDopelineCurves(dopeLine);
            if (curves == null || curves.Count == 0)
                return false;

            for (int i = 0; i < curves.Count; i++)
            {
                object curve = curves[i];
                if (curve != null && activeCurveSet.Contains(curve))
                    return true;
            }

            return false;
        }

        public static bool HasSelectedDopelineFlag(IList dopelines)
        {
            if (dopelines == null || dopelines.Count == 0)
                return false;

            for (int i = 0; i < dopelines.Count; i++)
            {
                if (IsDopelineSelectedByFlag(dopelines[i]))
                    return true;
            }

            return false;
        }

        public static bool IsDopelineSelectedByFlag(object dopeLine)
        {
            if (dopeLine == null)
                return false;

            if (_dopeLineSelectedProp == null && _dopeLineSelectedField == null)
            {
                var t = dopeLine.GetType();
                while (t != null && _dopeLineSelectedProp == null && _dopeLineSelectedField == null)
                {
                    _dopeLineSelectedProp = t.GetProperty("selected", InstFlags)
                                            ?? t.GetProperty("isSelected", InstFlags);
                    _dopeLineSelectedField = t.GetField("selected", InstFlags)
                                             ?? t.GetField("m_Selected", InstFlags)
                                             ?? t.GetField("isSelected", InstFlags);
                    t = t.BaseType;
                }
            }

            object v = null;
            try
            {
                if (_dopeLineSelectedProp != null)
                    v = _dopeLineSelectedProp.GetValue(dopeLine);
                else if (_dopeLineSelectedField != null)
                    v = _dopeLineSelectedField.GetValue(dopeLine);
            }
            catch
            {
                return false;
            }

            return v is bool b && b;
        }

        public static IList GetDopelineCurves(object dopeLine)
        {
            if (dopeLine == null)
                return null;

            if (_dopeLineCurvesProp == null && _dopeLineCurvesField == null)
            {
                var t = dopeLine.GetType();
                _dopeLineCurvesProp = t.GetProperty("curves", InstFlags);
                _dopeLineCurvesField = t.GetField("m_Curves", InstFlags);
            }

            object curvesObj = null;
            if (_dopeLineCurvesProp != null)
                curvesObj = _dopeLineCurvesProp.GetValue(dopeLine);
            else if (_dopeLineCurvesField != null)
                curvesObj = _dopeLineCurvesField.GetValue(dopeLine);

            return curvesObj as IList;
        }

        public static float GetDopelineHeight(object dopeLine)
        {
            if (dopeLine == null)
                return 0f;

            if (_dopeLineTallModeProp == null && _dopeLineTallModeField == null)
            {
                var t = dopeLine.GetType();
                _dopeLineTallModeProp = t.GetProperty("tallMode", InstFlags);
                _dopeLineTallModeField = t.GetField("tallMode", InstFlags);
            }

            bool isTall = false;
            if (_dopeLineTallModeProp != null)
            {
                object v = _dopeLineTallModeProp.GetValue(dopeLine);
                isTall = v is bool b && b;
            }
            else if (_dopeLineTallModeField != null)
            {
                object v = _dopeLineTallModeField.GetValue(dopeLine);
                isTall = v is bool b && b;
            }

            float line = EditorGUIUtility.singleLineHeight;
            return isTall ? line * 2f : line;
        }

        public static int GetHierarchyNodeId(object node)
        {
            if (node == null)
                return 0;

            if (_hierarchyNodeIdProp == null && _hierarchyNodeIdField == null)
            {
                var t = node.GetType();
                while (t != null && _hierarchyNodeIdProp == null && _hierarchyNodeIdField == null)
                {
                    _hierarchyNodeIdProp = t.GetProperty("id", InstFlags);
                    _hierarchyNodeIdField = t.GetField("id", InstFlags) ?? t.GetField("m_Id", InstFlags);
                    t = t.BaseType;
                }
            }

            object idObj = null;
            if (_hierarchyNodeIdProp != null)
                idObj = _hierarchyNodeIdProp.GetValue(node);
            else if (_hierarchyNodeIdField != null)
                idObj = _hierarchyNodeIdField.GetValue(node);

            return ToIntId(idObj);
        }

        public static HashSet<int> ToIdSet(IList ids)
        {
            var set = new HashSet<int>();
            if (ids == null)
                return set;

            for (int i = 0; i < ids.Count; i++)
            {
                int id = ToIntId(ids[i]);
                if (id != 0)
                    set.Add(id);
            }

            return set;
        }

        public static HashSet<object> ToRefSet(IList list)
        {
            var set = new HashSet<object>();
            if (list == null)
                return set;

            for (int i = 0; i < list.Count; i++)
            {
                object obj = list[i];
                if (obj != null)
                    set.Add(obj);
            }

            return set;
        }

        public static int ToIntId(object value)
        {
            if (value == null)
                return 0;

            if (value is int i)
                return i;

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return 0;
            }
        }
    }
}
#endif