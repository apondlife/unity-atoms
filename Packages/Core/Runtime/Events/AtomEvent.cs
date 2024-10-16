using System;
using System.Linq;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace UnityAtoms
{
    /// <summary>
    /// Generic base class for Events. Inherits from `AtomEventBase`.
    /// </summary>
    /// <typeparam name="T">The type for this Event.</typeparam>
    [EditorIcon("atom-icon-cherry")]
    public class AtomEvent<T> : AtomEventBase
    {
        public T InspectorRaiseValue { get => _inspectorRaiseValue; }

        /// <summary>
        /// Retrieve Replay Buffer as a List. This call will allocate memory so use sparsely.
        /// </summary>
        /// <returns></returns>
        public List<T> ReplayBuffer { get => _replayBuffer.ToList(); }

        public int ReplayBufferSize { get => _replayBufferSize; set => _replayBufferSize = value; }

        [SerializeField]
        protected event Action<T> _onEvent;

        /// <summary>
        /// The event replays the specified number of old values to new subscribers. Works like a ReplaySubject in Rx.
        /// </summary>
        [SerializeField]
        [Range(0, 10)]
        [Tooltip("The number of old values (between 0-10) being replayed when someone subscribes to this Event.")]
        private int _replayBufferSize = 1;

        private Queue<T> _replayBuffer = new Queue<T>();

#if UNITY_EDITOR
        /// <summary>
        /// Set of all AtomVariable instances in editor.
        /// </summary>
        private static HashSet<AtomEvent<T>> _instances = new HashSet<AtomEvent<T>>();
#endif

        private void OnEnable()
        {
#if UNITY_EDITOR
            if (EditorSettings.enterPlayModeOptionsEnabled)
            {
                _instances.Add(this);

                EditorApplication.playModeStateChanged -= HandlePlayModeStateChange;
                EditorApplication.playModeStateChanged += HandlePlayModeStateChange;
            }
#endif
        }


#if UNITY_EDITOR
        private static void HandlePlayModeStateChange(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode // BEFORE any GO is initialized:
                || state == PlayModeStateChange.EnteredEditMode) // AFTER Playmode stopped
            {
                foreach (var instance in _instances)
                {
                    instance._replayBuffer.Clear();
                    instance.UnregisterAll();
                }
            }
        }
#endif

        private void OnDisable()
        {
            // NOTE: This will not be called when deleting the Atom from the editor.
            // Therefore, there might still be null instances, but even though not ideal,
            // it should not cause any problems.
            // More info: https://issuetracker.unity3d.com/issues/ondisable-and-ondestroy-methods-are-not-called-when-a-scriptableobject-is-deleted-manually-in-project-window
#if UNITY_EDITOR
            _instances.Remove(this);
#endif
            // Clear all delegates when exiting play mode
            UnregisterAll();
        }

        /// <summary>
        /// Used when raising values from the inspector for debugging purposes.
        /// </summary>
        [SerializeField]
        [Tooltip("Value that will be used when using the Raise button in the editor inspector.")]
        private T _inspectorRaiseValue = default(T);

        /// <summary>
        /// Raise the Event.
        /// </summary>
        /// <param name="item">The value associated with the Event.</param>
        public void Raise(T item)
        {
#if !UNITY_ATOMS_GENERATE_DOCS && UNITY_EDITOR
            StackTraces.AddStackTrace(GetInstanceID(), StackTraceEntry.Create(item));
#endif
            base.Raise();
            _onEvent?.Invoke(item);
            AddToReplayBuffer(item);
        }

        /// <summary>
        /// Used in editor scipts since Raise is ambigious when using reflection to get method.
        /// </summary>
        /// <param name="item"></param>
        public void RaiseEditor(T item) => Raise(item);

        /// <summary>
        /// Register handler to be called when the Event triggers.
        /// </summary>
        /// <remarks>
        /// Replays the event buffer to the handler.
        /// </remarks>
        /// <param name="action">The handler.</param>
        public void Register(Action<T> action)
        {
            Register(action, replayEventsBuffer: true);
        }

        /// <summary>
        /// Register handler to be called when the Event triggers.
        /// </summary>
        /// <param name="action">The handler.</param>
        /// <param name="replayEventsBuffer">If this replays the events buffer to the handler.</param>
        public void Register(Action<T> action, bool replayEventsBuffer)
        {
            _onEvent += action;
            if (replayEventsBuffer)
            {
                ReplayBufferToSubscriber(action);
            }
        }

        /// <summary>
        /// Unregister handler that was registered using the `Register` method.
        /// </summary>
        /// <param name="action">The handler.</param>
        public void Unregister(Action<T> action)
        {
            _onEvent -= action;
        }

        /// <summary>
        /// Unregister all handlers that were registered using the `Register` method.
        /// </summary>
        public override void UnregisterAll()
        {
            base.UnregisterAll();
            _onEvent = null;
        }

        /// <summary>
        /// Register a Listener that in turn trigger all its associated handlers when the Event triggers.
        /// </summary>
        /// <param name="listener">The Listener to register.</param>
        /// <param name="replayEventsBuffer">If this replays the events buffer to the new listener. Defaults to true.</param>
        public void RegisterListener(IAtomListener<T> listener, bool replayEventsBuffer = true)
        {
            _onEvent += listener.OnEventRaised;
            if (replayEventsBuffer)
            {
                ReplayBufferToSubscriber(listener.OnEventRaised);
            }
        }

        /// <summary>
        /// Unregister a listener that was registered using the `RegisterListener` method.
        /// </summary>
        /// <param name="listener">The Listener to unregister.</param>
        public void UnregisterListener(IAtomListener<T> listener)
        {
            _onEvent -= listener.OnEventRaised;
        }

        #region Observable
        /// <summary>
        /// Turn the Event into an `IObservable&lt;T&gt;`. Makes Events compatible with for example UniRx.
        /// </summary>
        /// <returns>The Event as an `IObservable&lt;T&gt;`.</returns>
        public IObservable<T> Observe()
        {
            return new ObservableEvent<T>(Register, Unregister);
        }
        #endregion // Observable

        protected void AddToReplayBuffer(T item)
        {
            if (_replayBufferSize > 0)
            {
                while (_replayBuffer.Count >= _replayBufferSize) { _replayBuffer.Dequeue(); }
                _replayBuffer.Enqueue(item);
            }
        }

        private void ReplayBufferToSubscriber(Action<T> action)
        {
            if (_replayBufferSize > 0 && _replayBuffer.Count > 0)
            {
                var enumerator = _replayBuffer.GetEnumerator();
                try
                {
                    while (enumerator.MoveNext())
                    {
                        action(enumerator.Current);
                    }
                }
                finally
                {
                    enumerator.Dispose();
                }
            }
        }
    }
}
