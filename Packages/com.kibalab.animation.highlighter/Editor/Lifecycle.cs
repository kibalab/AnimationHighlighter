#if UNITY_EDITOR
using System;
using UnityEditor;

namespace K13A.AnimationEditor.PropertyHighlighter
{
    sealed class LifecycleHooks
    {
        bool _isRegistered;
        EditorApplication.CallbackFunction _registeredUpdate;
        Action _registeredCleanupForQuit;
        AssemblyReloadEvents.AssemblyReloadCallback _registeredCleanupForReload;

        public void Register(EditorApplication.CallbackFunction updateAction, EditorApplication.CallbackFunction cleanupAction)
        {
            if (_isRegistered)
                return;

            _registeredUpdate = updateAction;
            _registeredCleanupForQuit = () => cleanupAction?.Invoke();
            _registeredCleanupForReload = () => cleanupAction?.Invoke();

            EditorApplication.update += _registeredUpdate;
            AssemblyReloadEvents.beforeAssemblyReload += _registeredCleanupForReload;
            EditorApplication.quitting += _registeredCleanupForQuit;
            _isRegistered = true;
        }

        public void Unregister(EditorApplication.CallbackFunction updateAction, EditorApplication.CallbackFunction cleanupAction)
        {
            if (!_isRegistered)
                return;

            EditorApplication.update -= _registeredUpdate;
            AssemblyReloadEvents.beforeAssemblyReload -= _registeredCleanupForReload;
            EditorApplication.quitting -= _registeredCleanupForQuit;

            _registeredUpdate = null;
            _registeredCleanupForQuit = null;
            _registeredCleanupForReload = null;
            _isRegistered = false;
        }
    }

    sealed class InitializationGate
    {
        readonly double _retryIntervalSeconds;
        bool _isReady;
        double _nextRetryTime;

        public InitializationGate(double retryIntervalSeconds)
        {
            _retryIntervalSeconds = Math.Max(0.05d, retryIntervalSeconds);
        }

        public bool EnsureReady(Func<bool> tryInitialize)
        {
            if (_isReady)
                return true;

            double now = EditorApplication.timeSinceStartup;
            if (now < _nextRetryTime)
                return false;

            _isReady = tryInitialize != null && tryInitialize();
            if (!_isReady)
                _nextRetryTime = now + _retryIntervalSeconds;

            return _isReady;
        }

        public bool ForceRefresh(Func<bool> tryInitialize)
        {
            _isReady = tryInitialize != null && tryInitialize();
            _nextRetryTime = _isReady ? 0d : EditorApplication.timeSinceStartup + _retryIntervalSeconds;
            return _isReady;
        }

        public void Reset()
        {
            _isReady = false;
            _nextRetryTime = 0d;
        }
    }
}
#endif