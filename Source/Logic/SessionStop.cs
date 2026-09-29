using System;
using System.Collections.Concurrent;
using System.Threading;

namespace SirDiorama
{
    // The only way the effect stops: a name missing from the render engine, a
    // variant refused by the game's compiler, a fault on the render thread, or
    // a step already taken by another plugin.
    //
    // A stop lasts for the whole game session: the effect does not come back by
    // itself, the game's own rendering resumes, one line goes to the game log
    // and the player gets a notification as soon as a world is open.
    //
    // Callable from any thread: the render thread stops here, the main thread
    // delivers the notifications.
    public sealed class SessionStop
    {
        private readonly Action<string> m_log;
        private readonly ConcurrentQueue<string> m_notifications = new ConcurrentQueue<string>();
        private int m_stopped;
        private volatile string m_reason;

        public SessionStop(Action<string> log)
        {
            m_log = log ?? (_ => { });
        }

        public bool IsStopped
        {
            get { return Volatile.Read(ref m_stopped) != 0; }
        }

        // What stopped the effect, in the words given to the player.
        public string Reason
        {
            get { return m_reason; }
        }

        // True on the first stop only: later ones repeat neither the log line
        // nor the notification.
        public bool Stop(string detailForTheLog, string messageForThePlayer)
        {
            if (Interlocked.Exchange(ref m_stopped, 1) != 0)
                return false;

            m_reason = messageForThePlayer;
            m_log(detailForTheLog);
            m_notifications.Enqueue(messageForThePlayer);
            return true;
        }

        // A notice for the player that stops nothing (a toggle, for instance).
        public void Notify(string messageForThePlayer)
        {
            m_notifications.Enqueue(messageForThePlayer);
        }

        // Delivers what is waiting, but only once a world is open: in the main
        // menu, messages wait.
        public int Deliver(bool worldOpen, Action<string> show)
        {
            if (!worldOpen || show == null)
                return 0;

            var count = 0;
            string message;
            while (m_notifications.TryDequeue(out message))
            {
                show(message);
                count++;
            }
            return count;
        }
    }
}
