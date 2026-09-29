using System.Collections.Generic;

namespace CurveEditor.Core
{
    /// <summary> Snapshot-based undo/redo. States must be treated as immutable once pushed. </summary>
    public sealed class UndoStack<T>
    {
        private readonly List<T> undo = new List<T>();
        private readonly Stack<T> redo = new Stack<T>();
        private readonly int capacity;

        public UndoStack(int capacity = 500)
        {
            this.capacity = capacity;
        }

        public bool CanUndo => undo.Count > 0;

        public bool CanRedo => redo.Count > 0;

        /// <summary> Records the state as it was before a change. Clears redo history. </summary>
        public void Push(T before)
        {
            undo.Add(before);
            if (undo.Count > capacity) undo.RemoveAt(0);
            redo.Clear();
        }

        public bool TryUndo(T current, out T previous)
        {
            previous = default;
            if (undo.Count == 0) return false;
            previous = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            redo.Push(current);
            return true;
        }

        public bool TryRedo(T current, out T next)
        {
            next = default;
            if (redo.Count == 0) return false;
            next = redo.Pop();
            undo.Add(current);
            return true;
        }

        public void Clear()
        {
            undo.Clear();
            redo.Clear();
        }
    }
}
