using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityObject = UnityEngine.Object;
using HostView = UnityEngine.ScriptableObject;
using View = UnityEngine.ScriptableObject;
using ContainerWindow = UnityEngine.ScriptableObject;

namespace FullscreenEditor {
    /// <summary>Represents the pyramid containing all the elements that make up a window.</summary>
    [Serializable]
    public struct ViewPyramid {

        /// <summary>The actual window, may be null if the pyramid was created from a view or container.</summary>
        public EditorWindow Window {
            get {
#if UNITY_6000_3_OR_NEWER
                return ResolveCachedObject(ref m_window, m_windowEntityId);
#else
                return ResolveCachedObject(ref m_window, m_windowInstanceID);
#endif
            }
            set {
                m_window = value;
#if UNITY_6000_3_OR_NEWER
                m_windowEntityId = m_window ? m_window.GetEntityId() : EntityId.None;
#else
                m_windowInstanceID = m_window ? m_window.GetInstanceID() : 0;
#endif
            }
        }

        /// <summary>View that controls how the window (and child view) are drawn.</summary>
        public View View {
            get {
#if UNITY_6000_3_OR_NEWER
                return ResolveCachedObject(ref m_view, m_viewEntityId);
#else
                return ResolveCachedObject(ref m_view, m_viewInstanceID);
#endif
            }
            set {
                value.EnsureOfType(Types.View);
                m_view = value;
#if UNITY_6000_3_OR_NEWER
                m_viewEntityId = m_view ? m_view.GetEntityId() : EntityId.None;
#else
                m_viewInstanceID = m_view ? m_view.GetInstanceID() : 0;
#endif
            }
        }

        /// <summary>The native window.</summary>
        public ContainerWindow Container {
            get {
#if UNITY_6000_3_OR_NEWER
                return ResolveCachedObject(ref m_container, m_containerEntityId);
#else
                return ResolveCachedObject(ref m_container, m_containerInstanceID);
#endif
            }
            set {
                value.EnsureOfType(Types.ContainerWindow);
                m_container = value;
#if UNITY_6000_3_OR_NEWER
                m_containerEntityId = m_container ? m_container.GetEntityId() : EntityId.None;
#else
                m_containerInstanceID = m_container ? m_container.GetInstanceID() : 0;
#endif
            }
        }

        [SerializeField] private EditorWindow m_window;
        [SerializeField] private View m_view;
        [SerializeField] private ContainerWindow m_container;

#if UNITY_6000_3_OR_NEWER
        private static T ResolveCachedObject<T>(ref T value, EntityId entityId) where T : UnityObject {
            if (!value && entityId != EntityId.None)
                value = EditorUtility.EntityIdToObject(entityId) as T;
            return value;
        }
#else
        private static T ResolveCachedObject<T>(ref T value, int instanceID) where T : UnityObject {
            if (!value && instanceID != 0)
                value = EditorUtility.InstanceIDToObject(instanceID) as T;
            return value;
        }
#endif

#if UNITY_6000_3_OR_NEWER
        [SerializeField] private EntityId m_windowEntityId;
        [SerializeField] private EntityId m_viewEntityId;
        [SerializeField] private EntityId m_containerEntityId;
#else
        [SerializeField] private int m_windowInstanceID;
        [SerializeField] private int m_viewInstanceID;
        [SerializeField] private int m_containerInstanceID;
#endif

        /// <summary>Create a new instance and automatically assigns the window, view and container.</summary>
        public ViewPyramid(ScriptableObject viewOrWindow) {

            if (!viewOrWindow) {
                m_window = null;
                m_view = null;
                m_container = null;
            } else if (viewOrWindow.IsOfType(typeof(EditorWindow))) {
                m_window = viewOrWindow as EditorWindow;
                m_view = m_window.GetFieldValue<View>("m_Parent");
                m_container = m_view.GetPropertyValue<ContainerWindow>("window");
            } else if (viewOrWindow.IsOfType(Types.View)) {
                m_window = null;
                m_view = viewOrWindow;
                m_container = m_view.GetPropertyValue<ContainerWindow>("window");
            } else if (viewOrWindow.IsOfType(Types.ContainerWindow)) {
                m_window = null;
                m_view = viewOrWindow.GetPropertyValue<ContainerWindow>("rootView");
                m_container = viewOrWindow;
            } else {
                throw new ArgumentException("Param must be of type EditorWindow, View or ContainerWindow", "viewOrWindow");
            }

            if (!m_window && m_view && m_view.IsOfType(Types.HostView))
                m_window = m_view.GetPropertyValue<EditorWindow>("actualView");

#if UNITY_6000_3_OR_NEWER
            m_windowEntityId = m_window ? m_window.GetEntityId() : EntityId.None;
            m_viewEntityId = m_view ? m_view.GetEntityId() : EntityId.None;
            m_containerEntityId = m_container ? m_container.GetEntityId() : EntityId.None;
#else
            m_windowInstanceID = m_window ? m_window.GetInstanceID() : 0;
            m_viewInstanceID = m_view ? m_view.GetInstanceID() : 0;
            m_containerInstanceID = m_container ? m_container.GetInstanceID() : 0;
#endif

        }

    }
}
