// Background pre-generation of the level the player is about to start, so pressing Play never stalls on the generator +
// solver (late boards of 12 colors can take a few hundred ms on a phone). The Levels module is pure C# with no shared
// mutable state, so LevelGenerator.Generate is safe on a worker thread once the world catalog is loaded.
using System;
using System.Threading.Tasks;

namespace PotionPop.Levels
{
    public static class LevelPrefetch
    {
        static readonly object Gate = new object();
        static int _level = -1;
        static Task<LevelDefinition> _task;

        /// <summary>Starts generating `level` on a worker thread (no-op if it is already being prepared).
        /// Call from the main thread (Home shown, level won).</summary>
        public static void Prefetch(int level)
        {
            if (level < 1) return;
            lock (Gate)
            {
                if (_level == level && _task != null && !_task.IsFaulted) return;
            }
            // The catalog loads through Resources.Load, which only works on the main thread: touch it here so the
            // worker only reads cached data.
            var areas = Catalog.Areas;
            if (areas == null) return;
            Task<LevelDefinition> task;
            try { task = Task.Run(() => LevelGenerator.Generate(level)); }
            catch (Exception) { return; }   // no thread pool: GameSession just generates synchronously
            lock (Gate)
            {
                _level = level;
                _task = task;
            }
        }

        /// <summary>
        /// The pre-generated definition of `level` (waiting for it if it is still being built — never slower than
        /// starting over), or null when nothing was prefetched for this level. Consumes the cached result.
        /// </summary>
        public static LevelDefinition Take(int level)
        {
            Task<LevelDefinition> task;
            lock (Gate)
            {
                if (_level != level || _task == null) return null;
                task = _task;
                _task = null;
                _level = -1;
            }
            try { return task.Result; }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// Non-blocking and non-consuming: the definition of <paramref name="level"/> if it was prefetched and is ready
        /// (Level Start popup / level map show the star goal and the level's features without stalling).
        /// </summary>
        public static bool TryPeek(int level, out LevelDefinition def)
        {
            def = null;
            Task<LevelDefinition> task;
            lock (Gate)
            {
                if (_level != level || _task == null) return false;
                task = _task;
            }
            if (!task.IsCompleted || task.IsFaulted || task.IsCanceled) return false;
            try { def = task.Result; }
            catch (Exception) { def = null; }
            return def != null;
        }

        /// <summary>Drops any pending result (e.g. after a debug level jump).</summary>
        public static void Clear()
        {
            lock (Gate)
            {
                _task = null;
                _level = -1;
            }
        }
    }
}
